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
            PersonelId = "synthetic-1", WebOfScienceId = "A-1234-2020"
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
        input.Researchers.Add(new() { PersonelId = "synthetic-2", Orcid = "invalid" });
        input.Researchers.Add(new() { PersonelId = "synthetic-3", WebOfScienceId = "A-1234-2020" });
        input.Researchers.Add(new() { PersonelId = "synthetic-4", WebOfScienceId = "B-1234-2020" });
        var result = await scope.ServiceProvider.GetRequiredService<BulkCollectionService>().SubmitAsync(input);
        Assert.Equal(1, result.Counts[BulkJobStatus.Pending]);
        Assert.Equal(3, result.Counts[BulkJobStatus.Rejected]);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SubmitAsync_MessyOptionalFields_QueuesCanonicalInputAndReturnsDurableWarnings()
    {
        await using var services = BuildServices(new FakeApplicationService(false));
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await db.BulkCollectionJobs.Where(job => job.Status == BulkJobStatus.Pending ||
                job.Status == BulkJobStatus.RetryWaiting)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAt, DateTime.UtcNow.AddDays(5)));
        var input = new BulkCollectionSubmitRequest
        {
            BatchId = Guid.NewGuid(), Researchers = [new()
            {
                PersonelId = "synthetic-cleanup",
                Orcid = " (https://orcid.org/0000-0002-1825-009X) ,",
                GoogleScholarId = "person@example.test",
                WebOfScienceId = "https://www.webofscience.com/wos/author/record/A-1234-2020.",
                ScopusId = " unsupported-scopus-value "
            }]
        };

        var submitted = await service.SubmitAsync(input);
        Assert.Equal(BulkJobStatus.Pending, submitted.Jobs.Single().Status);
        Assert.Equal(2, submitted.Jobs.Single().Warnings.Count);
        string persisted = (await db.BulkCollectionJobs.SingleAsync(job => job.BatchId == input.BatchId)).InputJson;
        Assert.Contains("0000-0002-1825-009X", persisted);
        Assert.Contains("A-1234-2020", persisted);
        Assert.Contains("person@example.test", persisted);
        Assert.Contains(" unsupported-scopus-value ", persisted);
        using (JsonDocument document = JsonDocument.Parse(persisted))
        {
            Assert.Equal("person@example.test", document.RootElement.GetProperty("OriginalInput")
                .GetProperty("ScholarID").GetString());
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Input")
                .GetProperty("ScholarID").ValueKind);
        }

        Assert.True(await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync());
        var status = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.Succeeded, status.Jobs.Single().Status);
        Assert.NotNull(status.Jobs.Single().StartedAt);
        Assert.NotNull(status.Jobs.Single().CompletedAt);
        Assert.Equal(2, status.Jobs.Single().Warnings.Count);
        var fake = (FakeApplicationService)scope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
        Assert.Equal("0000-0002-1825-009X", fake.LastRequest!.Orcid);
        Assert.Equal("A-1234-2020", fake.LastRequest.WebOfScienceResearcherId);
        Assert.Equal("synthetic-cleanup", fake.LastRequest.PersonelId);
        Assert.Equal("unsupported-scopus-value", fake.LastRequest.ScopusId);
        Assert.Null(fake.LastRequest.GoogleScholarId);
    }

    [Fact]
    public async Task SubmitAsync_SharedProviderAcrossPersonnel_RejectsEveryConflictingRow()
    {
        using var scope = fixture.Services.CreateScope();
        var request = new BulkCollectionSubmitRequest { BatchId = Guid.NewGuid(), Researchers =
        [
            new() { PersonelId = "synthetic-a", Orcid = "0000-0002-1825-009X" },
            new() { PersonelId = "synthetic-b", Orcid = "https://orcid.org/0000-0002-1825-009X" },
            new() { PersonelId = "synthetic-c", WebOfScienceId = "A-4321-2020" }
        ]};

        var result = await scope.ServiceProvider.GetRequiredService<BulkCollectionService>().SubmitAsync(request);

        Assert.Equal(2, result.Counts[BulkJobStatus.Rejected]);
        Assert.Equal(1, result.Counts[BulkJobStatus.Pending]);
        Assert.All(result.Jobs.Where(job => job.Status == BulkJobStatus.Rejected),
            job => Assert.Contains("manual review", job.Message));
    }

    [Fact]
    public async Task SubmitAsync_DuplicatePersonelId_RejectsEveryDuplicate()
    {
        using var scope = fixture.Services.CreateScope();
        var request = new BulkCollectionSubmitRequest { BatchId = Guid.NewGuid(), Researchers =
        [
            new() { PersonelId = "same-person", Orcid = "0000-0002-1825-009X" },
            new() { PersonelId = "same-person", WebOfScienceId = "A-4321-2020" }
        ]};

        var result = await scope.ServiceProvider.GetRequiredService<BulkCollectionService>().SubmitAsync(request);

        Assert.Equal(2, result.Counts[BulkJobStatus.Rejected]);
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
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, BulkJobStatus.Running)
                // Simulate the InputJson shape saved before warning envelopes were introduced.
                .SetProperty(job => job.InputJson, JsonSerializer.Serialize(input.Researchers.Single())));
        db.ChangeTracker.Clear();
        Assert.True(await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync());
        var status = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.True(status.IsComplete);
        Assert.Equal(BulkJobStatus.Succeeded, status.Jobs.Single().Status);
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
    public async Task ProcessNextAsync_PersistenceDataTooLong_DoesNotRetryProviderFailure()
    {
        await using var services = BuildServices(new FakeApplicationService(true, "PersistenceDataTooLong"));
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await db.BulkCollectionJobs.Where(job => job.Status == BulkJobStatus.Pending ||
                job.Status == BulkJobStatus.RetryWaiting)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAt, DateTime.UtcNow.AddDays(5)));
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var input = Input();
        await service.SubmitAsync(input);

        Assert.True(await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync());

        var status = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.Failed, status.Jobs.Single().Status);
        Assert.Equal(1, status.Jobs.Single().Attempts);
        Assert.NotNull(status.Jobs.Single().CompletedAt);
        Assert.Contains("metadata exceeds", status.Jobs.Single().Message);
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
            ["BulkSqlSource:Query"] = "SELECT 'person-1' AS PersonelID, 'bad' AS ORCID, " +
                "'A-1234-2020' AS ResearcherID, NULL AS ScholarID, 'raw-scopus' AS ScopusID",
            ["BulkSqlSource:PersonelIdColumn"] = "PersonelID",
            ["BulkSqlSource:OrcidColumn"] = "ORCID",
            ["BulkSqlSource:WebOfScienceIdColumn"] = "ResearcherID",
            ["BulkSqlSource:GoogleScholarIdColumn"] = "ScholarID",
            ["BulkSqlSource:ScopusIdColumn"] = "ScopusID",
            ["ConnectionStrings:BulkSource"] = fixture.ConnectionString
        });
        using var scope = services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<BulkSqlImporter>().ImportAsync(Guid.NewGuid());
        Assert.Equal("person-1", result.Jobs.Single().PersonelId);
        Assert.Equal(BulkJobStatus.Pending, result.Jobs.Single().Status);
        Assert.Equal(2, result.Jobs.Single().Warnings.Count);
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
        input.Researchers.Single().GoogleScholarId = "damaged optional value";
        using var submit = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/Bulk/Submit", input);
        submit.EnsureSuccessStatusCode();
        var response = await submit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(response.GetProperty("WorkerEnabled").GetBoolean());
        Assert.Equal(input.BatchId, response.GetProperty("BatchId").GetGuid());
        using var status = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/Bulk/Status",
            new { input.BatchId });
        status.EnsureSuccessStatusCode();
        JsonElement statusJson = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, statusJson.GetProperty("Counts").GetProperty("Pending").GetInt32());
        Assert.Equal(1, statusJson.GetProperty("Jobs")[0].GetProperty("Warnings").GetArrayLength());
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

    private sealed class FakeApplicationService(bool fail, string? failureCode = null) : IAcademicPerformanceApplicationService
    {
        public AcademicDataCollectRequest? LastRequest { get; private set; }

        public Task<AcademicDataResponse> CollectAsync(AcademicDataCollectRequest request)
        {
            LastRequest = request;
            if (fail) ProviderCallScope.Record("WebOfScience", true, DateTime.UtcNow.AddHours(1));
            return Task.FromResult(new AcademicDataResponse
            {
                IsSaved = failureCode is null, FailureCode = failureCode, Researcher = new()
                {
                    PersonelId = request.PersonelId,
                    OrcidProfile = request.Orcid is null ? null : new(),
                    OpenAlexProfile = request.Orcid is null ? null : new(),
                    GoogleScholarProfile = request.GoogleScholarId is null ? null : new(),
                    WebOfScienceProfile = request.WebOfScienceResearcherId is null ? null : new()
                }
            });
        }
        public Task<AcademicDataResponse> GetResearcherAsync(AcademicResearcherRequest request) => throw new NotSupportedException();
        public Task<AcademicPublicationListResponse> ListPublicationsAsync(AcademicPublicationListRequest request) => throw new NotSupportedException();
        public Task<AcademicPublicationSelectionResponse> SavePublicationSelectionsAsync(AcademicPublicationSelectionRequest request) => throw new NotSupportedException();
    }
}
