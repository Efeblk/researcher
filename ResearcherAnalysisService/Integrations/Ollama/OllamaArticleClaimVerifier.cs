using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Ollama;

public sealed class OllamaArticleClaimVerifier(HttpClient client, IOptions<AiOptions> options) : IArticleClaimVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedVerificationBatch> VerifyAsync(string language, IReadOnlyList<GeneratedArticleClaim> claims,
        IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
    {
        AiOptions settings = options.Value;
        string model = string.IsNullOrWhiteSpace(settings.ArticleVerifierModel) ? settings.ArticleModel : settings.ArticleVerifierModel;
        if (settings.ArticleProvider != "Ollama" || string.IsNullOrWhiteSpace(model) ||
            !Uri.TryCreate(settings.OllamaBaseUrl, UriKind.Absolute, out Uri? baseUrl) || !baseUrl.IsLoopback)
            throw new AnalysisUnavailableException("Automatic article verification requires a configured local Ollama model.");
        if (model.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase) || model.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase))
            throw new AnalysisUnavailableException("Choose a downloaded local verifier model.");

        HashSet<string> cited = claims.SelectMany(x => x.SourceIds).ToHashSet(StringComparer.Ordinal);
        HashSet<int> contextIndexes = [];
        for (int i = 0; i < sourceSpans.Count; i++)
            if (cited.Contains(sourceSpans[i].SourceId))
                for (int context = Math.Max(0, i - 1); context <= Math.Min(sourceSpans.Count - 1, i + 1); context++)
                    contextIndexes.Add(context);
        var sources = contextIndexes.Order().Select(i => new
            { sourceSpans[i].SourceId, sourceSpans[i].PageNumber, sourceSpans[i].Text,
                citedEvidence = cited.Contains(sourceSpans[i].SourceId) }).ToList();
        string input = JsonSerializer.Serialize(new { language, claims, sources }, JsonOptions);
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(baseUrl, "/api/chat"))
        {
            Content = JsonContent.Create(new
            {
                model, stream = false, think = true, truncate = false, shift = false,
                format = ArticleVerificationPrompt.CreateSchema(claims.Select(x => x.ClaimId)),
                messages = new[] { new { role = "system", content = ArticleVerificationPrompt.Instructions },
                    new { role = "user", content = input } },
                options = new { temperature = 0, num_ctx = settings.ArticleContextTokens, num_predict = settings.ArticleVerifierMaxOutputTokens }
            })
        };
        using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            string error = await response.Content.ReadAsStringAsync(cancellationToken);
            if (error.Contains("context", StringComparison.OrdinalIgnoreCase) || error.Contains("tokens", StringComparison.OrdinalIgnoreCase))
                throw new AnalysisInputTooLargeException();
        }
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Local verifier request failed.", null, response.StatusCode);
        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            if (!root.GetProperty("done").GetBoolean() || root.GetProperty("done_reason").GetString() != "stop")
                throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
            int promptTokens = root.GetProperty("prompt_eval_count").GetInt32();
            int generatedTokens = root.GetProperty("eval_count").GetInt32();
            if (promptTokens <= 0 || generatedTokens <= 0)
                throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
            if (promptTokens > settings.ArticleContextTokens - settings.ArticleVerifierMaxOutputTokens ||
                promptTokens + generatedTokens > settings.ArticleContextTokens)
                throw new AnalysisInputTooLargeException();
            VerificationEnvelope envelope = JsonSerializer.Deserialize<VerificationEnvelope>(
                root.GetProperty("message").GetProperty("content").GetString()!, JsonOptions)
                ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            Validate(claims, envelope.Verdicts);
            return new(envelope.Verdicts, root.GetProperty("model").GetString() ?? model, ArticleVerificationPrompt.Version);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
        }
    }

    private static void Validate(IReadOnlyList<GeneratedArticleClaim> claims, IReadOnlyList<GeneratedClaimVerdict>? verdicts)
    {
        if (verdicts is null || verdicts.Count != claims.Count || verdicts.Any(x => x is null) ||
            verdicts.Select(x => x.ClaimId).Distinct(StringComparer.Ordinal).Count() != verdicts.Count ||
            verdicts.Any(x => !claims.Any(c => c.ClaimId == x.ClaimId) ||
                x.Verdict is not ("supported" or "unsupported" or "uncertain") ||
                string.IsNullOrWhiteSpace(x.Reason) || x.Reason.Length > 500))
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }

    private sealed record VerificationEnvelope(IReadOnlyList<GeneratedClaimVerdict> Verdicts);
}
