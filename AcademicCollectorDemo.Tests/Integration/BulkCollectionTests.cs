using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Background;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.SqlImport;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class BulkCollectionTests(SqlServerFixture fixture)
{
    private static BulkCollectionSubmitRequest Input() => new()
    {
        BatchId = Guid.NewGuid(), Researchers = [new()
        {
            SourceResearcherId = "synthetic-1", WebOfScienceId = "A-1234-2020"
        }]
    };

    [Fact]
    public async Task SubmitAsync_RepeatedBatch_IsIdempotentAndRejectsChangedInput()
    {
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var input = Input();
        var first = await service.SubmitAsync(input);
        var second = await service.SubmitAsync(input);
        Assert.Equal(first.Jobs.Single().Id, second.Jobs.Single().Id);
        input.Researchers[0].WebOfScienceId = "B-1234-2020";
        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(input));
    }

    [Fact]
    public async Task SubmitAsync_InvalidAndDuplicateRows_ReportsRejectedRows()
    {
        using var scope = fixture.Services.CreateScope();
        var input = Input();
        input.Researchers.Add(new() { SourceResearcherId = "synthetic-2", Orcid = "invalid" });
        input.Researchers.Add(new() { SourceResearcherId = "synthetic-3", WebOfScienceId = "A-1234-2020" });
        var result = await scope.ServiceProvider.GetRequiredService<BulkCollectionService>().SubmitAsync(input);
        Assert.Equal(1, result.Counts[BulkJobStatus.Pending]);
        Assert.Equal(2, result.Counts[BulkJobStatus.Rejected]);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task ProcessNextAsync_AbandonedJob_ResumesAndSavesResult()
    {
        await using var services = BuildServices(new FakeApplicationService(false));
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var input = Input();
        await service.SubmitAsync(input);
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        // Isolate this test's job from other batches in the shared fixture.
        await db.BulkCollectionJobs.Where(job => job.Status == BulkJobStatus.Pending)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAt, DateTime.UtcNow.AddDays(5)));
        await db.BulkCollectionJobs.Where(job => job.BatchId == input.BatchId)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, BulkJobStatus.Running));
        db.ChangeTracker.Clear();
        Assert.True(await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync());
        var status = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.True(status.IsComplete);
        Assert.Equal(BulkJobStatus.Succeeded, status.Jobs.Single().Status);
        Assert.Equal(42, status.Jobs.Single().ResearcherId);
    }

    [Fact]
    public async Task ProcessNextAsync_ProviderCooldown_RetriesThenReportsPartial()
    {
        await using var services = BuildServices(new FakeApplicationService(true));
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await db.BulkCollectionJobs.Where(job => job.Status == BulkJobStatus.Pending || job.Status == BulkJobStatus.RetryWaiting)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAt, DateTime.UtcNow.AddDays(5)));
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var input = Input();
        await service.SubmitAsync(input);
        var processor = scope.ServiceProvider.GetRequiredService<BulkJobProcessor>();
        Assert.True(await processor.ProcessNextAsync());
        var status = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.RetryWaiting, status.Jobs.Single().Status);
        Assert.True(status.Jobs.Single().NextAttemptAt > DateTime.UtcNow.AddMinutes(50));
        await db.BulkCollectionJobs.Where(job => job.BatchId == input.BatchId)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1)));
        db.ChangeTracker.Clear();
        Assert.True(await processor.ProcessNextAsync());
        status = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.Partial, status.Jobs.Single().Status);
        Assert.True(status.IsComplete);
    }

    [Fact]
    public async Task ProcessNextAsync_AnotherWorkerOwnsLock_DoesNotClaimJob()
    {
        await using var gate = await SqlApplicationLock.TryAcquireAsync(fixture.ConnectionString,
            "AcademicCollector.BulkWorker", 0, default);
        Assert.NotNull(gate);
        using var scope = fixture.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync());
    }

    [Fact]
    public async Task ImportAsync_ConfiguredSqlColumns_QueuesMappedRows()
    {
        await using var services = BuildServices(new FakeApplicationService(false), new()
        {
            ["BulkSqlSource:Enabled"] = "true",
            ["BulkSqlSource:Query"] = "SELECT 'person-1' AS Employee, 'A-1234-2020' AS wos",
            ["BulkSqlSource:SourceResearcherIdColumn"] = "Employee",
            ["BulkSqlSource:WebOfScienceIdColumn"] = "wos",
            ["ConnectionStrings:BulkSource"] = fixture.ConnectionString
        });
        using var scope = services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<BulkSqlImporter>().ImportAsync(Guid.NewGuid());
        Assert.Equal("person-1", result.Jobs.Single().SourceResearcherId);
        Assert.Equal(BulkJobStatus.Pending, result.Jobs.Single().Status);
    }

    [Fact]
    public async Task Worker_Enabled_ProcessesPersistedQueue()
    {
        await using var services = BuildServices(new FakeApplicationService(false), new()
        {
            ["BulkCollection:WorkerEnabled"] = "true", ["BulkCollection:PollSeconds"] = "1"
        });
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await db.BulkCollectionJobs.Where(job => job.Status == BulkJobStatus.Pending || job.Status == BulkJobStatus.RetryWaiting)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAt, DateTime.UtcNow.AddDays(5)));
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var input = Input();
        await service.SubmitAsync(input);
        var worker = services.GetServices<IHostedService>().OfType<BulkCollectionWorker>().Single();
        await worker.StartAsync(default);
        try
        {
            BulkCollectionStatusResponse result = new();
            for (int attempt = 0; attempt < 50; attempt++)
            {
                result = await service.GetStatusAsync(new() { BatchId = input.BatchId });
                if (result.IsComplete) break;
                await Task.Delay(100);
            }
            Assert.Equal(BulkJobStatus.Succeeded, result.Jobs.Single().Status);
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    [Fact]
    public async Task ProcessNextAsync_InterruptedFinalAttempt_DoesNotRepeatForever()
    {
        await using var services = BuildServices(new FakeApplicationService(false));
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var input = Input();
        await service.SubmitAsync(input);
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await db.BulkCollectionJobs.Where(job => job.BatchId == input.BatchId)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, BulkJobStatus.Running)
                .SetProperty(job => job.Attempts, 2));
        db.ChangeTracker.Clear();
        await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync();
        var result = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.Failed, result.Jobs.Single().Status);
        Assert.Equal(2, result.Jobs.Single().Attempts);
    }

    [Fact]
    public async Task Api_SubmitAndStatus_ReturnsDurableBatchWithoutRunningProviders()
    {
        using var host = new HostProcess(fixture.ConnectionString);
        await host.WaitUntilReadyAsync();
        var input = Input();
        using var submit = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/Bulk/Submit", input);
        submit.EnsureSuccessStatusCode();
        var response = await submit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(response.GetProperty("WorkerEnabled").GetBoolean());
        Assert.Equal(input.BatchId, response.GetProperty("BatchId").GetGuid());
        using var status = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/Bulk/Status",
            new { input.BatchId });
        status.EnsureSuccessStatusCode();
        Assert.Equal(1, (await status.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("Counts").GetProperty("Pending").GetInt32());
    }

    private ServiceProvider BuildServices(FakeApplicationService service, Dictionary<string, string?>? extra = null)
    {
        var settings = extra ?? [];
        settings["ConnectionStrings:AcademicDatabase"] = fixture.ConnectionString;
        settings["BulkCollection:MaximumAttempts"] = "2";
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAcademicPerformanceModule(configuration);
        services.AddSingleton<IAcademicPerformanceApplicationService>(service);
        return services.BuildServiceProvider();
    }

    private sealed class FakeApplicationService(bool fail) : IAcademicPerformanceApplicationService
    {
        public Task<AcademicDataResponse> CollectAsync(AcademicDataCollectRequest request)
        {
            if (fail) ProviderCallScope.Record("WebOfScience", true, DateTime.UtcNow.AddHours(1));
            return Task.FromResult(new AcademicDataResponse
            {
                IsSaved = true, Researcher = new() { Id = 42, WebOfScienceProfile = new() }
            });
        }
        public Task<AcademicDataResponse> GetResearcherAsync(AcademicResearcherRequest request) => throw new NotSupportedException();
        public Task<AcademicPublicationListResponse> ListPublicationsAsync(AcademicPublicationListRequest request) => throw new NotSupportedException();
        public Task<AcademicPublicationSelectionResponse> SavePublicationSelectionsAsync(AcademicPublicationSelectionRequest request) => throw new NotSupportedException();
    }
}
