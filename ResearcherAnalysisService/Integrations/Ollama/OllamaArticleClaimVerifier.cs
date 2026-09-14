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

public sealed class OllamaArticleClaimVerifier(HttpClient client, IOptions<AiOptions> options,
    ArticleEvaluationAttemptRecorder? attemptRecorder = null) : IArticleClaimVerifier
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

        string input = ArticleVerificationInput.CreateClaims(language, claims, sourceSpans);
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
        using ArticleEvaluationAttemptRecorder.Attempt? attempt = attemptRecorder?.Begin("Ollama", model);
        string attemptStatus = "failed";
        string? attemptError = "provider_failure";
        ArticleEvaluationUsage usage = new();
        try
        {
            using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                attemptError = "model_not_found";
                throw new AnalysisUnavailableException("Ollama could not find the configured verifier model.");
            }
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                attemptError = "provider_rejected";
                string error = await response.Content.ReadAsStringAsync(cancellationToken);
                if (error.Contains("context", StringComparison.OrdinalIgnoreCase) || error.Contains("tokens", StringComparison.OrdinalIgnoreCase))
                {
                    attemptError = "input_too_large";
                    throw new AnalysisInputTooLargeException();
                }
            }
            if (!response.IsSuccessStatusCode)
            {
                attemptError = "provider_rejected";
                throw new HttpRequestException("Local verifier request failed.", null, response.StatusCode);
            }
            try
            {
                using JsonDocument document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                JsonElement root = document.RootElement;
                usage = OllamaEvaluationUsage.Parse(root);
                OllamaArticleReviewGenerator.ValidateCompletion(root, settings.ArticleContextTokens,
                    settings.ArticleVerifierMaxOutputTokens);
                VerificationEnvelope envelope = JsonSerializer.Deserialize<VerificationEnvelope>(
                    root.GetProperty("message").GetProperty("content").GetString()!, JsonOptions)
                    ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
                Validate(claims, envelope.Verdicts);
                attemptStatus = "completed";
                attemptError = null;
                return new(envelope.Verdicts, root.GetProperty("model").GetString() ?? model, ArticleVerificationPrompt.Version);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                attemptError = "invalid_provider_response";
                throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            }
        }
        catch (OperationCanceledException)
        {
            attemptStatus = "cancelled";
            attemptError = "cancelled";
            throw;
        }
        catch (InvalidAnalysisException exception)
        {
            attemptError = InvalidAnalysisException.CodeFor(exception.Reason);
            throw;
        }
        finally
        {
            attempt?.Complete(attemptStatus, attemptError, usage);
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
