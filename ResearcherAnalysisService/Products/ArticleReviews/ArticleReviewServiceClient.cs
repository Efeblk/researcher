using System.Net;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Analysis;

namespace ResearcherAnalysisService.Products.ArticleReviews;

public sealed class ArticleReviewServiceClient(ResearcherAnalysisService.Analysis.ArticleReviewer reviewer,
    ResearcherAnalysisService.Analysis.ArticleReviewStageExecutor executor)
{
    private const string StageFailureMessage =
        "Article review stage failed; no partial report was saved.";
    private static readonly string[] Roles = ["method", "quantitative", "claim_evidence", "teaching"];

    public async Task<ArticleReviewReport> ReviewAsync(
        ReviewArticleRequest snapshot,
        CancellationToken cancellationToken)
    {
        ArticleReviewReport report = await reviewer.ReviewAsync(snapshot, cancellationToken);
        Validate(report, snapshot);
        return report;
    }

    public Task<ArticleReviewRuntimeConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
        ExecuteControlAsync(() => executor.GetConfiguration());

    public Task<ArticleReviewStageQuote> QuoteAsync(ArticleReviewStageQuoteRequest request,
        CancellationToken cancellationToken) => ExecuteControlAsync(() => executor.Quote(request));

    public Task<ArticleReviewGenerationStageResult> GenerateAsync(ArticleReviewStageDispatchRequest request,
        CancellationToken cancellationToken) => ExecuteStageAsync(
            request, "generation", () => executor.GenerateAsync(request, cancellationToken), cancellationToken);

    public Task<ArticleReviewVerificationStageResult> VerifyAsync(ArticleReviewStageDispatchRequest request,
        CancellationToken cancellationToken) => ExecuteStageAsync(
            request, "verification", () => executor.VerifyAsync(request, cancellationToken), cancellationToken);

    private async Task<T> ExecuteStageAsync<T>(ArticleReviewStageDispatchRequest request, string stage,
        Func<Task<T>> execute, CancellationToken cancellationToken)
    {
        try
        {
            return await execute();
        }
        catch (AnalysisInputTooLargeException)
        {
            throw new ArticleReviewInputTooLargeException();
        }
        catch (AnalysisUnavailableException)
        {
            throw StageFailure(HttpStatusCode.ServiceUnavailable, "provider_unavailable",
                StageFailureMessage, request);
        }
        catch (InvalidAnalysisException exception)
        {
            string code = InvalidAnalysisException.CodeFor(exception.Reason);
            throw StageFailure(HttpStatusCode.BadGateway, code,
                StageFailureMessage, request,
                new(code, exception.Stage ?? stage, exception.Role ?? request.Role));
        }
        catch (HttpRequestException)
        {
            throw StageFailure(HttpStatusCode.BadGateway, "provider_failure",
                StageFailureMessage, request);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw StageFailure(HttpStatusCode.GatewayTimeout, "timeout",
                StageFailureMessage, request,
                new("timeout", stage, request.Role));
        }
    }

    private static Task<T> ExecuteControlAsync<T>(Func<T> execute)
    {
        try
        {
            return Task.FromResult(execute());
        }
        catch (AnalysisInputTooLargeException)
        {
            throw new ArticleReviewInputTooLargeException();
        }
        catch (AnalysisUnavailableException)
        {
            throw new ArticleReviewAnalysisException(HttpStatusCode.ServiceUnavailable,
                StageFailureMessage, "provider_unavailable", null);
        }
        catch (InvalidAnalysisException exception)
        {
            string code = InvalidAnalysisException.CodeFor(exception.Reason);
            throw new ArticleReviewAnalysisException(HttpStatusCode.BadGateway,
                StageFailureMessage, code, new(code, exception.Stage, exception.Role));
        }
    }

    private ArticleReviewAnalysisException StageFailure(HttpStatusCode statusCode, string errorCode,
        string message, ArticleReviewStageDispatchRequest request,
        AnalysisFailureDetail? failure = null)
    {
        ArticleReviewProviderAttempt? attempt = executor.LastAttempt();
        if (attempt is null || attempt.AttemptId != request.AttemptId ||
            !HasBoundedText(attempt.Outcome, 40) ||
            attempt.ReturnedModel is not null && !HasBoundedText(attempt.ReturnedModel, 200) ||
            attempt.EstimatedCostUsd is < 0 ||
            attempt.PricingVersion is not null && !HasBoundedText(attempt.PricingVersion, 100))
            attempt = null;
        return new(statusCode, message, errorCode, failure, attempt);
    }

    internal static void Validate(ArticleReviewReport report, ReviewArticleRequest snapshot)
    {
        if (report.Language != snapshot.Language || report.SourceKind != snapshot.SourceKind ||
            report.SourceHash != snapshot.SourceHash || report.ExtractionVersion != snapshot.ExtractionVersion ||
            report.PolicyVersion != snapshot.PolicyVersion || report.Reviews is null || report.Reviews.Count != Roles.Length ||
            report.SourceFidelity != ArticleSourceFidelity.Create(snapshot.SourceKind, snapshot.ExtractionVersion) ||
            report.Reviews.Any(review => review is null || review.Findings is null) ||
            !report.Reviews.Select(review => review.Role).SequenceEqual(Roles) ||
            report.SourceCoverage is null || report.Coverage is null || report.Verification is null ||
            report.SourceCoverage.ProcessedPages != snapshot.Pages.Count ||
            report.SourceCoverage.TextBearingPages != snapshot.Pages.Count(page => !string.IsNullOrWhiteSpace(page.Text)) ||
            report.SourceCoverage.TotalPages != snapshot.TotalSourcePages ||
            report.SourceCoverage.IsPartial != snapshot.IsPartial || report.SourceCoverage.ScopeReason != snapshot.ScopeReason ||
            report.Coverage.ProcessedRoles != Roles.Length || report.Coverage.TotalRoles != Roles.Length ||
            report.Coverage.CandidateFindings is < 0 or > 12 ||
            report.Coverage.AutomaticallyCheckedFindings != report.Coverage.CandidateFindings ||
            report.Coverage.AutomaticallyCheckedFindings is < 0 or > 12 ||
            report.Coverage.SupportedFindings is < 0 or > 12 || report.Coverage.UnsupportedFindings is < 0 or > 12 ||
            report.Coverage.UncertainFindings is < 0 or > 12 || report.Coverage.OmittedFindings is < 0 or > 12 ||
            report.Coverage.CandidateFindings != report.Coverage.SupportedFindings +
                report.Coverage.UnsupportedFindings + report.Coverage.UncertainFindings ||
            report.Coverage.OmittedFindings != report.Coverage.UnsupportedFindings + report.Coverage.UncertainFindings ||
            report.Coverage.OmissionReasons is null || report.Coverage.OmissionReasons.Count > 20 ||
            report.Coverage.OmissionReasons.Any(reason => !HasBoundedText(reason, 1000)) ||
            !HasBoundedText(report.Model, 200) || !HasBoundedText(report.PromptVersion, 100) ||
            !HasBoundedText(report.Verification.Status, 50) || !HasBoundedText(report.Verification.Model, 200) ||
            !HasBoundedText(report.Verification.PromptVersion, 100) ||
            report.Verification.Limitation is null || report.Verification.Limitation.Length > 1000)
            throw new JsonException("The article review does not match the source snapshot.");

        List<ArticleReviewFinding> findings = report.Reviews.SelectMany(review => review.Findings!).ToList();
        if (findings.Any(finding => finding is null))
            throw new JsonException("The article review contains a null finding.");
        if (findings.Count != report.Coverage.SupportedFindings ||
            findings.Select(finding => finding.FindingId).Distinct(StringComparer.Ordinal).Count() != findings.Count ||
            report.Outcome != (findings.Count == 0 ? "no_supported_findings" : "automatically_checked") ||
            report.Verification.Status != report.Outcome ||
            report.Reviews.Any(review => review.Findings is null || review.Findings.Count > 3 ||
                review.Status != (review.Findings.Count == 0 ? "no_supported_findings" : "automatically_checked") ||
                review.Findings.Any(finding => !ValidFinding(review.Role, finding, snapshot))))
            throw new JsonException("The article review contains invalid findings or coverage arithmetic.");
    }

    private static bool ValidFinding(string role, ArticleReviewFinding finding, ReviewArticleRequest snapshot) =>
        finding is not null && finding.Role == role &&
        IsKindAllowed(role, finding.Kind) &&
        HasBoundedText(finding.FindingId, 120) && finding.FindingId == finding.FindingId.Trim() &&
        HasBoundedText(finding.Basis, 1200) &&
        (finding.Kind == "source_observation" ? finding.Suggestion is null : HasBoundedText(finding.Suggestion, 1200)) &&
        finding.Evidence is not null && finding.Evidence.Count is > 0 and <= 2 &&
        !finding.Evidence.Any(evidence => evidence is null) &&
        finding.Evidence.Select(evidence => evidence.SourceId).Distinct(StringComparer.Ordinal).Count() == finding.Evidence.Count &&
        finding.Evidence.All(evidence => !string.IsNullOrWhiteSpace(evidence.SourceId) &&
            evidence.StartOffset >= 0 && evidence.EndOffset > evidence.StartOffset &&
            !string.IsNullOrWhiteSpace(evidence.Quote) && evidence.Quote.Length <= 550 &&
            ArticleEvidenceMatcher.IsMatch(snapshot.Pages,
                new ArticleEvidence(evidence.Quote, evidence.PageNumber)
                {
                    SourceId = evidence.SourceId,
                    StartOffset = evidence.StartOffset,
                    EndOffset = evidence.EndOffset
                }, snapshot.SourceKind, snapshot.SourceSpans));

    private static bool HasBoundedText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    private static bool ValidError(AnalysisErrorResponse error) =>
        HasBoundedText(error.Message, 1000) &&
        error.ErrorCode is "invalid_provider_response" or "provider_failure" or "timeout" &&
        (error.Failure is null || KnownFailureReason(error.Failure.Reason) &&
            error.Failure.Stage is null or "generation" or "verification" &&
            error.Failure.Role is null or "method" or "quantitative" or "claim_evidence" or "teaching");

    private static bool KnownFailureReason(string? reason) => reason is
        "invalid_report" or "output_limit" or "incomplete_output" or "invalid_json" or
        "invalid_observation" or "invalid_evidence" or "unknown_publication" or
        "writing_evidence_not_abstract" or "quote_mismatch" or "timeout";

    private static bool IsKindAllowed(string role, string kind) => role switch
    {
        "method" or "quantitative" or "claim_evidence" => kind is "source_observation" or "review_question",
        "teaching" => kind is "teaching_adaptation" or "review_question",
        _ => false
    };
}
