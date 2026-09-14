using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Ollama;

public sealed class OllamaArticleReviewVerifier(HttpClient client, IOptions<AiOptions> options,
    ArticleEvaluationAttemptRecorder? attemptRecorder = null)
    : IArticleReviewVerifier
{
    private bool _capabilityVerified;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedArticleReviewVerification> VerifyAsync(
        string role,
        string language,
        IReadOnlyList<GeneratedArticleReviewFinding> findings,
        IReadOnlyList<ArticleSourceSpan> sourceSpans,
        CancellationToken cancellationToken)
    {
        AiOptions settings = options.Value;
        Uri baseUrl = OllamaArticleReviewGenerator.ValidateSettings(settings);
        await EnsureCapabilitiesAsync(baseUrl, cancellationToken);
        string model = string.IsNullOrWhiteSpace(settings.ArticleVerifierModel)
            ? settings.ArticleModel : settings.ArticleVerifierModel;
        if (model.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase) || model.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase))
            throw new AnalysisUnavailableException("Choose a downloaded local verifier model.");
        string input = ArticleVerificationInput.CreateFindings(role, language, findings, sourceSpans);
        object body = new
        {
            model, stream = false, think = true, truncate = false, shift = false,
            format = ArticleReviewVerificationPrompt.CreateSchema(findings.Select(finding => finding.FindingId)),
            messages = new[]
            {
                new { role = "system", content = ArticleReviewVerificationPrompt.Instructions },
                new { role = "user", content = input }
            },
            options = new { temperature = 0, num_ctx = settings.ArticleContextTokens, num_predict = settings.ArticleVerifierMaxOutputTokens }
        };
        string serializedBody = JsonSerializer.Serialize(body, JsonOptions);
        if (Encoding.UTF8.GetByteCount(serializedBody) > settings.ArticleContextTokens - settings.ArticleVerifierMaxOutputTokens - 512)
            throw new AnalysisInputTooLargeException();
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(baseUrl, "/api/chat"))
        {
            Content = new StringContent(serializedBody, Encoding.UTF8, "application/json")
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
                if (error.Contains("context", StringComparison.OrdinalIgnoreCase) || error.Contains("token", StringComparison.OrdinalIgnoreCase))
                {
                    attemptError = "input_too_large";
                    throw new AnalysisInputTooLargeException();
                }
            }
            if (!response.IsSuccessStatusCode)
            {
                attemptError = "provider_rejected";
                throw new HttpRequestException("Local article review verifier failed.", null, response.StatusCode);
            }
            try
            {
                using JsonDocument document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                JsonElement root = document.RootElement;
                usage = OllamaEvaluationUsage.Parse(root);
                OllamaArticleReviewGenerator.ValidateCompletion(root, settings.ArticleContextTokens, settings.ArticleVerifierMaxOutputTokens);
                VerificationEnvelope envelope = JsonSerializer.Deserialize<VerificationEnvelope>(
                    root.GetProperty("message").GetProperty("content").GetString()!, JsonOptions)
                    ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
                attemptStatus = "completed";
                attemptError = null;
                return new(envelope.Verdicts, root.GetProperty("model").GetString() ?? model,
                    ArticleReviewVerificationPrompt.Version);
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

    private sealed record VerificationEnvelope(IReadOnlyList<GeneratedArticleReviewVerdict> Verdicts);

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
            if (!Version.TryParse(value.Split('-', '+')[0], out Version? version) || version < new Version(0, 33, 3))
                throw new AnalysisUnavailableException("Article review requires Ollama 0.33.3 or newer for untruncated context handling.");
            _capabilityVerified = true;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AnalysisUnavailableException("The local Ollama version could not be verified.");
        }
    }
}
