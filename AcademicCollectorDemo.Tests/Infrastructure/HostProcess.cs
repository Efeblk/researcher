using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace AcademicCollectorDemo.Tests.Infrastructure;

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
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcademicCollectorDemo.csproj")))
            root = root.Parent;

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root?.FullName ?? throw new InvalidOperationException("Project root not found."),
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--urls=" + Client.BaseAddress);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["ConnectionStrings__AcademicDatabase"] = connectionString;
        start.Environment["ArticleSummaryAutomation__Enabled"] =
            articleSummaryAutomationEnabled.ToString();
        start.Environment["ArticleSummaryAutomation__WorkerEnabled"] =
            articleSummaryAutomationWorkerEnabled.ToString();
        if (articleSummaryAutomationPollSeconds.HasValue)
            start.Environment["ArticleSummaryAutomation__PollSeconds"] =
                articleSummaryAutomationPollSeconds.Value.ToString();
        if (articleSummaryAutomationRetrySeconds.HasValue)
            start.Environment["ArticleSummaryAutomation__RetrySeconds"] =
                articleSummaryAutomationRetrySeconds.Value.ToString();
        start.Environment["PublicationMetrics__WorkerEnabled"] =
            publicationMetricsWorkerEnabled.ToString();
        if (publicationMetricsPollSeconds.HasValue)
            start.Environment["PublicationMetrics__PollSeconds"] =
                publicationMetricsPollSeconds.Value.ToString();
        if (publicationMetricsRetrySeconds.HasValue)
            start.Environment["PublicationMetrics__RetrySeconds"] =
                publicationMetricsRetrySeconds.Value.ToString();
        if (publicationMetricsBatchSize.HasValue)
            start.Environment["PublicationMetrics__BatchSize"] =
                publicationMetricsBatchSize.Value.ToString();
        start.Environment["ArticleEvaluation__WorkerEnabled"] = articleEvaluationWorkerEnabled.ToString();
        start.Environment["FacultyAssistant__WorkerEnabled"] = "false";
        start.Environment["SearchApi__ApiKey"] = "";
        start.Environment["OpenAlex__ApiKey"] = "";
        start.Environment["SemanticScholar__ApiKey"] = "";
        start.Environment["WebOfScience__ApiKey"] = "";
        start.Environment["Yoksis__Username"] = "";
        start.Environment["Yoksis__Password"] = "";
        if (articleEvaluationPollSeconds.HasValue)
            start.Environment["ArticleEvaluation__PollSeconds"] = articleEvaluationPollSeconds.Value.ToString();
        if (bulkWorkerEnabled.HasValue)
        {
            start.Environment["BulkCollection__WorkerEnabled"] =
                bulkWorkerEnabled.Value.ToString();
        }
        else
        {
            start.Environment.Remove("BulkCollection__WorkerEnabled");
        }
        if (disablePublicationEnrichmentProviders)
        {
            start.Environment["ProviderRequestLimits__OpenAlex__Enabled"] = "false";
            start.Environment["ProviderRequestLimits__Crossref__Enabled"] = "false";
            start.Environment["ProviderRequestLimits__SemanticScholar__Enabled"] = "false";
        }
        if (analysisBaseUrl is not null)
            start.Environment["AnalysisService__BaseUrl"] = analysisBaseUrl;
        _process = Process.Start(start) ?? throw new InvalidOperationException("Host did not start.");
        _process.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) _output.Enqueue(eventArgs.Data); };
        _process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) _output.Enqueue(eventArgs.Data); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async Task WaitUntilReadyAsync()
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"Host exited with code {_process.ExitCode}.");
            try
            {
                using var response = await Client.GetAsync("/");
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
