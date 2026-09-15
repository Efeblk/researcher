using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ResearcherAnalysisService.Products.Data;

namespace ResearcherAnalysisService.Tests;

public sealed class ServiceStartupBoundarySmokeTests
{
    private const string AnalysisHistoryTable = "dbo.ResearcherAnalysisVersionInfo";
    private const string CollectorHistoryTable = "dbo.VersionInfo";

    private static readonly string[] CollectorBusinessTables =
    [
        "bulk.BulkCollectionBatches", "bulk.BulkCollectionJobs",
        "core.AcademicWorkResearchContexts", "core.AcademicWorks", "core.AcademicWorkSources",
        "core.AcademicWorkTopics", "core.CanonicalResearcherWorks", "core.CanonicalWorkObservations",
        "core.CanonicalWorks", "core.CollectionChanges", "core.PublicationDisplayApprovals",
        "core.PublicationSummaries", "core.Researchers", "crossref.CrossrefWorks",
        "googlescholar.GoogleScholarProfiles", "googlescholar.GoogleScholarWorks",
        "integrations.ProviderRequestBudgets", "integrations.ProviderStatusObservations",
        "openalex.OpenAlexProfiles", "openalex.OpenAlexWorks", "orcid.OrcidProfiles", "orcid.OrcidWorks",
        "scopus.ScopusProfiles", "scopus.ScopusWorks",
        "semanticscholar.SemanticScholarCitationContexts", "semanticscholar.SemanticScholarCitations",
        "semanticscholar.SemanticScholarPapers", "trdizin.TrDizinProfiles", "trdizin.TrDizinWorks",
        "wos.WebOfSciencePeerReviews", "wos.WebOfScienceProfiles", "wos.WebOfScienceWorks",
        "yoksis.YoksisRecords"
    ];

    private static readonly string[] AnalysisBusinessTables =
    [
        "analysis.ArticleEvaluationAttempts", "analysis.ArticleEvaluationCases",
        "analysis.ArticleEvaluationResults", "analysis.ArticleEvaluationRuns",
        "analysis.ArticleEvaluationWorkItems", "analysis.ArticleReviewStageCheckpoints",
        "analysis.ArticleReviewWorkItems", "analysis.ArticleSourcePages",
        "analysis.ArticleSourceSnapshots", "analysis.ArticleSourceSpans", "analysis.ArticleSummaries",
        "analysis.ArticleSummaryAutomationJobs", "analysis.CanonicalArticleAnalysisRuns",
        "analysis.CanonicalArticleClaimEvidence", "analysis.CanonicalArticleClaims",
        "analysis.CanonicalArticleReviewEvidence", "analysis.CanonicalArticleReviewFindings",
        "analysis.CanonicalArticleReviewRuns", "analysis.CollectionChangeReceipts",
        "analysis.GeminiUsageAttempts", "analysis.PublicationMetricProviderSnapshots",
        "analysis.PublicationMetricSnapshots", "analysis.PublicationMetricsRefreshStates",
        "analysis.ReferencePopulationManifests", "analysis.ReferencePopulationMembers",
        "analysis.ResearcherAnalyses", "faculty.AssistantContextVersions", "faculty.AssistantRuns",
        "hr.DossierReviewActions", "hr.EvidenceDossiers"
    ];

    private static readonly long[] AnalysisMigrationVersions =
    [
        202609140001, 202609140002, 202609140003, 202609140004, 202609140005,
        202609140006, 202609140007, 202609140008, 202609140009, 202609140010,
        202609140011, 202609140012, 202609140013, 202609140014, 202609140015,
        202609140016, 202609140017
    ];

    [ServiceBoundarySmokeFact]
    public async Task Hosts_MigrateSharedDatabaseInEitherOrderAndConcurrently()
    {
        string root = Environment.GetEnvironmentVariable("SERVICE_BOUNDARY_REPOSITORY_ROOT") ??
            FindRepositoryRoot();
        string collectorDll = Path.Combine(root, "bin", "Release", "net10.0",
            "AcademicCollectorDemo.dll");
        string analysisDll = Path.Combine(root, "ResearcherAnalysisService", "bin", "Release",
            "net10.0", "ResearcherAnalysisService.dll");
        Assert.True(File.Exists(collectorDll), $"Collector build output was not found: {collectorDll}");
        Assert.True(File.Exists(analysisDll), $"Analysis build output was not found: {analysisDll}");

        foreach (StartupOrder order in Enum.GetValues<StartupOrder>())
            await VerifyOrderAsync(root, collectorDll, analysisDll, order);
    }

