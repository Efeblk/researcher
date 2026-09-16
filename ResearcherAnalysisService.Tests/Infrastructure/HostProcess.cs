using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ResearcherAnalysisService.Tests.Infrastructure;

public sealed class HostProcess : IDisposable
{
    private readonly Process _process;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _output = new();

    public HttpClient Client { get; }
    public string Output => string.Join(Environment.NewLine, _output);

    public HostProcess(
        string connectionString,
        string? analysisBaseUrl = null,
        bool? bulkWorkerEnabled = false,
        bool disablePublicationEnrichmentProviders = false,
        bool articleSummaryAutomationEnabled = false,
        bool articleSummaryAutomationWorkerEnabled = false,
        int? articleSummaryAutomationPollSeconds = null,
        int? articleSummaryAutomationRetrySeconds = null,
        bool publicationMetricsWorkerEnabled = false,
        int? publicationMetricsPollSeconds = null,
        int? publicationMetricsRetrySeconds = null,
        int? publicationMetricsBatchSize = null,
        bool articleEvaluationWorkerEnabled = false,
        int? articleEvaluationPollSeconds = null)
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
            !File.Exists(Path.Combine(root.FullName, "ResearcherAnalysisService", "ResearcherAnalysisService.csproj")))
            root = root.Parent;
        string assembly = typeof(ResearcherAnalysisService.Program).Assembly.Location;
        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = root?.FullName ??
                throw new InvalidOperationException("Project root not found."),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--urls=" + Client.BaseAddress);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["ConnectionStrings__UsageDatabase"] = connectionString;
        start.Environment["DatabaseMigrations__Enabled"] = "false";
        start.Environment["CollectionChanges__WorkerEnabled"] = "false";
        start.Environment["ArticleSummaryAutomation__Enabled"] =
            articleSummaryAutomationEnabled.ToString();
        start.Environment["ArticleSummaryAutomation__WorkerEnabled"] =
            articleSummaryAutomationWorkerEnabled.ToString();
        start.Environment["PublicationMetrics__WorkerEnabled"] =
            publicationMetricsWorkerEnabled.ToString();
        start.Environment["ArticleEvaluation__WorkerEnabled"] =
            articleEvaluationWorkerEnabled.ToString();
        start.Environment["FacultyAssistant__WorkerEnabled"] = "false";
        if (articleSummaryAutomationPollSeconds.HasValue)
            start.Environment["ArticleSummaryAutomation__PollSeconds"] =
                articleSummaryAutomationPollSeconds.Value.ToString();
        if (articleSummaryAutomationRetrySeconds.HasValue)
            start.Environment["ArticleSummaryAutomation__RetrySeconds"] =
                articleSummaryAutomationRetrySeconds.Value.ToString();
        if (publicationMetricsPollSeconds.HasValue)
            start.Environment["PublicationMetrics__PollSeconds"] =
                publicationMetricsPollSeconds.Value.ToString();
        if (publicationMetricsRetrySeconds.HasValue)
            start.Environment["PublicationMetrics__RetrySeconds"] =
                publicationMetricsRetrySeconds.Value.ToString();
        if (publicationMetricsBatchSize.HasValue)
            start.Environment["PublicationMetrics__BatchSize"] =
                publicationMetricsBatchSize.Value.ToString();
        if (articleEvaluationPollSeconds.HasValue)
            start.Environment["ArticleEvaluation__PollSeconds"] =
                articleEvaluationPollSeconds.Value.ToString();
        _process = Process.Start(start) ??
            throw new InvalidOperationException("Host did not start.");
        _process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                _output.Enqueue(args.Data);
        };
        _process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                _output.Enqueue(args.Data);
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async Task WaitUntilReadyAsync()
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"Host exited with code {_process.ExitCode}.{Environment.NewLine}{Output}");
            try
            {
                using HttpResponseMessage response = await Client.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(250);
        }
        throw new TimeoutException("Test host did not become ready.");
    }

    public void Dispose()
    {
        Client.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10000);
        }
        _process.Dispose();
    }
}
