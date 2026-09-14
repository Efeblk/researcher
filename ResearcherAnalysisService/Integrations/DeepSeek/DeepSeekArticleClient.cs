using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.DeepSeek;

public sealed class DeepSeekArticleClient(HttpClient client, IOptions<ArticleEvaluationOptions> options,
    ArticleEvaluationAttemptRecorder attemptRecorder)
{
    public const string Model = "deepseek-flash";
    private static readonly Uri Endpoint = new("https://api.deepseek.com/chat/completions");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public async Task<DeepSeekArticleResult> GenerateAsync(string instructions, string input,
        int maxOutputTokens, CancellationToken cancellationToken)
    {
        ArticleEvaluationOptions settings = options.Value;
        string? apiKey = settings.DeepSeek.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AnalysisUnavailableException("The DeepSeek evaluation profile is not configured.");
        object body = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = instructions },
                new { role = "user", content = input }
            },
            stream = false,
            max_tokens = maxOutputTokens,
            response_format = new { type = "json_object" },
            thinking = new { type = "enabled" },
            reasoning_effort = "high"
        };
        string serializedBody = JsonSerializer.Serialize(body, JsonOptions);
        if (Encoding.UTF8.GetByteCount(serializedBody) > settings.ContextTokens - maxOutputTokens - 512)
            throw new AnalysisInputTooLargeException();
        using HttpRequestMessage message = new(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(serializedBody, Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using ArticleEvaluationAttemptRecorder.Attempt attempt = attemptRecorder.Begin("DeepSeek", Model);
        string attemptStatus = "failed";
        string? attemptError = "provider_failure";
        ArticleEvaluationUsage usage = new();
        try
        {
            using HttpResponseMessage response = await client.SendAsync(message,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                attemptError = "provider_unauthorized";
                throw new AnalysisUnavailableException("DeepSeek rejected the configured evaluation credential.");
            }
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                attemptError = "provider_rejected";
                string error = await response.Content.ReadAsStringAsync(cancellationToken);
                if (error.Contains("context", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("length", StringComparison.OrdinalIgnoreCase))
                {
                    attemptError = "input_too_large";
                    throw new AnalysisInputTooLargeException();
                }
            }
            if (!response.IsSuccessStatusCode)
            {
                attemptError = "provider_rejected";
                throw new HttpRequestException("DeepSeek evaluation request failed.", null, response.StatusCode);
            }

            try
            {
                using JsonDocument document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                JsonElement root = document.RootElement;
                usage = ParseUsage(root, settings.DeepSeek);
                JsonElement choices = root.GetProperty("choices");
                if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1)
                    throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
                JsonElement choice = choices[0];
                string? finishReason = choice.GetProperty("finish_reason").GetString();
                if (finishReason == "length")
                    throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
                if (finishReason != "stop")
                    throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
                string? returnedModel = root.GetProperty("model").GetString();
                string? answer = choice.GetProperty("message").GetProperty("content").GetString();
                if (string.IsNullOrWhiteSpace(returnedModel) || string.IsNullOrWhiteSpace(answer))
                    throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
                usage = usage with { ReturnedModel = returnedModel };
                attemptStatus = "completed";
                attemptError = null;
                return new(answer, returnedModel);
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
            attempt.Complete(attemptStatus, attemptError, usage);
        }
    }

    private static ArticleEvaluationUsage ParseUsage(JsonElement root, DeepSeekEvaluationOptions pricing)
    {
        string? model = Text(root, "model");
        if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object ||
            !TryCount(usage, "prompt_tokens", out int input) ||
            !TryCount(usage, "completion_tokens", out int output) ||
            !TryCount(usage, "prompt_cache_hit_tokens", out int cacheHit) ||
            !TryCount(usage, "prompt_cache_miss_tokens", out int cacheMiss) ||
            !TryCount(usage, "total_tokens", out int total) ||
            input != cacheHit + cacheMiss || total != input + output)
            return new(ReturnedModel: model);
        int? thinking = null;
        if (usage.TryGetProperty("completion_tokens_details", out JsonElement details) &&
            details.ValueKind == JsonValueKind.Object && details.TryGetProperty("reasoning_tokens", out JsonElement reasoning))
        {
            if (reasoning.ValueKind != JsonValueKind.Number || !reasoning.TryGetInt32(out int parsed) || parsed < 0 || parsed > output)
                return new(ReturnedModel: model);
            thinking = parsed;
        }
        decimal? estimate = Estimate(pricing, cacheMiss, cacheHit, output);
        return new(model, input, output, cacheHit, null, thinking, estimate,
            estimate.HasValue ? pricing.PricingVersion : null);
    }

    private static decimal? Estimate(DeepSeekEvaluationOptions pricing, int cacheMiss, int cacheHit, int output)
    {
        if (string.IsNullOrWhiteSpace(pricing.PricingVersion) ||
            pricing.InputUsdPerMillionTokens is not >= 0 ||
            pricing.CacheHitUsdPerMillionTokens is not >= 0 ||
            pricing.OutputUsdPerMillionTokens is not >= 0)
            return null;
        return decimal.Round((cacheMiss * pricing.InputUsdPerMillionTokens.Value +
            cacheHit * pricing.CacheHitUsdPerMillionTokens.Value + output * pricing.OutputUsdPerMillionTokens.Value) /
            1_000_000m, 9, MidpointRounding.AwayFromZero);
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static bool TryCount(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) && value >= 0;
    }
}

public sealed record DeepSeekArticleResult(string Json, string Model);