    private static async Task VerifyOrderAsync(string root, string collectorDll, string analysisDll,
        StartupOrder order)
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        int collectorPort = AvailablePort();
        int analysisPort = AvailablePort();
        await using ServiceProcess collector = ServiceProcess.Create(
            root, collectorDll, $"http://127.0.0.1:{collectorPort}", database.ConnectionString,
            analysis: false);
        await using ServiceProcess analysis = ServiceProcess.Create(
            root, analysisDll, $"http://127.0.0.1:{analysisPort}", database.ConnectionString,
            analysis: true);

        switch (order)
        {
            case StartupOrder.CollectorFirst:
                await collector.StartAndWaitAsync("/");
                await AssertTablesAsync(database,
                    [.. CollectorBusinessTables, CollectorHistoryTable]);
                await analysis.StartAndWaitAsync("/health");
                break;
            case StartupOrder.AnalysisFirst:
                await analysis.StartAndWaitAsync("/health");
                await AssertTablesAsync(database,
                    [.. AnalysisBusinessTables, AnalysisHistoryTable]);
                await collector.StartAndWaitAsync("/");
                break;
            case StartupOrder.Concurrent:
                await Task.WhenAll(collector.StartAndWaitAsync("/"),
                    analysis.StartAndWaitAsync("/health"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(order));
        }

        Assert.Equal(33, CollectorBusinessTables.Length);
        Assert.Equal(30, AnalysisBusinessTables.Length);
        Assert.Equal(63, CollectorBusinessTables.Length + AnalysisBusinessTables.Length);
        await AssertTablesAsync(database,
            [.. CollectorBusinessTables, .. AnalysisBusinessTables, CollectorHistoryTable, AnalysisHistoryTable]);
        Assert.Equal(AnalysisMigrationVersions, await database.QueryInt64sAsync(
            "SELECT [Version] FROM [dbo].[ResearcherAnalysisVersionInfo] ORDER BY [Version]"));

        if (order == StartupOrder.CollectorFirst)
            await AssertSourceProjectionQueriesAsync(database.ConnectionString);
    }

