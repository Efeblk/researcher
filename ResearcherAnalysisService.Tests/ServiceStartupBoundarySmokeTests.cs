using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;

namespace ResearcherAnalysisService.Tests;

public sealed class ServiceStartupBoundarySmokeTests
{
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
                await analysis.StartAndWaitAsync("/health");
                break;
            case StartupOrder.AnalysisFirst:
                await analysis.StartAndWaitAsync("/health");
                await collector.StartAndWaitAsync("/");
                break;
            case StartupOrder.Concurrent:
                await Task.WhenAll(collector.StartAndWaitAsync("/"),
                    analysis.StartAndWaitAsync("/health"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(order));
        }

        Assert.Equal(1, await database.CountAsync(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name='VersionInfo'"));
        Assert.Equal(1, await database.CountAsync(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name='ResearcherAnalysisVersionInfo'"));
        Assert.Equal(1, await database.CountAsync(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('analysis') AND name='GeminiUsageAttempts'"));
        Assert.Equal(1, await database.CountAsync(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('core') AND name='Researchers'"));
        Assert.Equal(1, await database.CountAsync(
            "SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo] WHERE [Version]=202609140001"));
    }

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
            }
            else
            {
                start.Environment["ConnectionStrings__AcademicDatabase"] = connectionString;
                start.Environment["AnalysisService__BaseUrl"] = "http://127.0.0.1:1/";
                start.Environment["BulkCollection__WorkerEnabled"] = "false";
                start.Environment["ArticleSummaryAutomation__Enabled"] = "false";
                start.Environment["ArticleSummaryAutomation__WorkerEnabled"] = "false";
                start.Environment["PublicationMetrics__WorkerEnabled"] = "false";
                start.Environment["ArticleEvaluation__WorkerEnabled"] = "false";
                start.Environment["FacultyAssistant__WorkerEnabled"] = "false";
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
                @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true");
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

        public async Task<int> CountAsync(string sql)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
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
