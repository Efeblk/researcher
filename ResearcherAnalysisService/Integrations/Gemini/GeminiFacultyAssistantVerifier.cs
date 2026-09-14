using AcademicCollector.Analysis.Contracts;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed class GeminiFacultyAssistantVerifier(GeminiArticleClient client, IOptions<AiOptions> options)
    : IFacultyAssistantVerifier
{
    public async Task<GeneratedFacultyAssistantSourceCheck> VerifyAsync(
        string role, string language, GeneratedArticleReviewFinding finding,
        IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
    {
        if (finding.SourceIds.Count is < 1 or > 2 || sourceSpans.Count != finding.SourceIds.Count ||
            !sourceSpans.Select(value => value.SourceId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(finding.SourceIds))
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        GeneratedArticleReviewFinding[] findings = [finding];
        string input = GeminiArticleReviewVerifier.CreateInput(role, language, findings, sourceSpans);
        string model = string.IsNullOrWhiteSpace(options.Value.ArticleVerifierModel)
            ? options.Value.ArticleModel : options.Value.ArticleVerifierModel;
        JsonObject schema = FacultyAssistantVerificationPrompt.CreateSchema([finding.FindingId]);
        GeminiArticleInvocationResult invocation = await client.GenerateAttemptAsync(model,
            FacultyAssistantVerificationPrompt.Instructions, input, schema,
            options.Value.ArticleVerifierMaxOutputTokens,
            options.Value.FacultyAssistantVerifierThinkingLevel, cancellationToken);
        if (invocation.Result is null)
        {
            if (IsDurablyAttested(invocation, model) && invocation.Failure == AnalysisFailure.OutputLimit)
                return new(invocation.AttemptId, "unverified_output_limit", null, invocation.Completion.ReturnedModel!,
                    FacultyAssistantVerificationPrompt.Version,
                    "The singleton source check reached its output limit and was omitted without retry.");
            if (IsDurablyAttested(invocation, model))
                return InvalidResponse(invocation);
            throw new InvalidAnalysisException(invocation.Failure ?? AnalysisFailure.InvalidReport);
        }
        if (!IsDurablyAttested(invocation, model))
            throw new AnalysisUnavailableException("Gemini usage tracking is unavailable.");
        try
        {
            GeneratedArticleReviewVerification result = GeminiArticleReviewVerifier.ParseResult(
                invocation.Result, FacultyAssistantVerificationPrompt.Version);
            GeneratedArticleReviewVerdict? verdict = result.Verdicts is { Count: 1 }
                ? result.Verdicts[0] : null;
            if (verdict is null || verdict.FindingId != finding.FindingId ||
                verdict.Verdict is not ("supported" or "unsupported" or "uncertain") ||
                string.IsNullOrWhiteSpace(verdict.Reason) || verdict.Reason.Length > 500)
                return InvalidResponse(invocation);
            return new(invocation.AttemptId, "checked", verdict, result.Model, result.PromptVersion, verdict.Reason);
        }
        catch (InvalidAnalysisException) when (IsDurablyAttested(invocation, model))
        {
            return InvalidResponse(invocation);
        }
    }

    private static bool IsDurablyAttested(GeminiArticleInvocationResult invocation, string requestedModel) =>
        invocation.UsagePersisted && invocation.Completion.UsageValidForAttribution &&
        invocation.Completion.EstimatedUsd.HasValue &&
        !string.IsNullOrWhiteSpace(invocation.Completion.PricingVersion) &&
        string.Equals(invocation.Completion.ReturnedModel, requestedModel, StringComparison.Ordinal);

    private static GeneratedFacultyAssistantSourceCheck InvalidResponse(
        GeminiArticleInvocationResult invocation) => new(invocation.AttemptId, "unverified_invalid_response", null,
        invocation.Completion.ReturnedModel!, FacultyAssistantVerificationPrompt.Version,
        "The singleton source check returned a fully attributed but unusable response and was omitted without retry.");
}