    private static async Task AssertTablesAsync(TemporaryDatabase database, string[] expected)
    {
        string[] actual = await database.QueryStringsAsync("""
            SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name)
            FROM sys.tables;
            """);
        Assert.Equal(expected.OrderBy(value => value, StringComparer.Ordinal),
            actual.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static async Task AssertSourceProjectionQueriesAsync(string connectionString)
    {
        DbContextOptions<AnalysisDbContext> options = new DbContextOptionsBuilder<AnalysisDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using AnalysisDbContext database = new(options);
        MethodInfo queryMethod = typeof(ServiceStartupBoundarySmokeTests).GetMethod(
            nameof(QuerySourceProjectionAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
        Type[] sourceEntityTypes = database.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null &&
                entity.GetSchema() is not ("analysis" or "hr" or "faculty"))
            .Select(entity => entity.ClrType)
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(sourceEntityTypes);
        foreach (Type sourceEntityType in sourceEntityTypes)
        {
            Task query = (Task)queryMethod.MakeGenericMethod(sourceEntityType)
                .Invoke(null, [database])!;
            await query;
        }
    }

    private static async Task QuerySourceProjectionAsync<TEntity>(AnalysisDbContext database)
        where TEntity : class =>
        await database.Set<TEntity>().AsNoTracking().Take(1).ToListAsync();

    private static int AvailablePort()
    {
        TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,
                   "AcademicCollectorDemo.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root from the test output directory.");
    }

    private enum StartupOrder
    {
        CollectorFirst,
        AnalysisFirst,
        Concurrent
    }

    private sealed class ServiceProcess : IAsyncDisposable
    {
        private readonly string _healthBaseUrl;
        private readonly Process _process;
        private readonly ConcurrentQueue<string> _output = new();
        private bool _started;

        private ServiceProcess(Process process, string healthBaseUrl)
        {
            _process = process;
            _healthBaseUrl = healthBaseUrl;
            _process.OutputDataReceived += (_, value) => AddOutput(value.Data);
            _process.ErrorDataReceived += (_, value) => AddOutput(value.Data);
        }

        public static ServiceProcess Create(string root, string dll, string url,
            string connectionString, bool analysis)
        {
            ProcessStartInfo start = new("dotnet")
            {
                WorkingDirectory = analysis ? Path.GetDirectoryName(dll)! : root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(dll);
            start.ArgumentList.Add("--urls");
            start.ArgumentList.Add(url);
            start.ArgumentList.Add("--environment");
            start.ArgumentList.Add("Testing");
            if (analysis)
            {
                start.Environment["ConnectionStrings__UsageDatabase"] = connectionString;
                start.Environment["CollectionChanges__WorkerEnabled"] = "false";
                start.Environment["ArticleSummaryAutomation__Enabled"] = "false";
                start.Environment["ArticleSummaryAutomation__WorkerEnabled"] = "false";
                start.Environment["PublicationMetrics__WorkerEnabled"] = "false";
                start.Environment["ArticleEvaluation__WorkerEnabled"] = "false";
                start.Environment["FacultyAssistant__WorkerEnabled"] = "false";
            }
            else
            {
                start.Environment["ConnectionStrings__AcademicDatabase"] = connectionString;
                start.Environment["BulkCollection__WorkerEnabled"] = "false";
            }
            return new(new Process { StartInfo = start }, url);
        }

        public async Task StartAndWaitAsync(string healthPath)
        {
            Assert.True(_process.Start(), "The service process did not start.");
            _started = true;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            using HttpClient client = new() { BaseAddress = new Uri(_healthBaseUrl) };
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
            while (!timeout.IsCancellationRequested)
            {
                if (_process.HasExited)
                    throw new InvalidOperationException(
                        $"Service exited with code {_process.ExitCode}.{Environment.NewLine}{Output()}");
                try
                {
                    using HttpResponseMessage response = await client.GetAsync(healthPath, timeout.Token);
                    if (response.IsSuccessStatusCode)
                        return;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!timeout.IsCancellationRequested) { }
                await Task.Delay(100, timeout.Token);
            }
            throw new TimeoutException($"Service did not become ready.{Environment.NewLine}{Output()}");
        }

        public async ValueTask DisposeAsync()
        {
            if (!_started)
            {
                _process.Dispose();
                return;
            }
            if (_process.HasExited)
            {
                _process.Dispose();
                return;
            }
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
            _process.Dispose();
        }

        private void AddOutput(string? value)
        {
            if (value is not null)
                _output.Enqueue(value);
        }

        private string Output() => string.Join(Environment.NewLine, _output);
    }

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string _databaseName;
        private readonly string _masterConnectionString;
        public string ConnectionString { get; }

        private TemporaryDatabase(string databaseName, string masterConnectionString,
            string connectionString)
        {
            _databaseName = databaseName;
            _masterConnectionString = masterConnectionString;
            ConnectionString = connectionString;
        }

        public static async Task<TemporaryDatabase> CreateAsync()
        {
            string databaseName = "ServiceBoundarySmoke_" + Guid.NewGuid().ToString("N");
            SqlConnectionStringBuilder connection = new(
                Environment.GetEnvironmentVariable("ACADEMIC_TEST_SQLSERVER") ??
                @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Encrypt=true;TrustServerCertificate=true");
            connection.Encrypt = SqlConnectionEncryptOption.Mandatory;
            connection.InitialCatalog = "master";
            string masterConnectionString = connection.ConnectionString;
            await using SqlConnection master = new(masterConnectionString);
            await master.OpenAsync();
            await using SqlCommand create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{databaseName}]";
            await create.ExecuteNonQueryAsync();
            connection.InitialCatalog = databaseName;
            connection.MaxPoolSize = 1;
            return new(databaseName, masterConnectionString, connection.ConnectionString);
        }

        public async Task<string[]> QueryStringsAsync(string sql)
        {
            List<string> values = [];
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                values.Add(reader.GetString(0));
            return values.ToArray();
        }

        public async Task<long[]> QueryInt64sAsync(string sql)
        {
            List<long> values = [];
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                values.Add(reader.GetInt64(0));
            return values.ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            await using SqlConnection master = new(_masterConnectionString);
            await master.OpenAsync();
            await using SqlCommand drop = master.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{_databaseName}]";
            await drop.ExecuteNonQueryAsync();
        }
    }
}

public sealed class ServiceBoundarySmokeFactAttribute : FactAttribute
{
    public ServiceBoundarySmokeFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_SERVICE_BOUNDARY_SMOKE"),
                "true", StringComparison.OrdinalIgnoreCase))
            Skip = "Set RUN_SERVICE_BOUNDARY_SMOKE=true after building both service executables.";
    }
}
