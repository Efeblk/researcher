namespace AcademicCollector.Analysis.Contracts;

using System.Security.Cryptography;
using System.Text;

public sealed record ArticlePage(int? PageNumber, string Text);
public sealed record ArticleSourceSpan(string SourceId, int? PageNumber, int StartOffset, int EndOffset, string Text);

public sealed record SummarizeArticleRequest(string Language, string SourceKind, string SourceHash, string ExtractionVersion,
    IReadOnlyList<ArticlePage> Pages, int TotalSourcePages, bool IsPartial, string? ScopeReason)
{
    public IReadOnlyList<ArticleSourceSpan>? SourceSpans { get; init; }
}

public sealed record ArticleEvidence(string Quote, int? PageNumber)
{
    public string? SourceId { get; init; }
    public int? StartOffset { get; init; }
    public int? EndOffset { get; init; }
}

public sealed record ArticleClaim(string Text, IReadOnlyList<ArticleEvidence> Evidence)
{
    public string? ClaimId { get; init; }
}

public sealed record ArticleSummarySections(IReadOnlyList<ArticleClaim> Purpose, IReadOnlyList<ArticleClaim> Methods,
    IReadOnlyList<ArticleClaim> Data, IReadOnlyList<ArticleClaim> Findings, IReadOnlyList<ArticleClaim> Limitations);

public sealed record ArticleCoverage(int ProcessedChunks, int TotalChunks, int ProcessedPages, int TextBearingPages,
    int TotalPages, int SelectedClaimsOmitted, bool IsPartial, string? ScopeReason)
{
    public int? CandidateClaims { get; init; }
    public int? AutomaticallyCheckedClaims { get; init; }
    public int? SupportedClaims { get; init; }
    public int? UnsupportedClaims { get; init; }
    public int? UncertainClaims { get; init; }
    public int? DuplicateOrCappedClaims { get; init; }
    public int? BudgetUnverifiedClaims { get; init; }
    public IReadOnlyList<string>? OmissionReasons { get; init; }
}

public sealed record ArticleVerificationMetadata(string Status, string Model, string PromptVersion,
    bool UsesSameModelFamily, string? Limitation);

public sealed record ArticleSummaryReport(string Language, string SourceKind, string SourceHash, string ExtractionVersion,
    ArticleCoverage Coverage, ArticleSummarySections Sections, string Model, string PromptVersion)
{
    public ArticleVerificationMetadata? Verification { get; init; }
    public string? ExtractionMethod { get; init; }
}

public static class ArticleSourceCatalog
{
    public static IReadOnlyList<ArticleSourceSpan> Create(IReadOnlyList<ArticlePage> pages, int maximumSpanCharacters = 550)
    {
        List<ArticleSourceSpan> result = [];
        for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            ArticlePage page = pages[pageIndex];
            for (int offset = 0; offset < page.Text.Length;)
            {
                int end = FindBoundary(page.Text, offset, maximumSpanCharacters);
                string text = page.Text[offset..end];
                result.Add(new(CreateId(pageIndex, page.PageNumber, offset, end, text), page.PageNumber, offset, end, text));
                offset = end;
            }
        }
        return result;
    }

    public static bool IsValid(IReadOnlyList<ArticlePage> pages, IReadOnlyList<ArticleSourceSpan>? spans, string sourceKind)
    {
        if (sourceKind is not ("pdf" or "html" or "abstract")) return false;
        if (spans is null || spans.Count == 0 || spans.Any(x => x is null) ||
            spans.Select(x => x.SourceId).Distinct(StringComparer.Ordinal).Count() != spans.Count)
            return false;
        if (sourceKind == "pdf" && (pages.Any(x => x.PageNumber is null or <= 0) ||
            pages.Select(x => x.PageNumber).Distinct().Count() != pages.Count)) return false;
        if (sourceKind is "abstract" or "html" && (pages.Count != 1 || pages.Any(x => x.PageNumber is not null))) return false;
        foreach (ArticleSourceSpan span in spans)
        {
            int pageIndex = FindPageIndex(pages, span);
            if (pageIndex < 0 || span.StartOffset < 0 || span.EndOffset <= span.StartOffset ||
                span.EndOffset > pages[pageIndex].Text.Length || pages[pageIndex].Text[span.StartOffset..span.EndOffset] != span.Text ||
                span.SourceId != CreateId(pageIndex, span.PageNumber, span.StartOffset, span.EndOffset, span.Text)) return false;
        }
        return spans.SequenceEqual(Create(pages));
    }

    private static int FindPageIndex(IReadOnlyList<ArticlePage> pages, ArticleSourceSpan span)
    {
        for (int i = 0; i < pages.Count; i++) if (pages[i].PageNumber == span.PageNumber) return i;
        return -1;
    }

    private static int FindBoundary(string text, int start, int maximum)
    {
        int limit = Math.Min(text.Length, start + maximum);
        if (limit == text.Length) return limit;
        int boundary = text.LastIndexOfAny(['\n', '.', '!', '?', ';'], limit - 1, limit - start);
        if (boundary < start + maximum / 3) boundary = text.LastIndexOf(' ', limit - 1, limit - start);
        int end = boundary >= start + maximum / 3 ? boundary + 1 : limit;
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return Math.Max(start + 1, end);
    }

    private static string CreateId(int pageIndex, int? pageNumber, int start, int end, string text)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{pageIndex}:{pageNumber?.ToString() ?? "abstract"}:{start}:{end}:{text}"));
        return $"src-{pageIndex + 1}-{start}-{Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant()}";
    }
}

public static class ArticleEvidenceMatcher
{
    public static bool IsMatch(IReadOnlyList<ArticlePage> pages, ArticleEvidence evidence, string sourceKind,
        IReadOnlyList<ArticleSourceSpan>? spans = null)
    {
        if (evidence is null || evidence.Quote is null || sourceKind is "abstract" or "html" && evidence.PageNumber is not null) return false;
        if (evidence.SourceId is not null)
        {
            ArticleSourceSpan? span = spans?.SingleOrDefault(x => x.SourceId == evidence.SourceId);
            return span is not null && span.PageNumber == evidence.PageNumber && span.Text == evidence.Quote &&
                span.StartOffset == evidence.StartOffset && span.EndOffset == evidence.EndOffset;
        }
        string quote = NormalizeWhitespace(evidence.Quote);
        return quote.Length > 0 && pages.Any(page => page.PageNumber == evidence.PageNumber &&
            NormalizeWhitespace(page.Text).Contains(quote, StringComparison.Ordinal));
    }

    public static string NormalizeWhitespace(string value)
    {
        StringBuilder result = new(value.Length); bool pendingSpace = false;
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune)) { pendingSpace = result.Length > 0; continue; }
            if (pendingSpace) result.Append(' ');
            result.Append(rune); pendingSpace = false;
        }
        return result.ToString();
    }
}
