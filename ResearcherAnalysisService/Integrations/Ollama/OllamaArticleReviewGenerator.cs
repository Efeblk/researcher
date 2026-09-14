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

public sealed class OllamaArticleReviewGenerator(HttpClient client, IOptions<AiOptions> options,
    ArticleEvaluationAttemptRecorder? attemptRecorder = null)
    : IArticleReviewGenerator
{
    private bool _capabilityVerified;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedArticleReviewPass> GenerateAsync(
        string role,
        string language,
        string sourceKind,
        IReadOnlyList<ArticleSourceSpan> sourceSpans,
        CancellationToken cancellationToken)
    {
        AiOptions settings = options.Value;
        Uri baseUrl = ValidateSettings(settings);
        await EnsureCapabilitiesAsync(baseUrl, cancellationToken);
        var sources = sourceSpans.Select(span => new { span.SourceId, span.PageNumber, span.Text });
        string input = JsonSerializer.Serialize(new { role, language, sourceKind, sources }, JsonOptions);
        object body = new
        {
            model = settings.ArticleModel, stream = false, think = false, truncate = false, shift = false,
            format = ArticleReviewPrompt.CreateSchema(role, sourceSpans.Select(span => span.SourceId)),
            messages = new[]
            {
                new { role = "system", content = ArticleReviewPrompt.InstructionsForRole(role) },
                new { role = "user", content = input }
            },
            options = new { temperature = 0, num_ctx = settings.ArticleContextTokens, num_predict = settings.ArticleMaxOutputTokens }
        };
        string serializedBody = JsonSerializer.Serialize(body, JsonOptions);
        if (Encoding.UTF8.GetByteCount(serializedBody) > settings.ArticleContextTokens - settings.ArticleMaxOutputTokens - 512)
            throw new AnalysisInputTooLargeException();
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(baseUrl, "/api/chat"))
        {
            Content = new StringContent(serializedBody, Encoding.UTF8, "application/json")
        };
        using ArticleEvaluationAttemptRecorder.Attempt? attempt =
            attemptRecorder?.Begin("Ollama", settings.ArticleModel);
        string attemptStatus = "failed";
        string? attemptError = "provider_failure";
        ArticleEvaluationUsage usage = new();
        try
        {
            using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                attemptError = "model_not_found";
                throw new AnalysisUnavailableException("Ollama could not find the configured article model.");
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
                throw new HttpRequestException("Local article review request failed.", null, response.StatusCode);
            }
            try
            {
                using JsonDocument document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                JsonElement root = document.RootElement;
                usage = OllamaEvaluationUsage.Parse(root);
                ValidateCompletion(root, settings.ArticleContextTokens, settings.ArticleMaxOutputTokens);
                ReviewEnvelope envelope = JsonSerializer.Deserialize<ReviewEnvelope>(
                    root.GetProperty("message").GetProperty("content").GetString()!, JsonOptions)
                    ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
                attemptStatus = "completed";
                attemptError = null;
                return new(envelope.Role, envelope.Findings,
                    root.GetProperty("model").GetString() ?? settings.ArticleModel, ArticleReviewPrompt.Version);
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

    internal static Uri ValidateSettings(AiOptions settings)
    {
        if (settings.ArticleProvider != "Ollama" || string.IsNullOrWhiteSpace(settings.ArticleModel) ||
            !Uri.TryCreate(settings.OllamaBaseUrl, UriKind.Absolute, out Uri? baseUrl) || !baseUrl.IsLoopback ||
            baseUrl.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(baseUrl.UserInfo))
            throw new AnalysisUnavailableException("Article review requires a configured local Ollama model.");
        if (settings.ArticleModel.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase) ||
            settings.ArticleModel.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase))
            throw new AnalysisUnavailableException("Choose a downloaded local model instead of a cloud model.");
        return baseUrl;
    }

    internal static void ValidateCompletion(JsonElement root, int contextTokens, int outputTokens)
    {
        if (!root.GetProperty("done").GetBoolean() || root.GetProperty("done_reason").GetString() != "stop")
            throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
        int promptTokens = root.GetProperty("prompt_eval_count").GetInt32();
        int generatedTokens = root.GetProperty("eval_count").GetInt32();
        if (promptTokens <= 0 || generatedTokens <= 0)
            throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
        if (promptTokens > contextTokens - outputTokens || promptTokens + generatedTokens > contextTokens)
            throw new AnalysisInputTooLargeException();
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
            if (!Version.TryParse(value.Split('-', '+')[0], out Version? version) || version < new Version(0, 33, 3))
                throw new AnalysisUnavailableException("Article review requires Ollama 0.33.3 or newer for untruncated context handling.");
            _capabilityVerified = true;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AnalysisUnavailableException("The local Ollama version could not be verified.");
        }
    }

    private sealed record ReviewEnvelope(string Role, IReadOnlyList<GeneratedArticleReviewFinding> Findings);
}
