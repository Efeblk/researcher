using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Api.V1.Contracts;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Ollama;

public sealed class OllamaReportGenerator(HttpClient client, IOptions<AiOptions> options)
    : IResearcherReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedFindings> GenerateAsync(AnalyzeResearcherRequest request, CancellationToken cancellationToken)
    {
        AiOptions settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new AnalysisUnavailableException("Configure Ai:Model with an installed local Ollama model. No AI API key is required.");
        if (!Uri.TryCreate(settings.OllamaBaseUrl, UriKind.Absolute, out Uri? baseUrl) ||
            !baseUrl.IsLoopback || baseUrl.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(baseUrl.UserInfo))
            throw new AnalysisUnavailableException("Ai:OllamaBaseUrl must point to the local Ollama server.");
        if (settings.Model.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase) ||
            settings.Model.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase))
            throw new AnalysisUnavailableException("Choose a downloaded local model instead of a cloud model.");

        string input = JsonSerializer.Serialize(new
        {
            request.Language,
            request.TotalPublicationCount,
            Publications = request.Publications.Select(publication => new
            {
                publication.Id, publication.Title, publication.Year,
                publication.Abstract, publication.Keywords
            })
        }, JsonOptions);
        // Conservative byte-based allowance, reserving space for output and the chat template.
        // Avoid silently dropping earlier publications when using a small local context window.
        int inputBudget = Encoding.UTF8.GetByteCount(ReportPrompt.Instructions) + Encoding.UTF8.GetByteCount(input) + 512;
        if (inputBudget + settings.MaxOutputTokens > settings.OllamaContextTokens)
            throw new AnalysisInputTooLargeException();

        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(baseUrl, "/api/chat"));
        message.Content = JsonContent.Create(new
        {
            model = settings.Model,
            stream = false,
            think = false,
            format = ReportPrompt.Schema,
            messages = new[]
            {
                new { role = "system", content = ReportPrompt.Instructions },
                new { role = "user", content = input }
            },
            options = new
            {
                temperature = 0,
                num_ctx = settings.OllamaContextTokens,
                num_predict = settings.MaxOutputTokens
            }
        });

        using HttpResponseMessage response = await SendAsync(message, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new AnalysisUnavailableException("Ollama could not find the configured model. Download it with ollama pull and check Ai:Model.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Local model request failed.", null, response.StatusCode);

        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            if (!root.GetProperty("done").GetBoolean() || root.GetProperty("done_reason").GetString() != "stop")
                throw new InvalidAnalysisException();
            string content = root.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            AnalysisFindings findings = JsonSerializer.Deserialize<AnalysisFindings>(content, JsonOptions)
                ?? throw new InvalidAnalysisException();
            return new GeneratedFindings(findings, root.GetProperty("model").GetString() ?? settings.Model, ReportPrompt.Version);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidAnalysisException();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.ConnectionError)
        {
            throw new AnalysisUnavailableException("The local Ollama server is not reachable. Start Ollama and check Ai:OllamaBaseUrl.");
        }
    }
}
