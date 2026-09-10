using System.Net;
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
    IOptions<GeminiOptions> geminiOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public async Task<GeminiArticleResult> GenerateAsync(string model, string instructions, string input,
        JsonObject schema, int maxOutputTokens, CancellationToken cancellationToken)
    {
        AiOptions settings = aiOptions.Value;
        string? apiKey = geminiOptions.Value.ApiKey;
        if (settings.ArticleProvider != "Gemini" || string.IsNullOrWhiteSpace(model))
            throw new AnalysisUnavailableException("Article analysis is not configured to use Gemini.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AnalysisUnavailableException("Configure Gemini:ApiKey with user secrets.");
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
                thinkingConfig = new { thinkingLevel = "high", includeThoughts = false }
            }
        };
        string serializedBody = JsonSerializer.Serialize(body, JsonOptions);
        if (Encoding.UTF8.GetByteCount(serializedBody) > settings.ArticleContextTokens - maxOutputTokens - 512)
            throw new AnalysisInputTooLargeException();
        string escapedModel = Uri.EscapeDataString(model);
        using HttpRequestMessage message = new(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{escapedModel}:generateContent")
        {
            Content = new StringContent(serializedBody, Encoding.UTF8, "application/json")
        };
        message.Headers.Add("x-goog-api-key", apiKey);
        using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
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

        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            JsonElement candidates = root.GetProperty("candidates");
            if (candidates.GetArrayLength() != 1)
                throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            JsonElement candidate = candidates[0];
            string? finishReason = candidate.GetProperty("finishReason").GetString();
            if (finishReason == "MAX_TOKENS")
                throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
            if (finishReason != "STOP")
                throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            int promptTokens = root.GetProperty("usageMetadata").GetProperty("promptTokenCount").GetInt32();
            if (promptTokens <= 0)
                throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
            if (promptTokens > settings.ArticleContextTokens - maxOutputTokens)
                throw new AnalysisInputTooLargeException();
            string answer = string.Concat(candidate.GetProperty("content").GetProperty("parts")
                .EnumerateArray().Where(part => !part.TryGetProperty("thought", out JsonElement thought) ||
                    thought.ValueKind != JsonValueKind.True).Select(part => part.GetProperty("text").GetString()));
            if (string.IsNullOrWhiteSpace(answer))
                throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
            return new(answer, root.GetProperty("modelVersion").GetString() ?? settings.ArticleModel);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
        }
    }
}

public sealed record GeminiArticleResult(string Json, string Model);
