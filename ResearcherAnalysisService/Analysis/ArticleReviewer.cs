using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Configuration;
using System.Text;

namespace ResearcherAnalysisService.Analysis;

public sealed class ArticleReviewer(
    IArticleReviewGenerator generator,
    IArticleReviewVerifier verifier,
    IOptions<AiOptions> options)
{
    public const string DefaultPolicyVersion = "article-specialist-review-policy-v5";
    public static readonly IReadOnlyList<string> Roles =
        ["method", "quantitative", "claim_evidence", "teaching"];

    public async Task<ArticleReviewReport> ReviewAsync(
        ReviewArticleRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        using CancellationTokenSource total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(TimeSpan.FromSeconds(options.Value.ArticleReviewTimeoutSeconds));
        IReadOnlyList<ArticleSourceSpan> catalog = request.SourceSpans!;
        List<ArticleSpecialistReview> reviews = [];
        List<string> generationModels = [];
        List<string> verifierModels = [];
        List<string> omissionReasons = [];
        int candidates = 0;
        int unsupported = 0;
        int uncertain = 0;

        foreach (string role in Roles)
        {
            GeneratedArticleReviewPass pass;
            try
            {
                pass = await generator.GenerateAsync(
                    role, request.Language, request.SourceKind, catalog, total.Token)
                    ?? throw new InvalidAnalysisException();
                ValidatePass(role, pass, catalog);
            }
            catch (InvalidAnalysisException exception)
            {
                exception.AddContext("generation", role);
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                total.IsCancellationRequested)
            {
                throw new ArticleReviewTimedOutException("generation", role);
            }
            generationModels.Add(pass.Model);
            candidates += pass.Findings.Count;

            IReadOnlyList<GeneratedArticleReviewVerdict> verdicts;
            if (pass.Findings.Count == 0)
            {
                verdicts = [];
            }
            else
            {
                GeneratedArticleReviewVerification verification;
                try
                {
                    verification = await verifier.VerifyAsync(
                        role, request.Language, pass.Findings, catalog, total.Token)
                        ?? throw new InvalidAnalysisException();
                    ValidateVerdicts(pass.Findings, verification);
                }
                catch (InvalidAnalysisException exception)
                {
                    exception.AddContext("verification", role);
                    throw;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                    total.IsCancellationRequested)
                {
                    throw new ArticleReviewTimedOutException("verification", role);
                }
                verifierModels.Add(verification.Model);
                verdicts = verification.Verdicts;
            }

            List<ArticleReviewFinding> supported = [];
            foreach (GeneratedArticleReviewFinding finding in pass.Findings)
            {
                GeneratedArticleReviewVerdict verdict = verdicts.Single(value => value.FindingId == finding.FindingId);
                if (verdict.Verdict != "supported")
                {
                    if (verdict.Verdict == "unsupported") unsupported++; else uncertain++;
                    if (omissionReasons.Count < 20)
                        omissionReasons.Add($"{role}/{finding.FindingId} {verdict.Verdict}: {verdict.Reason}");
                    continue;
                }
                supported.Add(Resolve(role, finding, catalog));
            }
            reviews.Add(new(role, supported.Count == 0 ? "no_supported_findings" : "automatically_checked", supported));
        }

        int supportedCount = reviews.Sum(review => review.Findings.Count);
        if (supportedCount + unsupported + uncertain != candidates)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        List<string> distinctGenerationModels = generationModels.Distinct(StringComparer.Ordinal).ToList();
        string configuredVerifier = string.IsNullOrWhiteSpace(options.Value.ArticleVerifierModel)
            ? options.Value.ArticleModel : options.Value.ArticleVerifierModel;
        List<string> distinctVerifierModels = verifierModels.Count == 0
            ? [configuredVerifier]
            : verifierModels.Distinct(StringComparer.Ordinal).ToList();
        if (distinctGenerationModels.Count != 1 || distinctVerifierModels.Count != 1)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        bool sameFamily = options.Value.ArticleProvider == "Gemini" ||
            string.Equals(distinctGenerationModels[0], distinctVerifierModels[0], StringComparison.OrdinalIgnoreCase);
        string outcome = supportedCount == 0 ? "no_supported_findings" : "automatically_checked";

        return new(
            request.Language,
            request.SourceKind,
            request.SourceHash,
            request.ExtractionVersion,
            request.PolicyVersion,
            outcome,
            new(request.Pages.Count, request.Pages.Count(page => !string.IsNullOrWhiteSpace(page.Text)),
                request.TotalSourcePages, request.IsPartial, request.ScopeReason),
            new(Roles.Count, Roles.Count, candidates, candidates, supportedCount, unsupported, uncertain,
                unsupported + uncertain, omissionReasons),
            reviews,
            string.Join(",", distinctGenerationModels),
            ArticleReviewPrompt.Version,
            new(outcome, string.Join(",", distinctVerifierModels), ArticleReviewVerificationPrompt.Version,
                sameFamily,
                "Automatic checking does not prove that the article is flawless or that every possible issue was found."))
        {
            SourceFidelity = ArticleSourceFidelity.Create(request.SourceKind, request.ExtractionVersion)
        };
    }

    public static bool IsKindAllowed(string role, string kind) => role switch
    {
        "method" or "quantitative" or "claim_evidence" => kind is "source_observation" or "review_question",
        "teaching" => kind is "teaching_adaptation" or "review_question",
        _ => false
    };

    public void ValidateRequest(ReviewArticleRequest request)
    {
        if (request.Pages is null || request.SourceSpans is null || request.Pages.Count is 0 or > 500 ||
            request.SourceSpans.Count is 0 or > 50000 || request.Pages.Any(page => page is null || page.Text is null) ||
            request.SourceSpans.Any(span => span is null || span.Text is null || span.SourceId is null))
            throw new BadHttpRequestException("The article review source shape is invalid.");
        if (request.Language is not ("tr" or "en") || string.IsNullOrWhiteSpace(request.SourceHash) ||
            request.SourceHash.Length > 64 || string.IsNullOrWhiteSpace(request.ExtractionVersion) ||
            request.ExtractionVersion.Length > 100 || !HasBoundedText(request.PolicyVersion, 100) ||
            request.TotalSourcePages < request.Pages.Count ||
            request.TotalSourcePages <= 0 || request.ScopeReason?.Length > 4000 ||
            request.TotalSourcePages > request.Pages.Count && !request.IsPartial ||
            request.SourceKind == "abstract" && !request.IsPartial ||
            request.IsPartial && string.IsNullOrWhiteSpace(request.ScopeReason))
            throw new BadHttpRequestException("The article review request is invalid.");
        int sourceBytes = request.SourceSpans.Sum(span =>
            Encoding.UTF8.GetByteCount(span.SourceId) + Encoding.UTF8.GetByteCount(span.Text) + 80);
        int contextBudget = options.Value.ArticleContextTokens -
            Math.Max(options.Value.ArticleMaxOutputTokens, options.Value.ArticleVerifierMaxOutputTokens) - 512;
        if (sourceBytes > Math.Min(options.Value.ArticleReviewMaximumInputBytes, contextBudget))
            throw new AnalysisInputTooLargeException();
        if (!ArticleSourceCatalog.IsValid(request.Pages, request.SourceSpans, request.SourceKind))
            throw new BadHttpRequestException("The article source catalog does not match its canonical pages.");
    }

    public static void ValidatePass(
        string expectedRole,
        GeneratedArticleReviewPass pass,
        IReadOnlyList<ArticleSourceSpan> catalog)
    {
        if (pass.Role != expectedRole || pass.Findings is null || pass.Findings.Count > 3 ||
            !HasBoundedText(pass.Model, 200) || pass.PromptVersion != ArticleReviewPrompt.Version ||
            pass.Findings.Any(finding => finding is null || finding.Role != expectedRole ||
                !IsKindAllowed(expectedRole, finding.Kind) || !HasBoundedText(finding.FindingId, 100) ||
                finding.FindingId != finding.FindingId.Trim() ||
                !HasBoundedText(finding.Basis, 1200) || finding.SourceIds is null ||
                finding.SourceIds.Count is < 1 or > 2 ||
                finding.SourceIds.Distinct(StringComparer.Ordinal).Count() != finding.SourceIds.Count ||
                finding.SourceIds.Any(id => !catalog.Any(span => span.SourceId == id && !string.IsNullOrWhiteSpace(span.Text))) ||
                finding.Kind == "source_observation" && finding.Suggestion is not null ||
                finding.Kind != "source_observation" && !HasBoundedText(finding.Suggestion, 1200)) ||
            pass.Findings.Select(finding => finding.FindingId).Distinct(StringComparer.Ordinal).Count() != pass.Findings.Count)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }

    public static void ValidateVerdicts(
        IReadOnlyList<GeneratedArticleReviewFinding> findings,
        GeneratedArticleReviewVerification verification)
    {
        IReadOnlyList<GeneratedArticleReviewVerdict>? verdicts = verification.Verdicts;
        if (verdicts is null || verdicts.Count != findings.Count ||
            !HasBoundedText(verification.Model, 200) ||
            verification.PromptVersion != ArticleReviewVerificationPrompt.Version ||
            verdicts.Any(verdict => verdict is null ||
                !findings.Any(finding => finding.FindingId == verdict.FindingId) ||
                verdict.Verdict is not ("supported" or "unsupported" or "uncertain") ||
                !HasBoundedText(verdict.Reason, 500)) ||
            verdicts.Select(verdict => verdict.FindingId).Distinct(StringComparer.Ordinal).Count() != verdicts.Count)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }

    private static ArticleReviewFinding Resolve(
        string role,
        GeneratedArticleReviewFinding finding,
        IReadOnlyList<ArticleSourceSpan> catalog)
    {
        List<ArticleReviewEvidence> evidence = finding.SourceIds.Select(sourceId =>
        {
            ArticleSourceSpan span = catalog.Single(value => value.SourceId == sourceId);
            return new ArticleReviewEvidence(span.SourceId, span.PageNumber,
                span.StartOffset, span.EndOffset, span.Text);
        }).ToList();
        return new($"{role}:{finding.FindingId}", role, finding.Kind,
            finding.Basis, finding.Suggestion, evidence);
    }

    private static bool HasBoundedText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;
}

public sealed class ArticleReviewTimedOutException(string stage, string role)
    : OperationCanceledException("Article review timed out.")
{
    public string Stage { get; } = stage;
    public string Role { get; } = role;
}
