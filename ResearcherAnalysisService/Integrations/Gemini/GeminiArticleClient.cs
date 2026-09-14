using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed class GeminiArticleClient(HttpClient client, IOptions<AiOptions> aiOptions,
    IOptions<GeminiOptions> geminiOptions, IGeminiUsageRepository usageRepository,
    ArticleEvaluationAttemptRecorder? attemptRecorder = null,
    ArticleReviewDispatchContext? reviewDispatchContext = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public async Task<GeminiProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        AiOptions settings = aiOptions.Value;
        string? apiKey = geminiOptions.Value.ApiKey;
        if (settings.ArticleProvider != "Gemini")
            return new("Disabled");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(settings.ArticleModel))
            return new("NotConfigured");

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            string model = settings.ArticleModel.Trim();
            using HttpRequestMessage message = new(HttpMethod.Get,
                $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}");
            message.Headers.Add("x-goog-api-key", apiKey.Trim());
            using HttpResponseMessage response = await client.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new("Unauthorized");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new("RateLimited");
            if ((int)response.StatusCode >= 500)
                return new("Unavailable");
            if (!response.IsSuccessStatusCode)
                return new("UnexpectedResponse");

            await response.Content.LoadIntoBufferAsync(1024 * 1024, timeout.Token);
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            JsonElement root = document.RootElement;
            bool expectedName = root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String &&
                name.GetString() == $"models/{model}";
            bool supportsGeneration = root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("supportedGenerationMethods", out JsonElement methods) &&
                methods.ValueKind == JsonValueKind.Array && methods.EnumerateArray().Any(method =>
                    method.ValueKind == JsonValueKind.String && method.GetString() == "generateContent");
            return new(expectedName && supportsGeneration ? "Healthy" : "UnexpectedResponse");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new("Timeout");
        }
        catch (HttpRequestException)
        {
            return new("Unavailable");
        }
        catch (JsonException)
        {
            return new("UnexpectedResponse");
        }
    }

    public async Task<GeminiArticleResult> GenerateAsync(string model, string instructions, string input,
        JsonObject schema, int maxOutputTokens, string thinkingLevel, CancellationToken cancellationToken)
    {
        GeminiArticleInvocationResult invocation = await GenerateAttemptAsync(model, instructions, input,
            schema, maxOutputTokens, thinkingLevel, cancellationToken);
        if (invocation.Result is not null)
            return invocation.Result;
        throw new InvalidAnalysisException(invocation.Failure ?? AnalysisFailure.InvalidReport);
    }

    public async Task<GeminiArticleInvocationResult> GenerateAttemptAsync(string model, string instructions,
        string input, JsonObject schema, int maxOutputTokens, string thinkingLevel,
        CancellationToken cancellationToken)
    {
        AiOptions settings = aiOptions.Value;
        string? apiKey = geminiOptions.Value.ApiKey;
        if (settings.ArticleProvider != "Gemini" || string.IsNullOrWhiteSpace(model))
            throw new AnalysisUnavailableException("Article analysis is not configured to use Gemini.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AnalysisUnavailableException("Configure Gemini:ApiKey with user secrets.");
        string serializedBody = CreateSerializedBody(instructions, input, schema, maxOutputTokens, thinkingLevel);
        if (Encoding.UTF8.GetByteCount(serializedBody) > settings.ArticleContextTokens - maxOutputTokens - 512)
            throw new AnalysisInputTooLargeException();
        string escapedModel = Uri.EscapeDataString(model);
        using HttpRequestMessage message = new(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{escapedModel}:generateContent")
        {
            Content = new StringContent(serializedBody, Encoding.UTF8, "application/json")
        };
        message.Headers.Add("x-goog-api-key", apiKey);
        Guid attemptId = reviewDispatchContext?.AttemptId ?? Guid.NewGuid();
        DateTime startedAt = DateTime.UtcNow;
        using (CancellationTokenSource beginTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            beginTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await usageRepository.BeginAsync(attemptId, startedAt, model, beginTimeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new AnalysisUnavailableException("Gemini usage tracking is unavailable.");
            }
        }

        GeminiUsageCompletion completion = new() { Outcome = "Unknown" };
        GeminiArticleResult? result = null;
        AnalysisFailure? failure = null;
        ExceptionDispatchInfo? delayedException = null;
        bool usagePersisted = false;
        using ArticleEvaluationAttemptRecorder.Attempt? providerAttempt =
            attemptRecorder?.Begin("Gemini", model);
        try
        {
            using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
            int httpStatus = (int)response.StatusCode;
            completion = completion with { HttpStatus = httpStatus,
                Outcome = response.IsSuccessStatusCode ? "InvalidResponse" : "Rejected" };
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AnalysisUnavailableException("Gemini rejected the configured API key.");
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                string error = await response.Content.ReadAsStringAsync(cancellationToken);
                if (error.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("context", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("too long", StringComparison.OrdinalIgnoreCase))
                    throw new AnalysisInputTooLargeException();
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Gemini article request failed.", null, response.StatusCode);

            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                completion = completion with { Outcome = "InvalidJson" };
                throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            }
            using (document)
            {
                JsonElement root = document.RootElement;
                completion = GeminiUsagePricing.Parse(root, model, startedAt, httpStatus, "InvalidResponse");
                if (!string.Equals(completion.ReturnedModel, model, StringComparison.Ordinal) ||
                    !completion.UsageValidForAttribution ||
                    string.Equals(model, "gemini-3.8-flash", StringComparison.Ordinal) &&
                    (completion.EstimatedUsd is null || string.IsNullOrWhiteSpace(completion.PricingVersion)))
                    throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
                try
                {
                    JsonElement candidates = root.GetProperty("candidates");
                    if (candidates.GetArrayLength() != 1)
                        throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
                    JsonElement candidate = candidates[0];
                    string? finishReason = candidate.GetProperty("finishReason").GetString();
                    if (finishReason == "MAX_TOKENS")
                        throw new InvalidAnalysisException(AnalysisFailure.OutputLimit);
                    if (finishReason != "STOP")
                        throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
                    int promptTokens = root.GetProperty("usageMetadata").GetProperty("promptTokenCount").GetInt32();
                    if (promptTokens <= 0)
                        throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
                    if (promptTokens > settings.ArticleContextTokens - maxOutputTokens)
                        throw new AnalysisInputTooLargeException();
                    string answer = string.Concat(candidate.GetProperty("content").GetProperty("parts")
                        .EnumerateArray().Where(part =>
                            !part.TryGetProperty("thought", out JsonElement thought) ||
                            thought.ValueKind != JsonValueKind.True)
                        .Select(part => part.GetProperty("text").GetString()));
                    if (string.IsNullOrWhiteSpace(answer))
                        throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
                    completion = completion with { Outcome = "Success" };
                    result = new(answer, completion.ReturnedModel!);
                }
                catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
                    InvalidOperationException)
                {
                    throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
                }
            }
        }
        catch (OperationCanceledException exception)
        {
            completion = completion with
            {
                Outcome = cancellationToken.IsCancellationRequested ? "Cancelled" : "Timeout"
            };
            delayedException = ExceptionDispatchInfo.Capture(exception);
        }
        catch (InvalidAnalysisException exception)
        {
            completion = completion with { Outcome = OutcomeFor(exception.Reason) };
            failure = exception.Reason;
        }
        catch (HttpRequestException exception)
        {
            if (completion.Outcome != "Rejected")
                completion = completion with { Outcome = "NetworkFailure" };
            delayedException = ExceptionDispatchInfo.Capture(exception);
        }
        catch (Exception exception)
        {
            delayedException = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            providerAttempt?.Complete(AttemptStatus(completion.Outcome), AttemptErrorCode(completion.Outcome),
                ToEvaluationUsage(completion));
            using CancellationTokenSource completionTimeout = new(TimeSpan.FromSeconds(5));
            try
            {
                await usageRepository.CompleteAsync(attemptId, DateTime.UtcNow, completion,
                    completionTimeout.Token);
                usagePersisted = true;
                reviewDispatchContext?.Capture(completion);
            }
            catch (Exception)
            {
                // The durable Pending row intentionally remains an unknown-cost attempt.
            }
        }
        delayedException?.Throw();
        return new(result, failure, completion, usagePersisted) { AttemptId = attemptId };
    }

    public static string CreateSerializedBody(string instructions, string input, JsonObject schema,
        int maxOutputTokens, string thinkingLevel)
    {
        object body = new
        {
            systemInstruction = new { parts = new[] { new { text = instructions } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = input } } } },
            generationConfig = new
            {
                temperature = 0,
                maxOutputTokens,
                responseMimeType = "application/json",
                responseJsonSchema = schema,
                thinkingConfig = new { thinkingLevel, includeThoughts = false }
            }
        };
        return JsonSerializer.Serialize(body, JsonOptions);
    }

    private static ArticleEvaluationUsage ToEvaluationUsage(GeminiUsageCompletion completion)
    {
        if (!completion.UsageValidForAttribution)
            return new(ReturnedModel: completion.ReturnedModel);
        long? output = null;
        if (completion.CandidateTokenCount.HasValue && completion.ThoughtTokenCount.HasValue)
        {
            try { output = checked(completion.CandidateTokenCount.Value + completion.ThoughtTokenCount.Value); }
            catch (OverflowException) { output = null; }
        }
        return new(completion.ReturnedModel, ToInt(completion.PromptTokenCount), ToInt(output),
            ToInt(completion.CachedTokenCount), null, ToInt(completion.ThoughtTokenCount),
            completion.EstimatedUsd, completion.PricingVersion);
    }

    private static int? ToInt(long? value) => value is >= 0 and <= int.MaxValue ? (int)value.Value : null;

    private static string AttemptStatus(string outcome) => outcome switch
    {
        "Success" => "completed",
        "Cancelled" => "cancelled",
        "Timeout" => "timed_out",
        _ => "failed"
    };

    private static string? AttemptErrorCode(string outcome) => outcome switch
    {
        "Success" => null,
        "Cancelled" => "cancelled",
        "Timeout" => "timeout",
        "OutputLimit" => "output_limit",
        "IncompleteOutput" => "incomplete_output",
        "InvalidJson" => "invalid_json",
        "InvalidEvidence" => "invalid_evidence",
        "InvalidResponse" => "invalid_provider_response",
        "Rejected" => "provider_rejected",
        "NetworkFailure" => "provider_failure",
        _ => "provider_failure"
    };

    private static string OutcomeFor(AnalysisFailure reason) => reason switch
    {
        AnalysisFailure.OutputLimit => "OutputLimit",
        AnalysisFailure.IncompleteOutput => "IncompleteOutput",
        AnalysisFailure.InvalidJson => "InvalidJson",
        AnalysisFailure.InvalidEvidence => "InvalidEvidence",
        _ => "InvalidResponse"
    };
}

public sealed record GeminiArticleResult(string Json, string Model);
public sealed record GeminiArticleInvocationResult(GeminiArticleResult? Result, AnalysisFailure? Failure,
    GeminiUsageCompletion Completion, bool UsagePersisted)
{
    public Guid AttemptId { get; init; }
}
