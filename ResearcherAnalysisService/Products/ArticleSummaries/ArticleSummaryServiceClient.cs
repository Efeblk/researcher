using System.Text.Json;
using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public sealed class ArticleSummaryServiceClient(ResearcherAnalysisService.Analysis.ArticleSummarizer summarizer)
{
    public async Task<ArticleSummaryReport> SummarizeAsync(SummarizeArticleRequest snapshot, CancellationToken cancellationToken)
    {
        if (!ArticleSourceCatalog.IsValid(snapshot.Pages, snapshot.SourceSpans, snapshot.SourceKind))
            throw new JsonException("The article source catalog is incomplete or invalid.");
        ArticleSummaryReport report = await summarizer.SummarizeAsync(snapshot, cancellationToken);
        if (report.SourceHash != snapshot.SourceHash || report.SourceKind != snapshot.SourceKind || report.ExtractionVersion != snapshot.ExtractionVersion ||
            report.Language != snapshot.Language || report.Coverage is null || report.Sections is null ||
            report.Coverage.TotalChunks <= 0 || report.Coverage.ProcessedChunks != report.Coverage.TotalChunks ||
            report.Coverage.ProcessedPages != snapshot.Pages.Count || report.Coverage.TextBearingPages != snapshot.Pages.Count ||
            report.Coverage.TotalPages != snapshot.TotalSourcePages || report.Coverage.SelectedClaimsOmitted < 0 ||
            report.Coverage.IsPartial != (snapshot.IsPartial || report.Coverage.SelectedClaimsOmitted > 0 ||
                report.Verification?.Status == "insufficient_evidence") ||
            snapshot.IsPartial && string.IsNullOrWhiteSpace(report.Coverage.ScopeReason) ||
            report.SourceFidelity != ArticleSourceFidelity.Create(snapshot.SourceKind, snapshot.ExtractionVersion) ||
            !ValidVerification(report) || !ValidSections(report.Sections, snapshot) ||
            !HasBoundedText(report.Model, 200) || !HasBoundedText(report.PromptVersion, 100) ||
            !HasOptionalBoundedText(report.Coverage.ScopeReason, 4000) ||
            !HasOptionalBoundedText(report.Verification?.Limitation, 4000))
            throw new JsonException("The article report does not match the source snapshot.");
        return report with { ExtractionMethod = GetExtractionMethod(snapshot.SourceKind, snapshot.ExtractionVersion) };
    }

    internal static string GetExtractionMethod(string sourceKind, string extractionVersion) => sourceKind switch
    {
        "pdf" when extractionVersion.Contains("ocr", StringComparison.OrdinalIgnoreCase) => "pdf_ocr",
        "pdf" => "pdf_text",
        "html" => "html",
        _ => "abstract"
    };

    private static bool ValidSections(ArticleSummarySections sections, SummarizeArticleRequest snapshot)
    {
        IReadOnlyList<ArticleClaim>?[] groups = [sections.Purpose, sections.Methods, sections.Data, sections.Findings, sections.Limitations];
        return groups.All(group => group is not null && group.Count <= 12 && group.All(claim => claim is not null &&
            !string.IsNullOrWhiteSpace(claim.Text) && claim.Text.Length <= 1200 &&
            HasOptionalBoundedText(claim.ClaimId, 200) && claim.Evidence is not null && claim.Evidence.Count is > 0 and <= 2 &&
            claim.Evidence.All(evidence => evidence is not null && !string.IsNullOrWhiteSpace(evidence.Quote) &&
                evidence.Quote.Length <= 550 && evidence.SourceId is not null &&
                evidence.StartOffset is >= 0 && evidence.EndOffset > evidence.StartOffset &&
                ArticleEvidenceMatcher.IsMatch(snapshot.Pages, evidence, snapshot.SourceKind, snapshot.SourceSpans))));
    }

    private static bool ValidVerification(ArticleSummaryReport report)
    {
        ArticleCoverage coverage = report.Coverage;
        if (report.Sections is null || new[] { report.Sections.Purpose, report.Sections.Methods, report.Sections.Data,
            report.Sections.Findings, report.Sections.Limitations }.Any(x => x is null)) return false;
        int claims = new[] { report.Sections.Purpose, report.Sections.Methods, report.Sections.Data,
            report.Sections.Findings, report.Sections.Limitations }.Sum(x => x.Count);
        return report.Verification is
            { Status: "automatically_checked" or "insufficient_evidence" } verification &&
            HasBoundedText(verification.Model, 200) && HasBoundedText(verification.PromptVersion, 100) &&
            coverage.CandidateClaims is >= 0 && coverage.BudgetUnverifiedClaims is >= 0 &&
            coverage.AutomaticallyCheckedClaims == coverage.CandidateClaims - coverage.BudgetUnverifiedClaims &&
            coverage.SupportedClaims is >= 0 && coverage.UnsupportedClaims is >= 0 && coverage.UncertainClaims is >= 0 &&
            coverage.CandidateClaims == coverage.SupportedClaims + coverage.UnsupportedClaims + coverage.UncertainClaims &&
            coverage.DuplicateOrCappedClaims is >= 0 &&
            coverage.BudgetUnverifiedClaims <= coverage.UncertainClaims &&
            coverage.SelectedClaimsOmitted == coverage.UnsupportedClaims + coverage.UncertainClaims + coverage.DuplicateOrCappedClaims &&
            claims == coverage.SupportedClaims - coverage.DuplicateOrCappedClaims &&
            coverage.OmissionReasons is not null &&
            (report.Verification.Status == "automatically_checked" && claims > 0 && coverage.SupportedClaims > 0 ||
             report.Verification.Status == "insufficient_evidence" && claims == 0 && coverage.SupportedClaims == 0);
    }

    private static bool HasBoundedText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    private static bool HasOptionalBoundedText(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength;
}
