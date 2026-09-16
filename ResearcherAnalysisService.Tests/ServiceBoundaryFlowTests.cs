using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Data;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Api;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.Metrics;
using ResearcherAnalysisService.SourceData;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class ServiceBoundaryFlowTests
{
    [Fact]
    public void ProductResponses_PreserveSafeInt64SerializationInNestedAndNullableValues()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            Data = new
            {
                Safe = 9_007_199_254_740_992L,
                Unsafe = 9_007_199_254_740_993L,
                Maximum = long.MaxValue,
                NullableMaximum = (long?)long.MaxValue,
                Missing = (long?)null
            }
        }, ProductJsonContractAttribute.CreateSerializerOptions());

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement data = document.RootElement.GetProperty("Data");
        Assert.Equal(System.Text.Json.JsonValueKind.Number, data.GetProperty("Safe").ValueKind);
        Assert.Equal("9007199254740993", data.GetProperty("Unsafe").GetString());
        Assert.Equal(long.MaxValue.ToString(), data.GetProperty("Maximum").GetString());
        Assert.Equal(long.MaxValue.ToString(), data.GetProperty("NullableMaximum").GetString());
        Assert.False(data.TryGetProperty("Missing", out _));
    }

    [Fact]
    public async Task ProductResponses_PreservePascalCaseWhileStatelessApiRemainsCamelCase()
    {
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(settings:
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:UsageDatabase"] =
                    "Server=127.0.0.1,1;Database=ProductJsonContract;User ID=synthetic;Password=synthetic;" +
                    "Encrypt=true;TrustServerCertificate=true;Connect Timeout=1",
                ["CollectionChanges:WorkerEnabled"] = "false",
                ["ArticleSummaryAutomation:Enabled"] = "false",
                ["ArticleSummaryAutomation:WorkerEnabled"] = "false",
                ["PublicationMetrics:WorkerEnabled"] = "false",
                ["ArticleEvaluation:WorkerEnabled"] = "false",
                ["FacultyAssistant:WorkerEnabled"] = "false"
            });

        using HttpResponseMessage product = await host.Client.PostAsync(
            "/api/v1/researchers/metrics",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        string productJson = await product.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, product.StatusCode);
        Assert.Contains("\"Message\":", productJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"message\":", productJson, StringComparison.Ordinal);

        using HttpResponseMessage health = await host.Client.GetAsync("/health");
        string healthJson = await health.Content.ReadAsStringAsync();
        Assert.Contains("\"service\":", healthJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Service\":", healthJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceProjections_RejectSyncAndAsyncWrites_ButOwnedRowsSave()
    {
        await using BoundaryDatabase fixture = await BoundaryDatabase.CreateAsync(createSourceContract: false);
        await using AnalysisDbContext database = fixture.CreateContext();
        string[] crossBoundaryRelationships = database.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(key => key.DeclaringEntityType.GetSchema() is "analysis" or "hr" or "faculty" &&
                key.PrincipalEntityType.GetSchema() is not ("analysis" or "hr" or "faculty"))
            .Select(key => $"{key.DeclaringEntityType.ClrType.Name}->{key.PrincipalEntityType.ClrType.Name}")
            .ToArray();
        Assert.Empty(crossBoundaryRelationships);
        database.Researchers.Add(new Researcher { PersonelId = "source-write" });

        InvalidOperationException sync = Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        Assert.Contains("Researcher", sync.Message, StringComparison.Ordinal);

        database.ChangeTracker.Clear();
        database.AcademicWorks.Add(new AcademicWork
        {
            Id = 91,
            PersonelId = "source-write",
            ProviderWorkId = "forbidden",
            SyncedAt = DateTime.UtcNow
        });
        InvalidOperationException async = await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.SaveChangesAsync());
        Assert.Contains("AcademicWork", async.Message, StringComparison.Ordinal);

        database.ChangeTracker.Clear();
        Guid eventId = Guid.NewGuid();
        database.CollectionChangeReceipts.Add(new CollectionChangeReceipt
        {
            EventId = eventId,
            ReceivedAtUtc = DateTime.UtcNow
        });
        Assert.Equal(1, await database.SaveChangesAsync());
        Assert.True(await database.CollectionChangeReceipts.AsNoTracking()
            .AnyAsync(value => value.EventId == eventId));
    }

    [Fact]
    public void SourceIdentity_HasStableCanonicalMeaning_AndRejectsReusedIntegerIds()
    {
        AcademicWork firstAuthor = Work(1, "person-1", "shared-paper", "10.1000/shared");
        AcademicWork secondAuthor = Work(2, "person-2", "shared-paper-copy", "10.1000/shared");
        string oneAuthor = ArticleSummaryAutomationScheduler.CreateInputHash([firstAuthor]);
        string twoAuthors = ArticleSummaryAutomationScheduler.CreateInputHash([firstAuthor, secondAuthor]);

        AcademicWork recycledIdentity = Work(1, "person-1", "different-paper", "10.1000/different");
        string recycled = ArticleSummaryAutomationScheduler.CreateInputHash([recycledIdentity]);

        Assert.Equal(oneAuthor, twoAuthors);
        Assert.NotEqual(oneAuthor, recycled);
    }

    [Fact]
    public async Task ChangeFeed_TwoResearchers_ReplaysOnceAndRejectsStaleRunAfterIdReuse()
    {
        await using BoundaryDatabase fixture = await BoundaryDatabase.CreateAsync(createSourceContract: true);
        Guid[] initialEvents = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        await fixture.SeedTwoResearchersAsync(initialEvents);

        await using AnalysisDbContext database = fixture.CreateContext();
        CollectionChangeProcessor processor = CreateProcessor(database);
        Assert.Equal(4, await processor.ProcessBatchAsync());
        Assert.Equal(0, await processor.ProcessBatchAsync());
        Assert.Equal(4, await database.CollectionChangeReceipts.CountAsync());
        Assert.Equal(2, await database.PublicationMetricsRefreshStates.CountAsync());
        Assert.Equal(2, await database.ArticleSummaryAutomationJobs.CountAsync());

        PublicationMetricsProcessor metricsProcessor = new(database, new SyntheticMetricsComputer(),
            new AnalysisSourceLock(database),
            new StaticOptionsMonitor<PublicationMetricsOptions>(new() { BatchSize = 10 }),
            TimeProvider.System);
        Assert.Equal(2, await metricsProcessor.ProcessBatchAsync());
        Assert.Equal(2, await database.PublicationMetricSnapshots.CountAsync());

        int generatedSummaries = 0;
        ArticleSummaryWorkflow automaticWorkflow = CreateSummaryWorkflow(
            database, automationEnabled: true, () => generatedSummaries++);
        long[] jobIds = await database.ArticleSummaryAutomationJobs.AsNoTracking()
            .OrderBy(value => value.CanonicalWorkId).Select(value => value.Id).ToArrayAsync();
        foreach (long jobId in jobIds)
        {
            database.ChangeTracker.Clear();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .SingleAsync(value => value.Id == jobId);
            Guid token = Guid.NewGuid();
            job.Status = ArticleSummaryAutomationJobStatus.Running;
            job.RunningInputHash = job.DesiredInputHash;
            job.RunningPolicyVersion = job.DesiredPolicyVersion;
            job.ExecutionToken = token;
            job.AttemptGeneration++;
            job.Attempts++;
            await database.SaveChangesAsync();
            await automaticWorkflow.SummarizeAutomaticAsync(new(
                job.Id, job.CanonicalWorkId, job.Language, job.RunningInputHash,
                job.RunningPolicyVersion, token), CancellationToken.None);
        }
        Assert.Equal(2, generatedSummaries);
        Assert.Equal(2, await database.CanonicalArticleAnalysisRuns.CountAsync());

        ArticleSummaryAutomationJob originalJob = await database.ArticleSummaryAutomationJobs
            .SingleAsync(value => value.CanonicalWorkId == 101);
        string originalIdentity = originalJob.DesiredInputHash;
        CanonicalArticleAnalysisRun oldRun = await database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .SingleAsync(value => value.CanonicalWorkId == 101);
        Assert.Equal(originalIdentity, oldRun.SourceIdentityHash);

        Guid unsupported = Guid.NewGuid();
        Guid laterSupported = Guid.NewGuid();
        await fixture.AddChangesAsync(
            (unsupported, "FutureChange", null, null, 2),
            (laterSupported, "ResearcherCollected", "person-2", null, 1));
        Assert.Equal(1, await processor.ProcessBatchAsync());
        Assert.False(await database.CollectionChangeReceipts.AnyAsync(value => value.EventId == unsupported));
        Assert.True(await database.CollectionChangeReceipts.AnyAsync(value => value.EventId == laterSupported));
        Assert.Equal(2, await database.PublicationMetricsRefreshStates
            .Where(value => value.PersonelId == "person-2")
            .Select(value => value.RequestedRevision).SingleAsync());

        Guid removed = Guid.NewGuid();
        await fixture.RemoveAndSignalAsync(removed);
        Assert.Equal(1, await processor.ProcessBatchAsync());
        database.ChangeTracker.Clear();
        ArticleSummaryAutomationJob removedJob = await database.ArticleSummaryAutomationJobs
            .SingleAsync(value => value.CanonicalWorkId == 101);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, removedJob.Status);
        Assert.Equal("SourceRemoved", removedJob.LastOutcomeCode);
        Assert.Null(removedJob.ExecutionToken);
        Assert.Null(removedJob.RunningInputHash);
        Assert.Null(removedJob.RunningPolicyVersion);

        Guid recreated = Guid.NewGuid();
        await fixture.RecreateWithSameIdsAsync(recreated);
        Assert.Equal(1, await processor.ProcessBatchAsync());
        database.ChangeTracker.Clear();
        ArticleSummaryAutomationJob recreatedJob = await database.ArticleSummaryAutomationJobs
            .SingleAsync(value => value.CanonicalWorkId == 101);
        Assert.NotEqual(originalIdentity, recreatedJob.DesiredInputHash);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Pending, recreatedJob.Status);

        AnalysisFreshnessResult freshness = (await AnalysisFreshnessEvaluator.EvaluateAsync(
            database, [oldRun.Id], recreatedJob.DesiredPolicyVersion, CancellationToken.None))[oldRun.Id];
        Assert.Equal(AnalysisFreshnessStatus.Stale, freshness.Status);
        Assert.Contains("SourceIdentityChanged", freshness.Reasons);
        Assert.Null(await new CanonicalArticleEvidenceQueryService(database).GetLatestAsync(
            "person-1", 101, "tr", 0, 20, CancellationToken.None));

        int manualGenerations = 0;
        ArticleSummaryWorkflow manualWorkflow = CreateSummaryWorkflow(
            database, automationEnabled: false, () => manualGenerations++);
        Assert.NotNull(await manualWorkflow.SummarizeAsync(
            "person-1", 1, "tr", CancellationToken.None));
        Assert.Equal(1, manualGenerations);
        SavedArticleSummary manualSummary = await database.ArticleSummaries.AsNoTracking()
            .Where(value => value.PersonelId == "person-1" && value.OriginalAcademicWorkId == 1)
            .OrderByDescending(value => value.Id).FirstAsync();
        Assert.Contains("Different synthetic abstract", manualSummary.SnapshotJson);
        Assert.DoesNotContain("Other person's canonical source", manualSummary.SnapshotJson);
        CanonicalArticleEvidenceResponse? currentEvidence =
            await new CanonicalArticleEvidenceQueryService(database).GetLatestAsync(
                "person-1", 101, "tr", 0, 20, CancellationToken.None);
        Assert.NotNull(currentEvidence);
        Assert.NotEqual(oldRun.Id, currentEvidence.AnalysisRunId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Pending,
            (await database.ArticleSummaryAutomationJobs.AsNoTracking()
                .SingleAsync(value => value.CanonicalWorkId == 101)).Status);
    }

    private static AcademicWork Work(int id, string personelId, string providerWorkId, string doi) => new()
    {
        Id = id,
        PersonelId = personelId,
        Provider = AcademicWorkProvider.Orcid,
        ProviderWorkId = providerWorkId,
        Doi = doi,
        Title = "Shared evidence",
        Abstract = "Stable synthetic abstract.",
        SyncedAt = DateTime.UtcNow,
        Sources = [new AcademicWorkSource
        {
            Url = "https://example.test/shared",
            Kind = "Landing",
            Origin = "Synthetic"
        }]
    };

    private static CollectionChangeProcessor CreateProcessor(AnalysisDbContext database) => new(
        database,
        new AnalysisSourceLock(database),
        new ArticleSummaryAutomationScheduler(database,
            new StaticOptionsMonitor<ArticleSummaryAutomationOptions>(new())),
        new PublicationMetricsRefreshScheduler(database,
            new StaticOptionsMonitor<PublicationMetricsOptions>(new()), TimeProvider.System),
        new StaticOptionsMonitor<CollectionChangeOptions>(new() { BatchSize = 100 }),
        TimeProvider.System,
        NullLogger<CollectionChangeProcessor>.Instance);

    private static ArticleSummaryWorkflow CreateSummaryWorkflow(
        AnalysisDbContext database,
        bool automationEnabled,
        Action generated)
    {
        IOptions<ArticleSummaryOptions> summaryOptions = Options.Create(new ArticleSummaryOptions
        {
            OcrEnabled = false
        });
        ArticleSummarizer summarizer = new(new SyntheticSummaryGenerator(generated),
            new SyntheticClaimVerifier(), Options.Create(new AiOptions()));
        return new(database, new SafeArticleFetcher(summaryOptions),
            new ArticlePdfExtractor(summaryOptions), new ArticleHtmlExtractor(summaryOptions),
            new ArticleSummaryServiceClient(summarizer), summaryOptions,
            new AnalysisSourceLock(database),
            new StaticOptionsMonitor<ArticleSummaryAutomationOptions>(new()
            {
                Enabled = automationEnabled,
                WorkerEnabled = automationEnabled
            }));
    }

    private sealed class SyntheticSummaryGenerator(Action generated) : IArticleSummaryGenerator
    {
        public Task<GeneratedArticleChunk> GenerateAsync(string language, string sourceKind,
            IReadOnlyList<AcademicCollector.Analysis.Contracts.ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            generated();
            GeneratedArticleClaim claim = new("claim-1", "Synthetic supported finding.",
                [sourceSpans[0].SourceId]);
            return Task.FromResult(new GeneratedArticleChunk(
                new([], [], [], [claim], []), "synthetic", "synthetic-v1"));
        }
    }

    private sealed class SyntheticClaimVerifier : IArticleClaimVerifier
    {
        public Task<GeneratedVerificationBatch> VerifyAsync(string language,
            IReadOnlyList<GeneratedArticleClaim> claims,
            IReadOnlyList<AcademicCollector.Analysis.Contracts.ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken) => Task.FromResult(new GeneratedVerificationBatch(
                claims.Select(claim => new GeneratedClaimVerdict(
                    claim.ClaimId, "supported", "Synthetic exact source match.")).ToList(),
                "synthetic", "synthetic-verify-v1"));
    }

    private sealed class SyntheticMetricsComputer : IPublicationMetricsComputer
    {
        public Task<PublicationMetricComputation> ComputeAsync(string personelId, string catalogVersion,
            DateTime computedAt, CancellationToken cancellationToken)
        {
            ResearcherAnalysisService.Products.Api.Contracts.ResearcherPublicationMetricsResponse response = new()
            {
                PersonelId = personelId,
                Catalog = "Synthetic",
                CatalogVersion = catalogVersion,
                ResultLabel = "Synthetic",
                ComputedAt = computedAt,
                ValidYearUpperBound = computedAt.Year,
                CanonicalWorkCount = 1,
                ProviderObservationCount = 1
            };
            return Task.FromResult(new PublicationMetricComputation(response, "{}"));
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class BoundaryDatabase : IAsyncDisposable
    {
        private readonly string _databaseName;
        private readonly string _masterConnectionString;
        public string ConnectionString { get; }

        private BoundaryDatabase(string databaseName, string masterConnectionString, string connectionString)
        {
            _databaseName = databaseName;
            _masterConnectionString = masterConnectionString;
            ConnectionString = connectionString;
        }

        public static async Task<BoundaryDatabase> CreateAsync(bool createSourceContract)
        {
            string databaseName = "ServiceBoundaryFlow_" + Guid.NewGuid().ToString("N");
            SqlConnectionStringBuilder connection = new(
                Environment.GetEnvironmentVariable("ACADEMIC_TEST_SQLSERVER") ??
                @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Encrypt=true;TrustServerCertificate=true");
            connection.Encrypt = SqlConnectionEncryptOption.Mandatory;
            connection.InitialCatalog = "master";
            string masterConnectionString = connection.ConnectionString;
            await using (SqlConnection master = new(masterConnectionString))
            {
                await master.OpenAsync();
                await using SqlCommand create = master.CreateCommand();
                create.CommandText = $"CREATE DATABASE [{databaseName}]";
                await create.ExecuteNonQueryAsync();
            }
            connection.InitialCatalog = databaseName;
            BoundaryDatabase result = new(databaseName, masterConnectionString, connection.ConnectionString);
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:UsageDatabase"] = result.ConnectionString })
                .Build();
            ServiceCollection services = new();
            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            services.AddAnalysisDatabaseMigrations(configuration);
            await using (ServiceProvider provider = services.BuildServiceProvider())
                provider.MigrateAnalysisDatabase();
            if (createSourceContract)
                await result.ExecuteAsync(SourceContractSql);
            return result;
        }

        public AnalysisDbContext CreateContext() => new(new DbContextOptionsBuilder<AnalysisDbContext>()
            .UseSqlServer(ConnectionString).Options);

        public async Task SeedTwoResearchersAsync(Guid[] events)
        {
            Assert.Equal(4, events.Length);
            await ExecuteAsync(SeedSql,
                ("@event1", events[0]), ("@event2", events[1]),
                ("@event3", events[2]), ("@event4", events[3]));
        }

        public Task AddChangesAsync(params (Guid Id, string Kind, string? PersonelId,
            int? CanonicalWorkId, int Version)[] changes) => ExecuteAsync(string.Join(Environment.NewLine,
            changes.Select((_, index) => $"""
                INSERT INTO [core].[CollectionChanges]
                    ([EventId],[ChangeKind],[PersonelID],[CanonicalWorkId],[OccurredAtUtc],[PayloadVersion])
                VALUES (@id{index},@kind{index},@person{index},@canonical{index},
                    DATEADD(millisecond,{index},SYSUTCDATETIME()),@version{index});
                """)), changes.SelectMany((value, index) => new (string, object?)[]
            {
                ($"@id{index}", value.Id), ($"@kind{index}", value.Kind),
                ($"@person{index}", value.PersonelId), ($"@canonical{index}", value.CanonicalWorkId),
                ($"@version{index}", value.Version)
            }).ToArray());

        public async Task RemoveAndSignalAsync(Guid eventId)
        {
            await ExecuteAsync("""
                DELETE FROM [core].[CanonicalResearcherWorks] WHERE [CanonicalWorkId]=101;
                DELETE FROM [core].[CanonicalWorkObservations] WHERE [CanonicalWorkId]=101;
                DELETE FROM [core].[AcademicWorkSources] WHERE [AcademicWorkId]=1;
                DELETE FROM [core].[AcademicWorks] WHERE [Id]=1;
                INSERT INTO [core].[CollectionChanges]
                    ([EventId],[ChangeKind],[CanonicalWorkId],[AcademicWorkId],[OccurredAtUtc],[PayloadVersion])
                VALUES (@event,N'CanonicalWorkChanged',101,1,SYSUTCDATETIME(),1);
                """, ("@event", eventId));
        }

        public async Task RecreateWithSameIdsAsync(Guid eventId)
        {
            await ExecuteAsync("""
                INSERT INTO [core].[AcademicWorks]
                    ([Id],[PersonelID],[Provider],[ProviderWorkId],[Title],[Doi],[Category],[CategorySource],[Abstract],[SyncedAt])
                VALUES
                    (1,N'person-1',N'Orcid',N'recycled-work',N'Different work',N'10.1000/recycled',
                        N'Article',N'Orcid',NULL,SYSUTCDATETIME()),
                    (3,N'person-1',N'OpenAlex',N'recycled-metadata-match',N'Different work',NULL,
                        N'Article',N'OpenAlex',N'Different synthetic abstract.',SYSUTCDATETIME()),
                    (4,N'person-2',N'OpenAlex',N'other-person-source',N'Different work',NULL,
                        N'Article',N'OpenAlex',N'Other person''s canonical source.',SYSUTCDATETIME());
                INSERT INTO [core].[CanonicalWorkObservations]
                    ([Id],[CanonicalWorkId],[AcademicWorkId],[PersonelID],[Provider],[ProviderWorkId],
                     [TitleObserved],[DoiObserved],[CategoryObserved],[ObservedAt])
                VALUES
                    (1,101,1,N'person-1',N'Orcid',N'recycled-work',N'Different work',N'10.1000/recycled',
                        N'Article',SYSUTCDATETIME()),
                    (3,101,3,N'person-1',N'OpenAlex',N'recycled-metadata-match',N'Different work',NULL,
                        N'Article',SYSUTCDATETIME()),
                    (4,101,4,N'person-2',N'OpenAlex',N'other-person-source',N'Different work',NULL,
                        N'Article',SYSUTCDATETIME());
                INSERT INTO [core].[CanonicalResearcherWorks] ([CanonicalWorkId],[PersonelID],[LastObservedAt])
                VALUES (101,N'person-1',SYSUTCDATETIME());
                INSERT INTO [core].[CollectionChanges]
                    ([EventId],[ChangeKind],[PersonelID],[CanonicalWorkId],[AcademicWorkId],[OccurredAtUtc],[PayloadVersion])
                VALUES (@event,N'CanonicalWorkChanged',N'person-1',101,1,SYSUTCDATETIME(),1);
                """, ("@event", eventId));
        }

        private async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object? value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using SqlConnection master = new(_masterConnectionString);
            await master.OpenAsync();
            await using SqlCommand drop = master.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{_databaseName}]";
            await drop.ExecuteNonQueryAsync();
        }

        private const string SourceContractSql = """
            IF SCHEMA_ID(N'core') IS NULL EXEC(N'CREATE SCHEMA [core] AUTHORIZATION [dbo]');
            CREATE TABLE [core].[Researchers]
            (
                [PersonelID] nvarchar(200) NOT NULL PRIMARY KEY
            );
            CREATE TABLE [core].[AcademicWorks]
            (
                [Id] int NOT NULL PRIMARY KEY, [PersonelID] nvarchar(200) NOT NULL,
                [Provider] nvarchar(50) NOT NULL, [ProviderWorkId] nvarchar(500) NULL,
                [Title] nvarchar(2000) NULL, [PublicationYear] int NULL, [PublicationDate] datetime2 NULL,
                [Doi] nvarchar(500) NULL, [RawType] nvarchar(100) NULL,
                [Category] nvarchar(50) NOT NULL, [CategorySource] nvarchar(50) NOT NULL,
                [CitedByCount] int NULL, [ReferencedWorksCount] int NULL, [Authors] nvarchar(max) NULL,
                [Institutions] nvarchar(4000) NULL, [Abstract] nvarchar(max) NULL,
                [Keywords] nvarchar(4000) NULL, [Topics] nvarchar(4000) NULL, [Language] nvarchar(20) NULL,
                [Publication] nvarchar(2000) NULL, [Volume] nvarchar(100) NULL, [Issue] nvarchar(100) NULL,
                [FirstPage] nvarchar(100) NULL, [LastPage] nvarchar(100) NULL, [Link] nvarchar(2000) NULL,
                [SourceId] nvarchar(500) NULL, [SourceName] nvarchar(2000) NULL, [SourceType] nvarchar(100) NULL,
                [IsOpenAccess] bit NULL, [OpenAccessStatus] nvarchar(50) NULL, [HasFullText] bit NULL,
                [FullTextUrl] nvarchar(2000) NULL, [License] nvarchar(100) NULL, [Version] nvarchar(100) NULL,
                [IsRetracted] bit NULL, [ProviderPayload] nvarchar(max) NULL, [SyncedAt] datetime2 NOT NULL
            );
            CREATE TABLE [core].[AcademicWorkSources]
            (
                [Id] int IDENTITY NOT NULL PRIMARY KEY, [AcademicWorkId] int NOT NULL,
                [Url] nvarchar(2000) NOT NULL, [Kind] nvarchar(20) NOT NULL,
                [Origin] nvarchar(100) NOT NULL, [IsOpenAccess] bit NULL
            );
            CREATE TABLE [core].[CanonicalWorkObservations]
            (
                [Id] int NOT NULL PRIMARY KEY, [CanonicalWorkId] int NOT NULL, [AcademicWorkId] int NOT NULL,
                [PersonelID] nvarchar(200) NOT NULL, [Provider] nvarchar(50) NOT NULL,
                [ProviderWorkId] nvarchar(500) NULL, [TitleObserved] nvarchar(2000) NULL,
                [DoiObserved] nvarchar(500) NULL, [PublicationYearObserved] int NULL,
                [PublicationDateObserved] datetime2 NULL, [CategoryObserved] nvarchar(50) NOT NULL,
                [AuthorsObserved] nvarchar(max) NULL, [PublicationObserved] nvarchar(2000) NULL,
                [SourceId] nvarchar(500) NULL, [SourceName] nvarchar(2000) NULL,
                [SourceType] nvarchar(100) NULL, [Link] nvarchar(2000) NULL,
                [FullTextUrl] nvarchar(2000) NULL, [License] nvarchar(100) NULL,
                [Version] nvarchar(100) NULL, [IsRetracted] bit NULL, [ObservedAt] datetime2 NOT NULL
            );
            CREATE TABLE [core].[CanonicalResearcherWorks]
            (
                [CanonicalWorkId] int NOT NULL, [PersonelID] nvarchar(200) NOT NULL,
                [LastObservedAt] datetime2 NOT NULL,
                CONSTRAINT [PK_TestCanonicalResearcherWorks] PRIMARY KEY ([CanonicalWorkId],[PersonelID])
            );
            CREATE TABLE [core].[CollectionChanges]
            (
                [EventId] uniqueidentifier NOT NULL PRIMARY KEY, [ChangeKind] nvarchar(40) NOT NULL,
                [PersonelID] nvarchar(200) NULL, [CanonicalWorkId] int NULL, [AcademicWorkId] int NULL,
                [OccurredAtUtc] datetime2 NOT NULL, [PayloadVersion] int NOT NULL
            );
            """;

        private const string SeedSql = """
            INSERT INTO [core].[Researchers] ([PersonelID]) VALUES (N'person-1'),(N'person-2');
            INSERT INTO [core].[AcademicWorks]
                ([Id],[PersonelID],[Provider],[ProviderWorkId],[Title],[Doi],[Category],[CategorySource],[Abstract],[SyncedAt])
            VALUES
                (1,N'person-1',N'Orcid',N'paper-1',N'First work',N'10.1000/first',N'Article',N'Orcid',N'First synthetic abstract.',SYSUTCDATETIME()),
                (2,N'person-2',N'Orcid',N'paper-2',N'Second work',N'10.1000/second',N'Article',N'Orcid',N'Second synthetic abstract.',SYSUTCDATETIME());
            INSERT INTO [core].[CanonicalWorkObservations]
                ([Id],[CanonicalWorkId],[AcademicWorkId],[PersonelID],[Provider],[ProviderWorkId],
                 [TitleObserved],[DoiObserved],[CategoryObserved],[ObservedAt])
            VALUES
                (1,101,1,N'person-1',N'Orcid',N'paper-1',N'First work',N'10.1000/first',N'Article',SYSUTCDATETIME()),
                (2,202,2,N'person-2',N'Orcid',N'paper-2',N'Second work',N'10.1000/second',N'Article',SYSUTCDATETIME());
            INSERT INTO [core].[CanonicalResearcherWorks] ([CanonicalWorkId],[PersonelID],[LastObservedAt])
            VALUES (101,N'person-1',SYSUTCDATETIME()),(202,N'person-2',SYSUTCDATETIME());
            INSERT INTO [core].[CollectionChanges]
                ([EventId],[ChangeKind],[PersonelID],[CanonicalWorkId],[AcademicWorkId],[OccurredAtUtc],[PayloadVersion])
            VALUES
                (@event1,N'ResearcherCollected',N'person-1',NULL,NULL,SYSUTCDATETIME(),1),
                (@event2,N'CanonicalWorkChanged',N'person-1',101,1,DATEADD(millisecond,1,SYSUTCDATETIME()),1),
                (@event3,N'ResearcherCollected',N'person-2',NULL,NULL,DATEADD(millisecond,2,SYSUTCDATETIME()),1),
                (@event4,N'CanonicalWorkChanged',N'person-2',202,2,DATEADD(millisecond,3,SYSUTCDATETIME()),1);
            """;
    }
}
