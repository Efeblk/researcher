using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Ollama;

public sealed class OllamaReportGenerator(HttpClient client, IOptions<AiOptions> options)
    : IResearcherReportGenerator
{
    private const string Instructions = ReportPrompt.Instructions + "\n\n" + """
        Keep the report concise. Prefer one short, complete source sentence per evidence quote.
        Copy publicationId from the publication's id value, not its title or DOI.
        A quote must contain 10-600 characters: copy a longer continuous passage if a phrase is too short.
        Do not translate, paraphrase, or join separate passages in a quote. Omit unsupported observations.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // This JSON becomes message text; preserve Turkish letters for the model to quote.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
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
        string instructions = request.Language == "tr"
            ? Instructions + "\nWrite every observation in Turkish. Keep evidence quotes in the original source language."
            : Instructions;
        // Conservative byte-based allowance, reserving space for output and the chat template.
        // Avoid silently dropping earlier publications when using a small local context window.
        int inputBudget = Encoding.UTF8.GetByteCount(instructions) + Encoding.UTF8.GetByteCount(input) + 512;
        if (inputBudget + settings.MaxOutputTokens > settings.OllamaContextTokens)
            throw new AnalysisInputTooLargeException();

        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(baseUrl, "/api/chat"));
        message.Content = JsonContent.Create(new
        {
            model = settings.Model,
            stream = false,
            think = false,
            format = OllamaReportSchema.Create(request.Publications),
            messages = new[]
            {
                new { role = "system", content = instructions },
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
            if (!root.GetProperty("done").GetBoolean())
                throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
            string? doneReason = root.GetProperty("done_reason").GetString();
            if (doneReason != "stop")
                throw new InvalidAnalysisException(doneReason == "length"
                    ? AnalysisFailure.OutputLimit : AnalysisFailure.IncompleteOutput);
            string content = root.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            AnalysisFindings findings = JsonSerializer.Deserialize<AnalysisFindings>(content, JsonOptions)
                ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            return new GeneratedFindings(findings, root.GetProperty("model").GetString() ?? settings.Model,
                ReportPrompt.Version + "-ollama-v3");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
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
