using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Ollama;

public sealed class OllamaArticleSummaryGenerator(HttpClient client, IOptions<AiOptions> options) : IArticleSummaryGenerator
{
    private bool _capabilityVerified;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedArticleChunk> GenerateAsync(string language, string sourceKind,
        IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
    {
        AiOptions settings = options.Value;
        if (settings.ArticleProvider != "Ollama")
            throw new AnalysisUnavailableException("Article summaries are not configured to use Ollama.");
        if (string.IsNullOrWhiteSpace(settings.ArticleModel))
            throw new AnalysisUnavailableException("Configure Ai:ArticleModel with an installed local Ollama model.");
        if (!Uri.TryCreate(settings.OllamaBaseUrl, UriKind.Absolute, out Uri? baseUrl) || !baseUrl.IsLoopback ||
            baseUrl.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(baseUrl.UserInfo))
            throw new AnalysisUnavailableException("Ai:OllamaBaseUrl must point to the local Ollama server.");
        await EnsureCapabilitiesAsync(baseUrl, cancellationToken);

        var sources = sourceSpans.Select(x => new { x.SourceId, x.PageNumber, x.Text });
        string input = JsonSerializer.Serialize(new { language, sourceKind, sources }, JsonOptions);
        if (settings.ArticleModel.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase) || settings.ArticleModel.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase))
            throw new AnalysisUnavailableException("Choose a downloaded local model instead of a cloud model.");
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(baseUrl, "/api/chat"));
        message.Content = JsonContent.Create(new
        {
            model = settings.ArticleModel, stream = false, think = false, truncate = false, shift = false,
            format = ArticleSummaryPrompt.CreateSchema(sourceSpans.Where(x => !string.IsNullOrWhiteSpace(x.Text)).Select(x => x.SourceId)),
            messages = new[] { new { role = "system", content = ArticleSummaryPrompt.Instructions }, new { role = "user", content = input } },
            options = new { temperature = 0, num_ctx = settings.ArticleContextTokens, num_predict = settings.ArticleMaxOutputTokens }
        });
        using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new AnalysisUnavailableException("Ollama could not find the configured model.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            string error = await response.Content.ReadAsStringAsync(cancellationToken);
            if (error.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("input length", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("too many tokens", StringComparison.OrdinalIgnoreCase))
                throw new AnalysisInputTooLargeException();
            throw new HttpRequestException("Local model rejected the article request.", null, response.StatusCode);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Local model request failed.", null, response.StatusCode);
        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            if (!root.GetProperty("done").GetBoolean() || root.GetProperty("done_reason").GetString() != "stop")
                throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
            int promptTokens = root.GetProperty("prompt_eval_count").GetInt32();
            int generatedTokens = root.GetProperty("eval_count").GetInt32();
            if (promptTokens <= 0 || generatedTokens <= 0)
                throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
            if (promptTokens > settings.ArticleContextTokens - settings.ArticleMaxOutputTokens ||
                promptTokens + generatedTokens > settings.ArticleContextTokens)
                throw new AnalysisInputTooLargeException();
            GeneratedArticleSections sections = JsonSerializer.Deserialize<GeneratedArticleSections>(root.GetProperty("message").GetProperty("content").GetString()!, JsonOptions)
                ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            sections = NormalizeSourceIds(sections);
            ValidateEvidence(sections, sourceSpans);
            return new(sections, root.GetProperty("model").GetString() ?? settings.ArticleModel, ArticleSummaryPrompt.Version);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
        }
    }

    private async Task EnsureCapabilitiesAsync(Uri baseUrl, CancellationToken cancellationToken)
    {
        if (_capabilityVerified) return;
        using HttpResponseMessage response = await client.GetAsync(new Uri(baseUrl, "/api/version"), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AnalysisUnavailableException("The local Ollama version could not be verified.");
        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            string value = document.RootElement.GetProperty("version").GetString() ?? string.Empty;
            string numeric = value.Split('-', '+')[0];
            if (!Version.TryParse(numeric, out Version? version) || version < new Version(0, 33, 3))
                throw new AnalysisUnavailableException("Article summaries require Ollama 0.33.3 or newer for untruncated context handling.");
            _capabilityVerified = true;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AnalysisUnavailableException("The local Ollama version could not be verified.");
        }
    }

    private static void ValidateEvidence(GeneratedArticleSections sections, IReadOnlyList<ArticleSourceSpan> sourceSpans)
    {
        IReadOnlyList<GeneratedArticleClaim>?[] groups = [sections.Purpose, sections.Methods, sections.Data, sections.Findings, sections.Limitations];
        if (groups.Any(x => x is null || x.Count > 3)) throw new InvalidAnalysisException();
        List<GeneratedArticleClaim> claims = groups.SelectMany(x => x!).ToList();
        if (claims.Any(claim => claim is null || string.IsNullOrWhiteSpace(claim.ClaimId) ||
            string.IsNullOrWhiteSpace(claim.Text) || claim.Text.Length > 1200 || claim.SourceIds is null ||
            claim.SourceIds.Count is 0 or > 2 ||
            claim.SourceIds.Any(id => !sourceSpans.Any(span => span.SourceId == id && !string.IsNullOrWhiteSpace(span.Text)))) ||
            claims.Select(x => x.ClaimId).Distinct(StringComparer.Ordinal).Count() != claims.Count)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }

    private static GeneratedArticleSections NormalizeSourceIds(GeneratedArticleSections sections)
    {
        IReadOnlyList<GeneratedArticleClaim> Normalize(IReadOnlyList<GeneratedArticleClaim> claims) =>
            claims.Select(claim => claim with { SourceIds = claim.SourceIds.Distinct(StringComparer.Ordinal).ToList() }).ToList();
        return new(Normalize(sections.Purpose), Normalize(sections.Methods), Normalize(sections.Data),
            Normalize(sections.Findings), Normalize(sections.Limitations));
    }
}
