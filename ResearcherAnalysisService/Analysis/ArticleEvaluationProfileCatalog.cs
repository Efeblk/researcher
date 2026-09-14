using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.DeepSeek;

namespace ResearcherAnalysisService.Analysis;

public sealed class ArticleEvaluationProfileCatalog(
    IOptions<AiOptions> aiOptions,
    IOptions<GeminiOptions> geminiOptions,
    IOptions<ArticleEvaluationOptions> evaluationOptions)
{
    public const string SettingsVersion = "article-evaluation-v1";
    public const string GeminiProfileId = "gemini-baseline";
    public const string OllamaProfileId = "ollama-qwen-baseline";
    public const string DeepSeekProfileId = "deepseek-candidate";
    public const string GeminiModel = "gemini-3.8-flash";
    public const string OllamaModel = "qwen3.8:27b-q4_K_M";
    public const string OllamaModelDigest = "25b843619e944cd0ae6069f94ff4e5e26a16e109ccbc0a66a0f05979ed70098e";

    public IReadOnlyList<ArticleEvaluationProfileDefinition> GetDefinitions()
    {
        ArticleEvaluationOptions evaluation = evaluationOptions.Value;
        int cloudContext = Math.Min(evaluation.ContextTokens, 131_072);
        int localContext = Math.Min(evaluation.ContextTokens, 32_768);
        int generationOutput = Math.Min(evaluation.MaxOutputTokens, 8_192);
        int verifierOutput = Math.Min(evaluation.VerifierMaxOutputTokens, 8_192);
        int localGenerationOutput = Math.Min(generationOutput, 4_096);
        int localVerifierOutput = Math.Min(verifierOutput, 4_096);
        return
        [
            Create(GeminiProfileId, "Gemini baseline", "Gemini", GeminiModel, null,
                cloudContext, generationOutput, verifierOutput, "high", "high", "0", "high",
                string.IsNullOrWhiteSpace(geminiOptions.Value.ApiKey) ? "not_configured" : "configured",
                string.IsNullOrWhiteSpace(geminiOptions.Value.ApiKey) ? "missing_credential" : null, false),
            Create(OllamaProfileId, "Ollama / Qwen baseline", "Ollama", OllamaModel,
                OllamaModelDigest, localContext, localGenerationOutput, localVerifierOutput,
                "disabled", "enabled", "0", "provider_default",
                HasSafeOllamaEndpoint(aiOptions.Value.OllamaBaseUrl) ? "configured" : "not_configured",
                HasSafeOllamaEndpoint(aiOptions.Value.OllamaBaseUrl) ? null : "invalid_local_endpoint", true),
            Create(DeepSeekProfileId, "DeepSeek candidate", "DeepSeek", DeepSeekArticleClient.Model, null,
                cloudContext, generationOutput, verifierOutput, "enabled", "enabled", "not_applicable", "high",
                string.IsNullOrWhiteSpace(evaluation.DeepSeek.ApiKey) ? "not_configured" : "configured",
                string.IsNullOrWhiteSpace(evaluation.DeepSeek.ApiKey) ? "missing_credential" : null, false)
        ];
    }

    public ArticleEvaluationProfileDefinition GetRequired(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArticleEvaluationRequestException(400, "unknown_profile", "Choose a known evaluation profile.");
        return GetDefinitions().SingleOrDefault(value => value.ProfileId == profileId)
            ?? throw new ArticleEvaluationRequestException(400, "unknown_profile", "Choose a known evaluation profile.");
    }

    private ArticleEvaluationProfileDefinition Create(string profileId, string displayName, string provider,
        string requestedModel, string? modelRevision, int contextTokens, int maxOutputTokens,
        int verifierMaxOutputTokens, string generationThinking, string verifierThinking, string temperature,
        string reasoningEffort, string availability,
        string? availabilityReasonCode, bool localOnly)
    {
        Dictionary<string, string> settings = new(StringComparer.Ordinal)
        {
            ["claimVerificationPromptVersion"] = ArticleVerificationPrompt.Version,
            ["contextTokens"] = contextTokens.ToString(CultureInfo.InvariantCulture),
            ["generationMaxOutputTokens"] = maxOutputTokens.ToString(CultureInfo.InvariantCulture),
            ["generationThinking"] = generationThinking,
            ["maximumInputBytes"] = evaluationOptions.Value.MaximumInputBytes.ToString(CultureInfo.InvariantCulture),
            ["reasoningEffort"] = reasoningEffort,
            ["reviewGenerationPromptVersion"] = ArticleReviewPrompt.Version,
            ["reviewVerificationPromptVersion"] = ArticleReviewVerificationPrompt.Version,
            ["temperature"] = temperature,
            ["timeoutSeconds"] = evaluationOptions.Value.TimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            ["verifierMaxOutputTokens"] = verifierMaxOutputTokens.ToString(CultureInfo.InvariantCulture),
            ["verifierModel"] = requestedModel,
            ["verifierThinking"] = verifierThinking
        };
        string fingerprintInput = string.Join("\n",
            new[] { SettingsVersion, profileId, provider, requestedModel, modelRevision ?? string.Empty }
                .Concat(settings.OrderBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value => $"{value.Key}={value.Value}")));
        string fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))).ToLowerInvariant();
        return new(profileId, displayName, provider, requestedModel, modelRevision, fingerprint,
            SettingsVersion, settings, availability, availabilityReasonCode, localOnly,
            contextTokens, maxOutputTokens, verifierMaxOutputTokens);
    }

    private static bool HasSafeOllamaEndpoint(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.IsLoopback &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
}
public sealed record ArticleEvaluationProfileDefinition(
    string ProfileId,
    string DisplayName,
    string Provider,
    string RequestedModel,
    string? ModelRevision,
    string SettingsFingerprint,
    string SettingsVersion,
    IReadOnlyDictionary<string, string> ExecutionSettings,
    string Availability,
    string? AvailabilityReasonCode,
    bool LocalOnly,
    int ContextTokens,
    int MaxOutputTokens,
    int VerifierMaxOutputTokens)
{
    public ArticleEvaluationProfile ToContract() => new(ProfileId, DisplayName, Provider, RequestedModel,
        ModelRevision, SettingsFingerprint, SettingsVersion,
        [ArticleEvaluationTaskKinds.Calibration, ArticleEvaluationTaskKinds.Review,
            ArticleEvaluationTaskKinds.CrossCheck], Availability, AvailabilityReasonCode, LocalOnly)
    { ExecutionSettings = ExecutionSettings };
}

public sealed class ArticleEvaluationRequestException(int statusCode, string errorCode, string message)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}
