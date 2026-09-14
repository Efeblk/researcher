using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ResearcherAnalysisService.Analysis;

public sealed class ArticleReviewStageExecutor(
    ArticleReviewer reviewer,
    IArticleReviewGenerator generator,
    IArticleReviewVerifier verifier,
    ArticleReviewDispatchContext dispatchContext,
    IOptions<AiOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ArticleReviewRuntimeConfiguration GetConfiguration()
    {
        AiOptions value = options.Value;
        if (value.ArticleProvider != "Gemini")
            throw new AnalysisUnavailableException(
                "Durable staged article review currently requires Gemini cost attribution.");
        string verifierModel = string.IsNullOrWhiteSpace(value.ArticleVerifierModel)
            ? value.ArticleModel : value.ArticleVerifierModel;
        string? generationPricing;
        string? verifierPricing;
        if (!GeminiUsagePricing.TryGetRates(value.ArticleModel, DateTime.UtcNow, out _, out _, out _,
                out generationPricing) ||
            !GeminiUsagePricing.TryGetRates(verifierModel, DateTime.UtcNow, out _, out _, out _,
                out verifierPricing))
            throw new AnalysisUnavailableException("Article review pricing is unavailable for the configured model.");
        string pricingVersion = generationPricing == verifierPricing
            ? generationPricing! : $"{generationPricing},{verifierPricing}";

        bool recoverySupported = value.ArticleGenerationThinkingLevel ==
            ArticleReviewGenerationRecovery.InitialThinkingLevel;
        object identity = new
        {
            value.ArticleProvider,
            value.ArticleModel,
            VerifierModel = verifierModel,
            value.ArticleGenerationThinkingLevel,
            value.ArticleVerifierThinkingLevel,
            value.ArticleContextTokens,
            value.ArticleMaxOutputTokens,
            value.ArticleVerifierMaxOutputTokens,
            GenerationPromptVersion = ArticleReviewPrompt.Version,
            VerificationPromptVersion = ArticleReviewVerificationPrompt.Version,
            GenerationRecoveryPolicyVersion = recoverySupported
                ? ArticleReviewGenerationRecovery.PolicyVersion : null,
            GenerationRecoveryThinkingLevel = recoverySupported
                ? ArticleReviewGenerationRecovery.RecoveryThinkingLevel : null,
            PricingVersion = pricingVersion
        };
        string fingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(identity, JsonOptions));
        return new(fingerprint, value.ArticleProvider, value.ArticleModel, verifierModel,
            ArticleReviewPrompt.Version, ArticleReviewVerificationPrompt.Version,
            value.ArticleMaxOutputTokens, value.ArticleVerifierMaxOutputTokens,
            value.ArticleGenerationThinkingLevel, value.ArticleVerifierThinkingLevel, pricingVersion)
        {
            GenerationRecoveryPolicyVersion = recoverySupported
                ? ArticleReviewGenerationRecovery.PolicyVersion : null,
            GenerationRecoveryThinkingLevel = recoverySupported
                ? ArticleReviewGenerationRecovery.RecoveryThinkingLevel : null
        };
    }

    public ArticleReviewStageQuote Quote(ArticleReviewStageQuoteRequest request)
    {
        Validate(request);
        ArticleReviewRuntimeConfiguration configuration = GetConfiguration();
        string serializedBody = CreateSerializedBody(request);
        int bodyBytes = Encoding.UTF8.GetByteCount(serializedBody);
        AiOptions value = options.Value;
        int maxOutput = request.Stage == ArticleReviewStageKinds.Generation
            ? value.ArticleMaxOutputTokens : value.ArticleVerifierMaxOutputTokens;
        if (bodyBytes > value.ArticleContextTokens - maxOutput - 512)
            throw new AnalysisInputTooLargeException();

        decimal maximumCharge = 0;
        if (value.ArticleProvider == "Gemini")
        {
            string model = request.Stage == ArticleReviewStageKinds.Generation
                ? value.ArticleModel
                : string.IsNullOrWhiteSpace(value.ArticleVerifierModel) ? value.ArticleModel : value.ArticleVerifierModel;
            if (!GeminiUsagePricing.TryGetRates(model, DateTime.UtcNow, out decimal inputRate,
                    out _, out decimal outputRate, out _))
                throw new AnalysisUnavailableException("Article review pricing is unavailable for the configured model.");
            decimal raw = ((bodyBytes + 512) * inputRate + maxOutput * outputRate) / 1_000_000m;
            maximumCharge = Math.Ceiling(raw * 1_000_000_000m) / 1_000_000_000m;
        }
        return new(configuration.SettingsFingerprint,
            Hash(JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions)), maximumCharge);
    }

    public async Task<ArticleReviewGenerationStageResult> GenerateAsync(
        ArticleReviewStageDispatchRequest request, CancellationToken cancellationToken)
    {
        ArticleReviewStageQuote quote = ValidateDispatch(request, ArticleReviewStageKinds.Generation);
        using IDisposable dispatch = dispatchContext.Enter(request.AttemptId);
        GeneratedArticleReviewPass pass;
        if (request.GenerationThinkingLevel is null)
        {
            pass = await generator.GenerateAsync(request.Role, request.Source.Language,
                request.Source.SourceKind, request.Source.SourceSpans!, cancellationToken);
        }
        else if (generator is GeminiArticleReviewGenerator geminiGenerator)
        {
            pass = await geminiGenerator.GenerateAsync(request.Role, request.Source.Language,
                request.Source.SourceKind, request.Source.SourceSpans!, request.GenerationThinkingLevel,
                cancellationToken);
        }
        else
        {
            throw new AnalysisUnavailableException(
                "Article review generation recovery requires the Gemini adapter.");
        }
        ArticleReviewer.ValidatePass(request.Role, pass, request.Source.SourceSpans!);
        return new(pass.Role, pass.Findings.Select(ToContract).ToList(), pass.Model, pass.PromptVersion,
            dispatchContext.Snapshot());
    }

    public async Task<ArticleReviewVerificationStageResult> VerifyAsync(
        ArticleReviewStageDispatchRequest request, CancellationToken cancellationToken)
    {
        ArticleReviewStageQuote quote = ValidateDispatch(request, ArticleReviewStageKinds.Verification);
        List<GeneratedArticleReviewFinding> findings = request.Findings!.Select(ToGenerated).ToList();
        using IDisposable dispatch = dispatchContext.Enter(request.AttemptId);
        GeneratedArticleReviewVerification result = await verifier.VerifyAsync(request.Role,
            request.Source.Language, findings, request.Source.SourceSpans!, cancellationToken);
        ArticleReviewer.ValidateVerdicts(findings, result);
        return new(result.Verdicts.Select(value => new ArticleReviewVerificationVerdict(
                value.FindingId, value.Verdict, value.Reason)).ToList(),
            result.Model, result.PromptVersion, dispatchContext.Snapshot());
    }

    public ArticleReviewProviderAttempt? LastAttempt() => dispatchContext.LastAttempt;

    private ArticleReviewStageQuote ValidateDispatch(ArticleReviewStageDispatchRequest request, string stage)
    {
        ArticleReviewStageQuoteRequest quoteRequest = new(stage, request.Role, request.Source)
        {
            Findings = request.Findings,
            GenerationThinkingLevel = request.GenerationThinkingLevel,
            RecoveryOfAttemptId = request.RecoveryOfAttemptId
        };
        ArticleReviewStageQuote quote = Quote(quoteRequest);
        if (request.AttemptId == Guid.Empty || request.SettingsFingerprint != quote.SettingsFingerprint ||
            request.RequestFingerprint != quote.RequestFingerprint ||
            request.MaximumChargeUsd != quote.MaximumChargeUsd)
            throw new BadHttpRequestException("The article review stage quote is stale or invalid.");
        return quote;
    }

    private void Validate(ArticleReviewStageQuoteRequest request)
    {
        if (request is null || request.Stage is not (ArticleReviewStageKinds.Generation or
                ArticleReviewStageKinds.Verification) || !ArticleReviewer.Roles.Contains(request.Role))
            throw new BadHttpRequestException("The article review stage request is invalid.");
        reviewer.ValidateRequest(request.Source);
        if (request.Stage == ArticleReviewStageKinds.Generation)
        {
            bool initial = request.GenerationThinkingLevel is null && request.RecoveryOfAttemptId is null;
            bool recovery = request.GenerationThinkingLevel == ArticleReviewGenerationRecovery.RecoveryThinkingLevel &&
                request.RecoveryOfAttemptId.HasValue && request.RecoveryOfAttemptId.Value != Guid.Empty &&
                options.Value.ArticleGenerationThinkingLevel == ArticleReviewGenerationRecovery.InitialThinkingLevel;
            if (request.Findings is not null || !(initial || recovery))
                throw new BadHttpRequestException("Generation does not accept findings.");
            return;
        }
        if (request.GenerationThinkingLevel is not null || request.RecoveryOfAttemptId is not null ||
            request.Findings is null || request.Findings.Count is < 1 or > 3)
            throw new BadHttpRequestException("Verification requires one to three findings.");
        List<GeneratedArticleReviewFinding> findings = request.Findings.Select(ToGenerated).ToList();
        ArticleReviewer.ValidatePass(request.Role,
            new(request.Role, findings, GetConfiguration().VerificationModel, ArticleReviewPrompt.Version),
            request.Source.SourceSpans!);
    }

    private string CreateSerializedBody(ArticleReviewStageQuoteRequest request)
    {
        AiOptions value = options.Value;
        if (request.Stage == ArticleReviewStageKinds.Generation)
        {
            string input = GeminiArticleReviewGenerator.CreateInput(request.Role, request.Source.Language,
                request.Source.SourceKind, request.Source.SourceSpans!);
            return GeminiArticleClient.CreateSerializedBody(ArticleReviewPrompt.InstructionsForRole(request.Role),
                input, ArticleReviewPrompt.CreateSchema(request.Role), value.ArticleMaxOutputTokens,
                request.GenerationThinkingLevel ?? value.ArticleGenerationThinkingLevel);
        }
        List<GeneratedArticleReviewFinding> findings = request.Findings!.Select(ToGenerated).ToList();
        string verificationInput = GeminiArticleReviewVerifier.CreateInput(request.Role, request.Source.Language,
            findings, request.Source.SourceSpans!);
        return GeminiArticleClient.CreateSerializedBody(ArticleReviewVerificationPrompt.Instructions,
            verificationInput,
            ArticleReviewVerificationPrompt.CreateSchema(findings.Select(value => value.FindingId)),
            value.ArticleVerifierMaxOutputTokens, value.ArticleVerifierThinkingLevel);
    }

    private static ArticleReviewCandidateFinding ToContract(GeneratedArticleReviewFinding value) =>
        new(value.FindingId, value.Role, value.Kind, value.Basis, value.Suggestion, value.SourceIds);

    private static GeneratedArticleReviewFinding ToGenerated(ArticleReviewCandidateFinding value) =>
        new(value.FindingId, value.Role, value.Kind, value.Basis, value.Suggestion, value.SourceIds);

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
