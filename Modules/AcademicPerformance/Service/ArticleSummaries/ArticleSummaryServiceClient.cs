using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticleSummaryServiceClient(HttpClient client, IOptions<AnalysisServiceOptions> options)
{
    public async Task<ArticleSummaryReport> SummarizeAsync(SummarizeArticleRequest snapshot, CancellationToken cancellationToken)
    {
        if (!ArticleSourceCatalog.IsValid(snapshot.Pages, snapshot.SourceSpans, snapshot.SourceKind))
            throw new JsonException("The article source catalog is incomplete or invalid.");
        using HttpRequestMessage request = new(HttpMethod.Post, "api/v1/articles/summarize") { Content = JsonContent.Create(snapshot) };
        if (!string.IsNullOrWhiteSpace(options.Value.ApiKey)) request.Headers.Add("X-Analysis-Key", options.Value.ApiKey);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("The analysis service could not summarize the article.", null, response.StatusCode);
        ArticleSummaryReport report = await response.Content.ReadFromJsonAsync<ArticleSummaryReport>(cancellationToken)
            ?? throw new JsonException("The analysis service returned an empty article report.");
        if (report.SourceHash != snapshot.SourceHash || report.SourceKind != snapshot.SourceKind || report.ExtractionVersion != snapshot.ExtractionVersion ||
            report.Language != snapshot.Language || report.Coverage is null || report.Sections is null ||
            report.Coverage.TotalChunks <= 0 || report.Coverage.ProcessedChunks != report.Coverage.TotalChunks ||
            report.Coverage.ProcessedPages != snapshot.Pages.Count || report.Coverage.TextBearingPages != snapshot.Pages.Count ||
            report.Coverage.TotalPages != snapshot.TotalSourcePages || report.Coverage.SelectedClaimsOmitted < 0 ||
            report.Coverage.IsPartial != (snapshot.IsPartial || report.Coverage.SelectedClaimsOmitted > 0 ||
                report.Verification?.Status == "insufficient_evidence") ||
            snapshot.IsPartial && string.IsNullOrWhiteSpace(report.Coverage.ScopeReason) ||
            !ValidVerification(report) || !ValidSections(report.Sections, snapshot) ||
            string.IsNullOrWhiteSpace(report.Model) || string.IsNullOrWhiteSpace(report.PromptVersion))
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
            !string.IsNullOrWhiteSpace(claim.Text) && claim.Text.Length <= 1200 && claim.Evidence is not null && claim.Evidence.Count is > 0 and <= 2 &&
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
            { Status: "automatically_checked" or "insufficient_evidence", Model.Length: > 0, PromptVersion.Length: > 0 } &&
            coverage.CandidateClaims is >= 0 && coverage.AutomaticallyCheckedClaims == coverage.CandidateClaims &&
            coverage.SupportedClaims is >= 0 && coverage.UnsupportedClaims is >= 0 && coverage.UncertainClaims is >= 0 &&
            coverage.CandidateClaims == coverage.SupportedClaims + coverage.UnsupportedClaims + coverage.UncertainClaims &&
            coverage.DuplicateOrCappedClaims is >= 0 &&
            coverage.BudgetUnverifiedClaims is >= 0 && coverage.BudgetUnverifiedClaims <= coverage.UncertainClaims &&
            coverage.SelectedClaimsOmitted == coverage.UnsupportedClaims + coverage.UncertainClaims + coverage.DuplicateOrCappedClaims &&
            claims == coverage.SupportedClaims - coverage.DuplicateOrCappedClaims &&
            coverage.OmissionReasons is not null &&
            (report.Verification.Status == "automatically_checked" && claims > 0 && coverage.SupportedClaims > 0 ||
             report.Verification.Status == "insufficient_evidence" && claims == 0 && coverage.SupportedClaims == 0);
    }
}
