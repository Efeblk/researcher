using System.Net;
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
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
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
        await Assert.ThrowsAsync<BulkRequestException>(() => service.SubmitAsync(input));
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
                TcKimlikNo = new string('1', 11),
                Orcid = " (https://orcid.org/0000-0002-1825-009X) ,",
                GoogleScholarId = "person@example.test",
                WebOfScienceId = "https://www.webofscience.com/wos/author/record/A-1234-2020.",
                ScopusId = " https://www.scopus.com/authid/detail.uri?authorId=57200000001 "
            }]
        };

        var submitted = await service.SubmitAsync(input);
        Assert.Equal(BulkJobStatus.Pending, submitted.Jobs.Single().Status);
        Assert.Single(submitted.Jobs.Single().Warnings);
        string persisted = (await db.BulkCollectionJobs.SingleAsync(job => job.BatchId == input.BatchId)).InputJson;
        Assert.Contains("0000-0002-1825-009X", persisted);
        Assert.Contains("A-1234-2020", persisted);
        Assert.Contains("person@example.test", persisted);
        Assert.Contains("authorId=57200000001", persisted);
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
        Assert.Single(status.Jobs.Single().Warnings);
        var fake = (FakeApplicationService)scope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
        Assert.Equal("0000-0002-1825-009X", fake.LastRequest!.Orcid);
        Assert.Equal("A-1234-2020", fake.LastRequest.WebOfScienceResearcherId);
        Assert.Equal("synthetic-cleanup", fake.LastRequest.PersonelId);
        Assert.Equal(new string('1', 11), fake.LastRequest.TcKimlikNo);
        Assert.Equal("57200000001", fake.LastRequest.ScopusId);
        Assert.Null(fake.LastRequest.GoogleScholarId);
        Assert.Equal("synthetic-cleanup", fake.LastMetricsRequest!.PersonelId);
        Assert.Equal(1, fake.MetricsCallCount);
    }

    [Fact]
    public async Task ProcessNextAsync_MetricRecalculationFails_RetriesSavedCollection()
    {
        var fake = new FakeApplicationService(false, metricsFail: true);
        await using ServiceProvider services = BuildServices(fake, new()
        {
            ["BulkCollection:MaximumAttempts"] = "2",
            ["BulkCollection:RetrySeconds"] = "1"
        });
        using IServiceScope scope = services.CreateScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await DeferExistingJobsAsync(db);
        BulkCollectionSubmitRequest input = Input();
        await scope.ServiceProvider.GetRequiredService<BulkCollectionService>().SubmitAsync(input);

        Assert.True(await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync());

        BulkCollectionJob job = await db.BulkCollectionJobs.SingleAsync(value => value.BatchId == input.BatchId);
        Assert.Equal(BulkJobStatus.RetryWaiting, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Contains("metric recalculation failed", job.ResultMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, fake.MetricsCallCount);
    }

    [Fact]
    public async Task ProcessNextAsync_ScopusOnly_UsesRealCollectionAndPersistsCanonicalWork()
    {
        string scopusId = Random.Shared.NextInt64(10000000000, 99999999999).ToString();
        string personelId = "scopus-bulk-" + Guid.NewGuid().ToString("N");
        string workId = Random.Shared.NextInt64(10000000000, 99999999999).ToString();
        using StubHttpHandler handler = new(request =>
        {
            Assert.Equal("bulk-scopus-key", request.Headers.GetValues("X-ELS-APIKey").Single());
            Assert.Equal("bulk-inst-token", request.Headers.GetValues("X-ELS-Insttoken").Single());
            return request.RequestUri!.AbsolutePath.Contains("/author/author_id/")
                ? StubHttpHandler.Json("{\"author-retrieval-response\":{\"coredata\":{\"dc:identifier\":\"AUTHOR_ID:" +
                    scopusId + "\",\"document-count\":\"1\",\"citation-count\":\"5\"},\"h-index\":\"2\"}}")
                : StubHttpHandler.Json("{\"search-results\":{\"opensearch:totalResults\":\"1\",\"entry\":[{" +
                    "\"dc:identifier\":\"SCOPUS_ID:" + workId + "\",\"eid\":\"2-s2.0-" + workId +
                    "\",\"dc:title\":\"Bulk Scopus work\",\"prism:doi\":\"10.5555/scopus-bulk\"," +
                    "\"prism:coverDate\":\"2026-09-14\",\"subtypeDescription\":\"Article\"}]}}" );
        });
        await using ServiceProvider services = BuildScopusServices(handler);
        using IServiceScope scope = services.CreateScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await DeferExistingJobsAsync(db);
        BulkCollectionService bulk = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        BulkCollectionSubmitRequest request = new()
        {
            BatchId = Guid.NewGuid(),
            Researchers = [new() { PersonelId = personelId, ScopusId = scopusId }]
        };

        await bulk.SubmitAsync(request);
        Assert.True(await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync());
        BulkCollectionStatusResponse status = await bulk.GetStatusAsync(new() { BatchId = request.BatchId });
        db.ChangeTracker.Clear();

        Assert.Equal(BulkJobStatus.Succeeded, status.Jobs.Single().Status);
        AcademicWork work = await db.AcademicWorks.SingleAsync(item =>
            item.PersonelId == personelId && item.Provider == AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models.AcademicWorkProvider.Scopus);
        Assert.Equal("2-s2.0-" + workId, work.SourceId);
        Assert.Equal("10.5555/scopus-bulk", work.Doi);
        Assert.True(await db.CanonicalWorks.AnyAsync(item => item.NormalizedDoi == "10.5555/scopus-bulk"));
        Assert.True(await db.ScopusProfiles.AnyAsync(item => item.PersonelId == personelId && item.HIndex == 2));
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
            new() { PersonelId = "SAME-PERSON", WebOfScienceId = "A-4321-2020" }
        ]};

        var result = await scope.ServiceProvider.GetRequiredService<BulkCollectionService>().SubmitAsync(request);

        Assert.Equal(2, result.Counts[BulkJobStatus.Rejected]);
        Assert.Equal(["same-person", "SAME-PERSON"], result.Jobs.Select(value => value.PersonelId));
    }

    [Fact]
    public async Task SubmitAsync_SharedTcAcrossPersonnel_RejectsBeforeWorkerRuns()
    {
        using var scope = fixture.Services.CreateScope();
        string tcKimlikNo = new('1', 11);
        var request = new BulkCollectionSubmitRequest { BatchId = Guid.NewGuid(), Researchers =
        [
            new() { PersonelId = "synthetic-tc-a", TcKimlikNo = tcKimlikNo },
            new() { PersonelId = "synthetic-tc-b", TcKimlikNo = tcKimlikNo }
        ]};

        var result = await scope.ServiceProvider.GetRequiredService<BulkCollectionService>().SubmitAsync(request);

        Assert.Equal(2, result.Counts[BulkJobStatus.Rejected]);
        Assert.All(result.Jobs, job => Assert.DoesNotContain(tcKimlikNo, job.Message));
    }

    [Fact]
    public async Task SubmitAsync_ScholarIdsRemainCaseSensitive()
    {
        using var scope = fixture.Services.CreateScope();
        var request = new BulkCollectionSubmitRequest { BatchId = Guid.NewGuid(), Researchers =
        [
            new() { PersonelId = "scholar-case-a", GoogleScholarId = "AbCdEfGhIjKl" },
            new() { PersonelId = "scholar-case-b", GoogleScholarId = "abcdefghijkl" }
        ]};

        var result = await scope.ServiceProvider.GetRequiredService<BulkCollectionService>().SubmitAsync(request);

        Assert.Equal(2, result.Counts[BulkJobStatus.Pending]);
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
    public async Task HostedWorker_TwoResearchers_NormalizesWorksAndEmitsCollectionChanges()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string[] personelIds = ["hosted-canonical-a-" + suffix, "hosted-canonical-b-" + suffix];
        string[] researcherIds = ["D-7392-2098", "D-7392-2099"];
        string[] dois = ["10.7200/bulk-a-" + suffix, "10.7200/bulk-b-" + suffix];
        Guid batchId = Guid.NewGuid();
        await using (AsyncServiceScope seedScope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext db = seedScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            await DeferExistingJobsAsync(db);
            db.Researchers.AddRange(personelIds.Select((personelId, index) => new Researcher
            {
                PersonelId = personelId,
                WebOfScienceResearcherId = researcherIds[index],
                WebOfScienceProfile = new WebOfScienceProfile
                {
                    PersonelId = personelId,
                    LastUpdatedAt = DateTime.UtcNow,
                    DocumentsCount = 1,
                    DocumentPagesJson = """{"WOS":[{}],"WOK":[{}]}""",
                    Works = [new WebOfScienceWork
                    {
                        Uid = "WOS:hosted-canonical-" + index,
                        Title = "Hosted bulk canonical publication " + index,
                        Doi = dois[index],
                        PublicationYear = 2026,
                        WorkTypes = "Article",
                        RawDataJson = "{}"
                    }]
                }
            }));
            await db.SaveChangesAsync();
        }

        using (var submitHost = new HostProcess(
            fixture.ConnectionString,
            disablePublicationEnrichmentProviders: true))
        {
            await submitHost.WaitUntilReadyAsync();
            using HttpResponseMessage submit = await submitHost.Client.PostAsJsonAsync(
                "/Services/AcademicPerformance/V1/Bulk/Submit",
                new
                {
                    BatchId = batchId,
                    Researchers = personelIds.Select((personelId, index) => new
                    { PersonelID = personelId, ResearcherID = researcherIds[index] }).ToArray()
                });
            submit.EnsureSuccessStatusCode();
            JsonElement submitBody = await submit.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(submitBody.GetProperty("WorkerEnabled").GetBoolean());
            Assert.Equal(2, submitBody.GetProperty("Counts").GetProperty("Pending").GetInt32());
        }

        using var workerHost = new HostProcess(
            fixture.ConnectionString,
            bulkWorkerEnabled: null,
            disablePublicationEnrichmentProviders: true,
            articleSummaryAutomationEnabled: true,
            articleSummaryAutomationWorkerEnabled: false);
        await workerHost.WaitUntilReadyAsync();
        JsonElement statusBody = default;
        for (int attempt = 0; attempt < 100; attempt++)
        {
            using HttpResponseMessage status = await workerHost.Client.PostAsJsonAsync(
                "/Services/AcademicPerformance/V1/Bulk/Status", new { BatchId = batchId });
            status.EnsureSuccessStatusCode();
            statusBody = await status.Content.ReadFromJsonAsync<JsonElement>();
            if (statusBody.GetProperty("IsComplete").GetBoolean())
                break;
            await Task.Delay(200);
        }
        Assert.True(statusBody.GetProperty("WorkerEnabled").GetBoolean());
        Assert.True(statusBody.GetProperty("IsComplete").GetBoolean(), statusBody.GetRawText());
        Assert.All(statusBody.GetProperty("Jobs").EnumerateArray(), job =>
            Assert.Equal(BulkJobStatus.Succeeded, job.GetProperty("Status").GetString()));

        for (int index = 0; index < personelIds.Length; index++)
        {
            using HttpResponseMessage canonical = await workerHost.Client.PostAsJsonAsync(
                "/Services/AcademicPerformance/V1/ListCanonicalPublications",
                new { PersonelID = personelIds[index],
                    SearchText = "HTTPS://DOI.ORG/" + dois[index].ToUpperInvariant() });
            canonical.EnsureSuccessStatusCode();
            JsonElement canonicalBody = await canonical.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, canonicalBody.GetProperty("TotalCount").GetInt32());
            JsonElement publication = canonicalBody.GetProperty("Entities")[0];
            Assert.Equal(dois[index], publication.GetProperty("NormalizedDoi").GetString());
            Assert.Equal("WebOfScience", publication.GetProperty("Observations")[0]
                .GetProperty("Provider").GetString());
            Assert.Equal(1, publication.GetProperty("KnownResearcherCount").GetInt32());
        }
        await using AsyncServiceScope queueScope = fixture.Services.CreateAsyncScope();
        AcademicDbContext queueDatabase = queueScope.ServiceProvider
            .GetRequiredService<AcademicDbContext>();
        var changeRows = await queueDatabase.CollectionChanges.AsNoTracking()
            .Where(change => personelIds.Contains(change.PersonelId))
            .Select(change => new { change.PersonelId, change.ChangeKind })
            .ToArrayAsync();
        ILookup<string, string> changes = changeRows.ToLookup(
            change => change.PersonelId!, change => change.ChangeKind);
        Assert.All(personelIds, personelId =>
        {
            Assert.Contains("ResearcherCollected", changes[personelId]);
            Assert.Contains("CanonicalWorkChanged", changes[personelId]);
        });
        await using SqlConnection ownership = new(fixture.ConnectionString);
        await ownership.OpenAsync();
        await using SqlCommand ownedTables = ownership.CreateCommand();
        ownedTables.CommandText = """
            SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
            WHERE s.name IN (N'analysis', N'hr', N'faculty');
            """;
        Assert.Equal(0, Convert.ToInt32(await ownedTables.ExecuteScalarAsync()));
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
    public async Task ProcessNextAsync_OnlyLocalDeferral_DoesNotConsumeAttempt()
    {
        await using var services = BuildServices(new FakeApplicationService(true, localDeferral: true), new()
        {
            ["BulkCollection:MaximumAttempts"] = "1"
        });
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await db.BulkCollectionJobs.Where(job => job.Status == BulkJobStatus.Pending ||
                job.Status == BulkJobStatus.RetryWaiting)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAt,
                DateTime.UtcNow.AddDays(5)));
        var input = Input();
        await service.SubmitAsync(input);
        var processor = scope.ServiceProvider.GetRequiredService<BulkJobProcessor>();
        await processor.ProcessNextAsync();

        var result = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.RetryWaiting, result.Jobs.Single().Status);
        Assert.Equal(0, result.Jobs.Single().Attempts);

        await db.BulkCollectionJobs.Where(job => job.BatchId == input.BatchId)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAt, DateTime.UtcNow));
        db.ChangeTracker.Clear();
        await processor.ProcessNextAsync();
        result = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.RetryWaiting, result.Jobs.Single().Status);
        Assert.Equal(0, result.Jobs.Single().Attempts);
    }

    [Fact]
    public async Task ProcessNextAsync_ExceptionAfterLocalDeferral_ConsumesAttempt()
    {
        await using var services = BuildServices(
            new FakeApplicationService(true, localDeferral: true, throwAfterFailure: true), new()
            {
                ["BulkCollection:MaximumAttempts"] = "1"
            });
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await db.BulkCollectionJobs.Where(job => job.Status == BulkJobStatus.Pending ||
                job.Status == BulkJobStatus.RetryWaiting)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAt,
                DateTime.UtcNow.AddDays(5)));
        var input = Input();
        await service.SubmitAsync(input);
        await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync();

        var result = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.Failed, result.Jobs.Single().Status);
        Assert.Equal(1, result.Jobs.Single().Attempts);
    }

    [Fact]
    public async Task ProcessNextAsync_LocalAndDisabledFailures_DoNotConsumeAttempt()
    {
        await using var services = BuildServices(
            new FakeApplicationService(true, localDeferral: true, nonretryableFailure: true), new()
            {
                ["BulkCollection:MaximumAttempts"] = "1"
            });
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await DeferExistingJobsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var input = Input();
        await service.SubmitAsync(input);
        await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync();

        var result = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.RetryWaiting, result.Jobs.Single().Status);
        Assert.Equal(0, result.Jobs.Single().Attempts);
    }

    [Fact]
    public async Task ProcessNextAsync_LocalAndActualRetryableFailures_ConsumeAttempt()
    {
        await using var services = BuildServices(
            new FakeApplicationService(true, localDeferral: true, actualFailure: true), new()
            {
                ["BulkCollection:MaximumAttempts"] = "1"
            });
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await DeferExistingJobsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var input = Input();
        await service.SubmitAsync(input);
        await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync();

        var result = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.Partial, result.Jobs.Single().Status);
        Assert.Equal(1, result.Jobs.Single().Attempts);
    }

    [Fact]
    public async Task ProcessNextAsync_YoksisCategoryFailure_SchedulesGenericRetry()
    {
        await using var services = BuildServices(new FakeApplicationService(false, yoksisFailedCategories: 1));
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await DeferExistingJobsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var input = Input();
        await service.SubmitAsync(input);

        await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync();
        var result = await service.GetStatusAsync(new() { BatchId = input.BatchId });

        Assert.Equal(BulkJobStatus.RetryWaiting, result.Jobs.Single().Status);
        Assert.Equal(1, result.Jobs.Single().Attempts);
        Assert.Equal("Temporary failure or provider cooldown; retry scheduled.", result.Jobs.Single().Message);
    }

    [Fact]
    public async Task ProcessNextAsync_LocalAndActualNonretryableFailures_ConsumeAttempt()
    {
        await using var services = BuildServices(
            new FakeApplicationService(true, localDeferral: true, actualNonretryableFailure: true), new()
            {
                ["BulkCollection:MaximumAttempts"] = "1"
            });
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await DeferExistingJobsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var input = Input();
        await service.SubmitAsync(input);
        await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync();

        var result = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.Partial, result.Jobs.Single().Status);
        Assert.Equal(1, result.Jobs.Single().Attempts);
    }

    [Fact]
    public async Task ProcessNextAsync_PersistenceFailureAfterLocalDeferral_ConsumesAttempt()
    {
        await using var services = BuildServices(
            new FakeApplicationService(true, "PersistenceFailure", localDeferral: true), new()
            {
                ["BulkCollection:MaximumAttempts"] = "1"
            });
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await DeferExistingJobsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<BulkCollectionService>();
        var input = Input();
        await service.SubmitAsync(input);
        await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>().ProcessNextAsync();

        var result = await service.GetStatusAsync(new() { BatchId = input.BatchId });
        Assert.Equal(BulkJobStatus.Failed, result.Jobs.Single().Status);
        Assert.Equal(1, result.Jobs.Single().Attempts);
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

    [Fact]
    public async Task Api_InvalidSubmitAndUnknownStatus_ReturnStructuredErrors()
    {
        using var host = new HostProcess(fixture.ConnectionString);
        await host.WaitUntilReadyAsync();

        using HttpResponseMessage invalidSubmit = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/Bulk/Submit",
            new { BatchId = Guid.Empty, Researchers = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, invalidSubmit.StatusCode);
        JsonElement invalidSubmitBody = await invalidSubmit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(invalidSubmitBody.TryGetProperty("Error", out JsonElement submitError),
            invalidSubmitBody.GetRawText());
        Assert.Contains("stable, non-empty BatchId", submitError.GetProperty("Message").GetString());

        using HttpResponseMessage emptyBatch = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/Bulk/Submit",
            new { BatchId = Guid.NewGuid(), Researchers = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, emptyBatch.StatusCode);
        JsonElement emptyBatchBody = await emptyBatch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("between 1 and", emptyBatchBody.GetProperty("Error")
            .GetProperty("Message").GetString());

        using HttpResponseMessage invalidImport = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/Bulk/ImportSql", new { BatchId = Guid.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, invalidImport.StatusCode);
        JsonElement invalidImportBody = await invalidImport.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("stable, non-empty BatchId", invalidImportBody.GetProperty("Error")
            .GetProperty("Message").GetString());

        using HttpResponseMessage unknownStatus = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/Bulk/Status", new { BatchId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, unknownStatus.StatusCode);
        JsonElement unknownStatusBody = await unknownStatus.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(unknownStatusBody.TryGetProperty("Error", out JsonElement statusError),
            unknownStatusBody.GetRawText());
        Assert.Equal("Batch not found.", statusError.GetProperty("Message").GetString());
    }

    private ServiceProvider BuildServices(FakeApplicationService service, Dictionary<string, string?>? extra = null)
    {
        var settings = extra ?? [];
        settings["ConnectionStrings:AcademicDatabase"] = fixture.ConnectionString;
        settings.TryAdd("BulkCollection:MaximumAttempts", "2");
        settings.TryAdd("BulkCollection:WorkerEnabled", "false");
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAcademicPerformanceModule(configuration);
        services.AddSingleton<IAcademicPerformanceApplicationService>(service);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SubmitAsync_SharedScopusIdAcrossPersonnel_RejectsBothRows()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        BulkCollectionSubmitRequest request = new()
        {
            BatchId = Guid.NewGuid(),
            Researchers =
            [
                new() { PersonelId = "scopus-conflict-a", ScopusId = "57200000001" },
                new() { PersonelId = "scopus-conflict-b", ScopusId = " 57200000001 " }
            ]
        };

        BulkCollectionStatusResponse result = await scope.ServiceProvider
            .GetRequiredService<BulkCollectionService>().SubmitAsync(request);

        Assert.Equal(2, result.Counts[BulkJobStatus.Rejected]);
        Assert.All(result.Jobs, job => Assert.Equal(BulkJobStatus.Rejected, job.Status));
    }

    private ServiceProvider BuildScopusServices(HttpMessageHandler handler)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:AcademicDatabase"] = fixture.ConnectionString,
                ["BulkCollection:MaximumAttempts"] = "1",
                ["BulkCollection:WorkerEnabled"] = "false",
                ["Scopus:ApiBaseUrl"] = "https://scopus.test/content/",
                ["Scopus:ApiKey"] = "bulk-scopus-key",
                ["Scopus:InstToken"] = "bulk-inst-token",
                ["ProviderRequestLimits:Crossref:Enabled"] = "false",
                ["ProviderRequestLimits:Unpaywall:Enabled"] = "false",
                ["ProviderRequestLimits:SemanticScholar:Enabled"] = "false"
            }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAcademicPerformanceModule(configuration);
        services.AddSingleton(new HttpClient(handler));
        return services.BuildServiceProvider();
    }

    private static Task DeferExistingJobsAsync(AcademicDbContext database) =>
        database.BulkCollectionJobs.Where(job => job.Status == BulkJobStatus.Pending ||
                job.Status == BulkJobStatus.RetryWaiting)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAt,
                DateTime.UtcNow.AddDays(5)));

    private sealed class FakeApplicationService(bool fail, string? failureCode = null,
        bool localDeferral = false, bool throwAfterFailure = false, bool actualFailure = false,
        bool nonretryableFailure = false, bool actualNonretryableFailure = false,
        int yoksisFailedCategories = 0, bool metricsFail = false) : IAcademicPerformanceApplicationService
    {
        public AcademicDataCollectRequest? LastRequest { get; private set; }
        public ResearcherMetricsRequest? LastMetricsRequest { get; private set; }
        public int MetricsCallCount { get; private set; }

        public Task<AcademicDataResponse> CollectAsync(AcademicDataCollectRequest request)
        {
            LastRequest = request;
            if (fail) ProviderCallScope.Record("WebOfScience", true, DateTime.UtcNow.AddHours(1), localDeferral);
            if (actualFailure) ProviderCallScope.Record("Orcid", true, DateTime.UtcNow.AddMinutes(1));
            if (nonretryableFailure) ProviderCallScope.Record("SearchApi", false, isDisabled: true);
            if (actualNonretryableFailure) ProviderCallScope.Record("Crossref", false);
            if (throwAfterFailure) throw new HttpRequestException("Synthetic collection failure.");
            return Task.FromResult(new AcademicDataResponse
            {
                IsSaved = failureCode is null, FailureCode = failureCode, Researcher = new()
                {
                    PersonelId = request.PersonelId,
                    OrcidProfile = request.Orcid is null ? null : new(),
                    OpenAlexProfile = request.Orcid is null ? null : new(),
                    ScopusProfile = request.ScopusId is null ? null : new(),
                    GoogleScholarProfile = request.GoogleScholarId is null ? null : new(),
                    WebOfScienceProfile = request.WebOfScienceResearcherId is null ? null : new()
                },
                YoksisFailedCategoryCount = yoksisFailedCategories
            });
        }
        public Task<ResearcherMetricsResponse> RecalculateMetricsAsync(ResearcherMetricsRequest request,
            CancellationToken cancellationToken = default)
        {
            LastMetricsRequest = request;
            MetricsCallCount++;
            if (LastRequest is null)
                throw new InvalidOperationException("Metrics were called before collection.");
            if (metricsFail)
                throw new InvalidOperationException("Synthetic metric failure.");
            return Task.FromResult(new ResearcherMetricsResponse
            {
                PersonelId = request.PersonelId ?? string.Empty,
                RecalculatedAt = DateTime.UtcNow
            });
        }
        public Task<AcademicDataResponse> GetResearcherAsync(AcademicResearcherRequest request) => throw new NotSupportedException();
        public Task<AcademicPublicationListResponse> ListPublicationsAsync(AcademicPublicationListRequest request) => throw new NotSupportedException();
        public Task<AcademicPublicationSelectionResponse> SavePublicationSelectionsAsync(AcademicPublicationSelectionRequest request) => throw new NotSupportedException();
        public Task<CanonicalPublicationListResponse> ListCanonicalPublicationsAsync(CanonicalPublicationListRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CanonicalPublicationRebuildResponse> RebuildCanonicalPublicationsAsync(CanonicalPublicationRebuildRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
