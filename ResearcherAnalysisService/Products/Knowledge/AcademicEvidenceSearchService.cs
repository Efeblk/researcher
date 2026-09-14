using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Products.Knowledge;

public interface IAcademicEvidenceSearchService
{
    Task<AcademicEvidenceSearchResponse> SearchAsync(
        string personelId,
        AcademicEvidenceSearchRequest request,
        CancellationToken cancellationToken);
}

public sealed partial class AcademicEvidenceSearchService(AnalysisDbContext database,
    IOptions<ArticleSummaryAutomationOptions>? automationOptions = null)
    : IAcademicEvidenceSearchService
{
    public const string CatalogVersion = "academic-evidence-search-v4";
    private const string VerifiedStatus = "automatically_checked";
    private const int MaximumCandidateSpans = 5000;
    private const int MaximumClaimCandidates = 1500;
    private const int MaximumClaimEvidenceLinks = 5000;
    private const int MaximumMatchProvenance = 20;
    private const int MaximumQueryTokens = 12;
    private const int MaximumExpandedDatabaseTokens = 48;
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "for", "how", "in", "is", "of", "the", "to", "what",
        "bu", "bir", "da", "de", "dersimde", "icin", "ile", "makaledeki", "nasil", "ve"
    };
    private static readonly IReadOnlyDictionary<string, HashSet<string>> SectionIntentTokens =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["purpose"] = Tokens("purpose", "purposes", "aim", "aims", "objective", "objectives",
                "amaç", "amacı", "amaçlar", "hedef", "hedefi", "hedefler"),
            ["methods"] = Tokens("method", "methods", "methodology", "methodological", "approach",
                "approaches", "technique", "techniques", "yöntem", "yöntemi", "yöntemler",
                "yöntemsel", "metodoloji", "yaklaşım", "yaklaşımı", "yaklaşımlar", "teknik", "teknikler"),
            ["data"] = Tokens("data", "dataset", "datasets", "sample", "samples", "veri", "veriyi",
                "veriler", "verileri", "örneklem", "örneklemi", "örneklemler"),
            ["findings"] = Tokens("finding", "findings", "result", "results", "outcome", "outcomes",
                "bulgu", "bulgular", "bulguları", "sonuç", "sonucu", "sonuçlar", "sonuçları"),
            ["limitations"] = Tokens("limitation", "limitations", "constraint", "constraints",
                "sınırlılık", "sınırlılıklar", "kısıt", "kısıtlar", "eksiklik", "eksiklikler")
        };
    private static readonly QueryIntent[] QueryIntents =
    [
        new("purpose", Tokens("purpose", "aim", "objective", "amac", "hedef"),
            Tokens("purpose", "aim", "objective", "goal"), ["purpose"]),
        new("methods", Tokens("method", "methodology", "approach", "technique", "procedure",
                "metod", "metodoloji", "yontem", "yaklasim", "teknik", "prosedur"),
            Tokens("method", "methods", "methodology", "approach", "technique", "procedure",
                "algorithm", "optimization", "training"), ["methods"]),
        new("data", Tokens("data", "dataset", "sample", "veri", "orneklem"),
            Tokens("data", "dataset", "sample", "cohort", "participants"), ["data"]),
        new("findings", Tokens("finding", "result", "outcome", "bulgu", "sonuc"),
            Tokens("finding", "findings", "result", "results", "outcome", "performance",
                "achieve", "achieves", "demonstrate", "show", "outperform"), ["findings"]),
        new("limitations", Tokens("limitation", "constraint", "caveat", "drawback", "sinirlilik",
                "kisit", "eksik", "hata", "belirsiz"),
            Tokens("limitation", "limitations", "constraint", "constraints", "caveat", "drawback",
                "does not apply", "non-convex", "nonconvex", "future work"), ["limitations"]),
        new("conditions", Tokens("condition", "assumption", "check", "recheck", "kosul", "varsayim",
                "kontrol", "denetle", "hata", "belirsiz"),
            Tokens("condition", "conditions", "assume", "assumes", "assumption", "assumptions",
                "bounded", "bound", "guarantee", "theorem", "require", "requires", "gamma"),
            ["methods", "findings", "limitations"])
    ];

    public async Task<AcademicEvidenceSearchResponse> SearchAsync(
        string personelId,
        AcademicEvidenceSearchRequest request,
        CancellationToken cancellationToken)
    {
        string normalizedQuery = NormalizeForRanking(request.Query);
        if (normalizedQuery.Length == 0)
            throw new ArgumentException("Query must contain a letter or number.", nameof(request));

        int[] requestedIds = (request.CanonicalWorkIds ?? []).Where(id => id > 0)
            .Distinct().Order().ToArray();
        if (requestedIds.Length > 100)
            throw new ArgumentException("At most 100 canonical works may be selected.", nameof(request));
        int take = Math.Clamp(request.Take, 1, 20);

        IQueryable<int> associatedWorks = database.CanonicalResearcherWorks.AsNoTracking()
            .Where(value => value.PersonelId == personelId)
            .Select(value => value.CanonicalWorkId);
        int eligibleCount = requestedIds.Length == 0
            ? await associatedWorks.CountAsync(cancellationToken)
            : await associatedWorks.CountAsync(id => requestedIds.Contains(id), cancellationToken);

        // Select latest first. An invalid latest run suppresses that language instead of falling back.
        IQueryable<RunProjection> runsQuery = database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Where(run => database.CanonicalResearcherWorks.Any(association =>
                association.PersonelId == personelId &&
                association.CanonicalWorkId == run.CanonicalWorkId))
            .Where(run => requestedIds.Length == 0 || requestedIds.Contains(run.CanonicalWorkId))
            .Where(run => !database.CanonicalArticleAnalysisRuns.Any(later =>
                later.CanonicalWorkId == run.CanonicalWorkId &&
                later.Language == run.Language && later.Id > run.Id))
            .Where(run => run.ArticleSourceSnapshot != null &&
                run.ArticleSourceSnapshot.CanonicalWorkId == run.CanonicalWorkId)
            .OrderBy(run => run.CanonicalWorkId)
            .ThenBy(run => run.Language)
            .ThenBy(run => run.Id)
            .Select(run => new RunProjection(
                run.Id, run.CanonicalWorkId, run.ArticleSourceSnapshotId,
                run.Language, run.VerificationStatus, run.IsPartial, run.ScopeReason,
                run.ProcessedChunks, run.TotalChunks, run.ProcessedPages,
                run.TextBearingPages, run.TotalPages,
                run.ArticleSourceSnapshot!.SourceKind,
                run.ArticleSourceSnapshot.ExtractionVersion,
                run.ArticleSourceSnapshot.ExtractedTextHash));
        List<RunProjection> runs = await runsQuery.ToListAsync(cancellationToken);
        string currentAnalysisPolicy = (automationOptions?.Value ?? new ArticleSummaryAutomationOptions())
            .PolicyVersion.Trim();
        IReadOnlyDictionary<long, AnalysisFreshnessResult> freshness =
            await AnalysisFreshnessEvaluator.EvaluateAsync(database,
                runs.Select(value => value.Id).ToArray(), currentAnalysisPolicy, cancellationToken);
        string freshnessHash = AnalysisFreshnessEvaluator.CreateFreshnessHash(
            freshness.Values, currentAnalysisPolicy);
        HashSet<int> coveredWorks = runs.Select(run => run.CanonicalWorkId).ToHashSet();

        string[] tokens = TokenizeForRanking(request.Query);
        string[] databaseTokens = TokenizeForDatabase(request.Query);
        string[] intentNames = DetectIntentNames(request.Query);
        string[] intentTerms = ExpandedIntentTerms(intentNames);
        string[] expandedDatabaseTokens = databaseTokens.Concat(intentTerms)
            .Distinct(StringComparer.Ordinal).Take(MaximumExpandedDatabaseTokens).ToArray();
        string[] sectionIntents = DetectSectionIntents(request.Query, intentNames);
        string queryPlanHash = Hash(string.Join("\n",
        [
            CatalogVersion,
            normalizedQuery,
            string.Join('|', tokens),
            string.Join('|', databaseTokens),
            string.Join('|', intentNames),
            string.Join('|', intentTerms),
            string.Join('|', sectionIntents)
        ]));

        long[] snapshotIds = runs.Select(run => run.ArticleSourceSnapshotId).Distinct().ToArray();
        IQueryable<ArticleSourceSpanSnapshot> spansQuery = database.ArticleSourceSpans.AsNoTracking()
            .Where(span => snapshotIds.Contains(span.ArticleSourceSnapshotId))
            .Where(ContainsAnySpanToken(expandedDatabaseTokens));
        List<SpanProjection> spans = await spansQuery
            .OrderBy(span => span.ArticleSourceSnapshotId).ThenBy(span => span.Ordinal)
            .ThenBy(span => span.Id).Select(span => new SpanProjection(
                span.Id, span.ArticleSourceSnapshotId, span.SourceId, span.Ordinal,
                span.PageNumber, span.StartOffset, span.EndOffset, span.Text))
            .Take(MaximumCandidateSpans + 1).ToListAsync(cancellationToken);
        bool directTruncated = spans.Count > MaximumCandidateSpans;
        if (directTruncated)
            spans.RemoveAt(spans.Count - 1);

        Dictionary<long, RunProjection> runBySnapshot = runs
            .GroupBy(run => run.ArticleSourceSnapshotId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(run => run.Id).First());
        Dictionary<long, RunProjection> runById = runs.ToDictionary(run => run.Id);
        Dictionary<long, AccumulatedSpan> accumulated = [];
        foreach (SpanProjection span in spans)
        {
            TextMatch match = MatchForRanking(span.Text, normalizedQuery, tokens);
            string[] matchedIntents = MatchIntentNames(span.Text, intentNames);
            if (match.Score <= 0 && matchedIntents.Length == 0)
                continue;
            RunProjection run = runBySnapshot[span.ArticleSourceSnapshotId];
            AccumulatedSpan value = GetOrAdd(accumulated, span, run);
            value.DirectScore = Math.Max(value.DirectScore, match.Score);
            value.HasDirectExactPhrase |= match.HasExactPhrase;
            if (match.Score > 0)
                value.Provenance.Add(new MatchProvenance("source_text", run.Id, run.Language, null, null));
            foreach (string intent in matchedIntents)
            {
                value.IntentNames.Add(intent);
                if ((intent != "conditions" || HasExplicitConditionEvidence(span.Text)) &&
                    (intent != "limitations" || HasExplicitLimitationEvidence(span.Text)))
                    value.DiversityIntentNames.Add(intent);
                value.Provenance.Add(new MatchProvenance("source_text_intent", run.Id, run.Language,
                    null, intent));
            }
            value.IntentTermScore = Math.Max(value.IntentTermScore,
                CountIntentTermMatches(span.Text, intentNames));
        }

        long[] verifiedRunIds = runs.Where(run =>
            run.VerificationStatus.Equals(VerifiedStatus, StringComparison.Ordinal))
            .Select(run => run.Id).ToArray();
        long[] bridgeCoveredRunIds = verifiedRunIds.Length == 0 ? [] :
            await database.CanonicalArticleClaimEvidence.AsNoTracking()
                .Where(link => link.CanonicalArticleClaim != null &&
                    verifiedRunIds.Contains(link.CanonicalArticleClaim.CanonicalArticleAnalysisRunId))
                .Where(link => link.CanonicalArticleClaim!.CanonicalArticleAnalysisRun != null &&
                    link.ArticleSourceSpan != null && link.ArticleSourceSpan.ArticleSourceSnapshot != null &&
                    link.CanonicalArticleClaim.CanonicalArticleAnalysisRun.ArticleSourceSnapshotId ==
                        link.ArticleSourceSpan.ArticleSourceSnapshotId &&
                    link.CanonicalArticleClaim.CanonicalArticleAnalysisRun.CanonicalWorkId ==
                        link.ArticleSourceSpan.ArticleSourceSnapshot.CanonicalWorkId)
                .Select(link => link.CanonicalArticleClaim!.CanonicalArticleAnalysisRunId)
                .Distinct().Order().ToArrayAsync(cancellationToken);
        IQueryable<CanonicalArticleClaim> claimsQuery = database.CanonicalArticleClaims.AsNoTracking()
            .Where(claim => verifiedRunIds.Contains(claim.CanonicalArticleAnalysisRunId))
            .Where(claim => database.CanonicalResearcherWorks.Any(association =>
                association.PersonelId == personelId &&
                association.CanonicalWorkId == claim.CanonicalArticleAnalysisRun!.CanonicalWorkId))
            .Where(claim => requestedIds.Length == 0 ||
                requestedIds.Contains(claim.CanonicalArticleAnalysisRun!.CanonicalWorkId))
            .Where(claim => claim.CanonicalArticleAnalysisRun!.ArticleSourceSnapshot != null &&
                claim.CanonicalArticleAnalysisRun.ArticleSourceSnapshot.CanonicalWorkId ==
                    claim.CanonicalArticleAnalysisRun.CanonicalWorkId)
            .Where(ContainsClaimCandidate(databaseTokens, sectionIntents));
        List<ClaimProjection> claimCandidates = await claimsQuery
            .OrderBy(claim => claim.CanonicalArticleAnalysisRun!.CanonicalWorkId)
            .ThenBy(claim => claim.CanonicalArticleAnalysisRun!.Language)
            .ThenBy(claim => claim.CanonicalArticleAnalysisRunId)
            .ThenBy(claim => claim.SectionOrder).ThenBy(claim => claim.Ordinal).ThenBy(claim => claim.Id)
            .Select(claim => new ClaimProjection(claim.Id, claim.CanonicalArticleAnalysisRunId,
                claim.CanonicalArticleAnalysisRun!.CanonicalWorkId,
                claim.CanonicalArticleAnalysisRun.ArticleSourceSnapshotId,
                claim.CanonicalArticleAnalysisRun.Language, claim.Section, claim.SectionOrder,
                claim.Ordinal, claim.Text))
            .Take(MaximumClaimCandidates + 1).ToListAsync(cancellationToken);
        bool claimCandidatesTruncated = claimCandidates.Count > MaximumClaimCandidates;
        if (claimCandidatesTruncated)
            claimCandidates.RemoveAt(claimCandidates.Count - 1);

        Dictionary<long, MatchedClaim> matchedClaims = claimCandidates.Select(claim =>
        {
            TextMatch textMatch = MatchForRanking(claim.Text, normalizedQuery, tokens);
            bool sectionMatch = sectionIntents.Contains(NormalizeSection(claim.Section), StringComparer.Ordinal);
            return new MatchedClaim(claim, textMatch.Score, textMatch.HasExactPhrase, sectionMatch);
        }).Where(match => match.ClaimScore > 0 || match.SectionMatch)
          .ToDictionary(match => match.Claim.Id);

        List<ClaimEvidenceProjection> claimEvidence = [];
        bool claimEvidenceTruncated = false;
        if (matchedClaims.Count > 0)
        {
            long[] matchedClaimIds = matchedClaims.Keys.Order().ToArray();
            claimEvidence = await database.CanonicalArticleClaimEvidence.AsNoTracking()
                .Where(link => matchedClaimIds.Contains(link.CanonicalArticleClaimId))
                .Where(link => link.CanonicalArticleClaim != null &&
                    link.CanonicalArticleClaim.CanonicalArticleAnalysisRun != null &&
                    link.ArticleSourceSpan != null && link.ArticleSourceSpan.ArticleSourceSnapshot != null &&
                    link.CanonicalArticleClaim.CanonicalArticleAnalysisRun.ArticleSourceSnapshotId ==
                        link.ArticleSourceSpan.ArticleSourceSnapshotId &&
                    link.CanonicalArticleClaim.CanonicalArticleAnalysisRun.CanonicalWorkId ==
                        link.ArticleSourceSpan.ArticleSourceSnapshot.CanonicalWorkId)
                .OrderBy(link => link.CanonicalArticleClaimId).ThenBy(link => link.Ordinal).ThenBy(link => link.Id)
                .Select(link => new ClaimEvidenceProjection(link.Id, link.CanonicalArticleClaimId,
                    new SpanProjection(link.ArticleSourceSpan!.Id,
                        link.ArticleSourceSpan.ArticleSourceSnapshotId, link.ArticleSourceSpan.SourceId,
                        link.ArticleSourceSpan.Ordinal, link.ArticleSourceSpan.PageNumber,
                        link.ArticleSourceSpan.StartOffset, link.ArticleSourceSpan.EndOffset,
                        link.ArticleSourceSpan.Text)))
                .Take(MaximumClaimEvidenceLinks + 1).ToListAsync(cancellationToken);
            claimEvidenceTruncated = claimEvidence.Count > MaximumClaimEvidenceLinks;
            if (claimEvidenceTruncated)
                claimEvidence.RemoveAt(claimEvidence.Count - 1);
        }

        foreach (ClaimEvidenceProjection link in claimEvidence)
        {
            MatchedClaim match = matchedClaims[link.ClaimId];
            RunProjection run = runById[match.Claim.RunId];
            AccumulatedSpan value = GetOrAdd(accumulated, link.Span, run);
            value.ClaimScore = Math.Max(value.ClaimScore, match.ClaimScore);
            value.HasSectionMatch |= match.SectionMatch;
            if (match.ClaimScore > 0)
                value.Provenance.Add(new MatchProvenance("summary_claim", match.Claim.RunId,
                    match.Claim.Language, match.Claim.Id, NormalizeSection(match.Claim.Section)));
            if (match.SectionMatch)
            {
                string normalizedSection = NormalizeSection(match.Claim.Section);
                foreach (string intent in intentNames.Where(intent =>
                             intent is not "conditions" and not "limitations" &&
                             QueryIntents.Single(definition => definition.Name == intent)
                                 .Sections.Contains(normalizedSection, StringComparer.Ordinal)))
                {
                    value.DiversityIntentNames.Add(intent);
                    value.VerifiedSectionIntentNames.Add(intent);
                }
                value.Provenance.Add(new MatchProvenance("summary_section", match.Claim.RunId,
                    match.Claim.Language, match.Claim.Id, normalizedSection));
            }
        }

        List<RankedSpan> allRanked = accumulated.Values
            .Select(value => new RankedSpan(value, RankScore(value)))
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.Value.Run.CanonicalWorkId)
            .ThenBy(value => value.Value.Span.ArticleSourceSnapshotId)
            .ThenBy(value => value.Value.Span.Ordinal)
            .ThenBy(value => value.Value.Span.Id).ToList();
        List<RankedSpan> ranked = SelectDiversified(allRanked, intentNames, requestedIds, take);

        string claimBridgeHash = Hash(string.Join("\n",
            claimCandidates.Select(claim =>
                $"claim:{claim.Id}:{claim.RunId}:{claim.WorkId}:{claim.SnapshotId}:{claim.Language}:{claim.Section}:{claim.SectionOrder}:{claim.Ordinal}:{Hash(claim.Text)}")
            .Concat(claimEvidence.Select(link =>
                $"link:{link.Id}:{link.ClaimId}:{link.Span.Id}:{link.Span.ArticleSourceSnapshotId}:{Hash(link.Span.Text)}"))
            .Append($"bounds:{MaximumClaimCandidates}:{MaximumClaimEvidenceLinks}:{claimCandidatesTruncated}:{claimEvidenceTruncated}")));
        string sourceCorpusIdentity = string.Join("\n", runs.Select(run =>
            $"{run.Id}:{run.CanonicalWorkId}:{run.ArticleSourceSnapshotId}:{run.Language}:{run.VerificationStatus}:{run.ExtractedTextHash}"));
        string corpusHash = Hash(sourceCorpusIdentity);
        string queryHash = Hash(normalizedQuery);
        string filter = string.Join(',', requestedIds);
        RunProjection[] verifiedRuns = runs.Where(run => verifiedRunIds.Contains(run.Id)).ToArray();
        RunProjection[] bridgeCoveredRuns = runs.Where(run => bridgeCoveredRunIds.Contains(run.Id)).ToArray();
        bool bridgeTruncated = claimCandidatesTruncated || claimEvidenceTruncated;
        return new()
        {
            CatalogVersion = CatalogVersion,
            CorpusFreshness = "Freshness is evaluated against saved provider/source metadata and the configured analysis policy; Current does not prove that remote URL bytes are unchanged or real-time fresh. Verified saved claims may provide bounded search hints to their original source spans. Deterministic Turkish/English lexical intent expansion improves bounded recall but is not semantic or complete search.",
            NormalizedQuery = normalizedQuery,
            QueryHash = queryHash,
            QueryPlanHash = queryPlanHash,
            CorpusHash = corpusHash,
            ClaimBridgeHash = claimBridgeHash,
            InputHash = Hash(string.Join("\n",
            [
                CatalogVersion, queryHash, queryPlanHash, filter, take.ToString(CultureInfo.InvariantCulture),
                corpusHash, freshnessHash, claimBridgeHash, directTruncated.ToString(), bridgeTruncated.ToString()
            ])),
            FreshnessHash = freshnessHash,
            RequestedCanonicalWorkCount = requestedIds.Length,
            EligibleCanonicalWorkCount = eligibleCount,
            CoveredCanonicalWorkCount = coveredWorks.Count,
            MissingSourceCanonicalWorkCount = Math.Max(0, eligibleCount - coveredWorks.Count),
            CandidateSpanCount = spans.Count,
            CurrentAnalysisRunCount = freshness.Values.Count(value => value.Status == AnalysisFreshnessStatus.Current),
            StaleAnalysisRunCount = freshness.Values.Count(value => value.Status == AnalysisFreshnessStatus.Stale),
            UnknownAnalysisRunCount = freshness.Values.Count(value => value.Status == AnalysisFreshnessStatus.Unknown),
            IsCorpusTruncated = directTruncated || bridgeTruncated,
            IsDirectCorpusTruncated = directTruncated,
            ClaimBridgeEligibleRunCount = verifiedRuns.Length,
            ClaimBridgeCoveredCanonicalWorkCount = bridgeCoveredRuns.Select(run => run.CanonicalWorkId).Distinct().Count(),
            ClaimBridgeCandidateClaimCount = matchedClaims.Count,
            ClaimBridgeCandidateSpanCount = claimEvidence.Select(link => link.Span.Id).Distinct().Count(),
            IsClaimBridgeTruncated = bridgeTruncated,
            AnalysisLanguageCoverage = Coverage(runs, run => run.Language),
            ClaimBridgeLanguageCoverage = Coverage(bridgeCoveredRuns, run => run.Language),
            SourceKindCoverage = Coverage(runs, run => run.SourceKind),
            Hits = ranked.Select((value, index) => Map(value, index + 1, freshness[value.Value.Run.Id])).ToList()
        };
    }

    private static AccumulatedSpan GetOrAdd(Dictionary<long, AccumulatedSpan> accumulated,
        SpanProjection span, RunProjection run)
    {
        if (!accumulated.TryGetValue(span.Id, out AccumulatedSpan? value))
        {
            value = new(span, run);
            accumulated.Add(span.Id, value);
        }
        return value;
    }

    private static decimal RankScore(AccumulatedSpan value)
    {
        if (value.HasDirectExactPhrase)
            return 1_000_000m + value.DirectScore;
        if (value.ClaimScore > 0)
            return 100_000m + value.ClaimScore;
        if (value.DirectScore > 0)
            return 10_000m + value.DirectScore;
        if (value.IntentNames.Count > 0)
            return 5_000m + value.IntentTermScore * 10m + value.IntentNames.Count;
        return value.HasSectionMatch ? 1_000m : 0m;
    }

    private static List<RankedSpan> SelectDiversified(
        IReadOnlyList<RankedSpan> candidates, IReadOnlyList<string> intents,
        IReadOnlyList<int> explicitlySelectedWorks, int take)
    {
        List<RankedSpan> selected = [];
        HashSet<long> selectedIds = [];
        void Add(RankedSpan value)
        {
            if (selected.Count < take && selectedIds.Add(value.Value.Span.Id))
                selected.Add(value);
        }

        RankedSpan? bestExact = candidates.FirstOrDefault(value => value.Value.HasDirectExactPhrase);
        if (bestExact is not null)
            Add(bestExact);
        if (explicitlySelectedWorks.Count > 1)
        {
            foreach (string intent in intents)
            foreach (int workId in explicitlySelectedWorks)
            {
                if (selected.Any(value => value.Value.Run.CanonicalWorkId == workId &&
                        value.Value.DiversityIntentNames.Contains(intent)))
                    continue;
                IEnumerable<RankedSpan> ownedCandidates = candidates.Where(value =>
                    !selectedIds.Contains(value.Value.Span.Id) &&
                    value.Value.Run.CanonicalWorkId == workId);
                RankedSpan? perWork = ownedCandidates.Where(value =>
                        value.Value.VerifiedSectionIntentNames.Contains(intent))
                    .OrderByDescending(value => DiversityScore(value, intent))
                    .ThenByDescending(value => value.Score)
                    .ThenBy(value => value.Value.Span.Ordinal)
                    .ThenBy(value => value.Value.Span.Id)
                    .FirstOrDefault();
                perWork ??= ownedCandidates.Where(value =>
                        value.Value.DiversityIntentNames.Contains(intent))
                    .OrderByDescending(value => DiversityScore(value, intent))
                    .ThenByDescending(value => value.Score)
                    .ThenBy(value => value.Value.Span.Ordinal)
                    .ThenBy(value => value.Value.Span.Id)
                    .FirstOrDefault();
                if (perWork is not null)
                    Add(perWork);
            }
        }
        foreach (string intent in intents)
        {
            RankedSpan? best = candidates.Where(value => !selectedIds.Contains(value.Value.Span.Id) &&
                    value.Value.DiversityIntentNames.Contains(intent))
                .OrderByDescending(value => DiversityScore(value, intent))
                .ThenByDescending(value => value.Score)
                .ThenBy(value => value.Value.Span.Ordinal)
                .ThenBy(value => value.Value.Span.Id)
                .FirstOrDefault();
            if (best is not null)
                Add(best);
        }
        foreach (RankedSpan candidate in candidates)
            Add(candidate);
        return selected;
    }

    private static AcademicEvidenceSearchHitDto Map(
        RankedSpan ranked, int rank, AnalysisFreshnessResult freshness)
    {
        AccumulatedSpan value = ranked.Value;
        MatchProvenance[] provenance = value.Provenance
            .OrderBy(match => match.Kind == "source_text" ? 0 : 1)
            .ThenBy(match => match.Kind, StringComparer.Ordinal)
            .ThenBy(match => match.AnalysisRunId)
            .ThenBy(match => match.ClaimId)
            .ThenBy(match => match.Section, StringComparer.Ordinal)
            .ToArray();
        return new()
        {
            Rank = rank,
            Score = ranked.Score,
            EvidenceId = $"work:{value.Run.CanonicalWorkId}:snapshot:{value.Span.ArticleSourceSnapshotId}:span:{value.Span.Id}",
            CanonicalWorkId = value.Run.CanonicalWorkId,
            ArticleSourceSnapshotId = value.Span.ArticleSourceSnapshotId,
            ArticleSourceSpanId = value.Span.Id,
            AnalysisRunId = value.Run.Id,
            SourceId = value.Span.SourceId,
            PageNumber = value.Span.PageNumber,
            StartOffset = value.Span.StartOffset,
            EndOffset = value.Span.EndOffset,
            ExactText = value.Span.Text,
            AnalysisLanguage = value.Run.Language,
            SourceKind = value.Run.SourceKind,
            ExtractionVersion = value.Run.ExtractionVersion,
            ExtractedTextHash = value.Run.ExtractedTextHash,
            IsPartial = value.Run.IsPartial,
            AnalysisFreshnessStatus = freshness.Status,
            AnalysisFreshnessReasons = freshness.Reasons.ToList(),
            IsMatchProvenanceTruncated = provenance.Length > MaximumMatchProvenance,
            MatchProvenance = provenance.Take(MaximumMatchProvenance).Select(match =>
                new AcademicEvidenceMatchProvenanceDto
                {
                    Kind = match.Kind,
                    AnalysisRunId = match.AnalysisRunId,
                    Language = match.Language,
                    CanonicalArticleClaimId = match.ClaimId,
                    Section = match.Section
                }).ToList(),
            SourceCoverage = new()
            {
                ProcessedChunks = value.Run.ProcessedChunks,
                TotalChunks = value.Run.TotalChunks,
                ProcessedPages = value.Run.ProcessedPages,
                TextBearingPages = value.Run.TextBearingPages,
                TotalPages = value.Run.TotalPages,
                ScopeReason = value.Run.ScopeReason
            }
        };
    }

    private static List<AcademicEvidenceCoverageBucketDto> Coverage(
        IEnumerable<RunProjection> runs, Func<RunProjection, string> key) => runs
        .GroupBy(run => key(run).Trim(), StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => new AcademicEvidenceCoverageBucketDto
        {
            Value = group.Key,
            CanonicalWorkCount = group.Select(run => run.CanonicalWorkId).Distinct().Count()
        }).ToList();

    internal static AcademicEvidenceSearchEvaluation Evaluate(
        IReadOnlyCollection<(IReadOnlyList<string> RankedEvidenceIds,
            IReadOnlyCollection<string> RelevantEvidenceIds, bool HasSource)> cases,
        int k)
    {
        int boundedK = Math.Max(1, k);
        var eligible = cases.Where(value => value.HasSource).ToArray();
        int relevant = eligible.Sum(value => value.RelevantEvidenceIds.Count);
        int retrieved = eligible.Sum(value => value.RankedEvidenceIds.Take(boundedK)
            .Intersect(value.RelevantEvidenceIds, StringComparer.Ordinal).Count());
        decimal reciprocalRanks = eligible.Sum(value =>
        {
            int first = value.RankedEvidenceIds.Take(boundedK).Select((id, index) => (id, index))
                .Where(item => value.RelevantEvidenceIds.Contains(item.id, StringComparer.Ordinal))
                .Select(item => item.index + 1).DefaultIfEmpty(0).First();
            return first == 0 ? 0m : 1m / first;
        });
        return new()
        {
            CaseCount = cases.Count,
            EligibleCaseCount = eligible.Length,
            MissingSourceCaseCount = cases.Count - eligible.Length,
            RelevantEvidenceCount = relevant,
            RetrievedRelevantAtK = retrieved,
            RecallAtK = relevant == 0 ? null : (decimal)retrieved / relevant,
            MeanReciprocalRank = eligible.Length == 0 ? null : reciprocalRanks / eligible.Length
        };
    }

    internal static string NormalizeForRanking(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD).ToLowerInvariant()
            .Replace('\u0131', 'i');
        string withoutMarks = string.Concat(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
        return Whitespace().Replace(NonWord().Replace(withoutMarks, " "), " ").Trim();
    }

    internal static string[] TokenizeForRanking(string value)
    {
        string normalized = NormalizeForRanking(value);
        string[] allTokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] contentTokens = allTokens.Where(token => token.Length >= 2 && !Stopwords.Contains(token))
            .Distinct(StringComparer.Ordinal).Take(MaximumQueryTokens).ToArray();
        return contentTokens.Length > 0 ? contentTokens :
            allTokens.Distinct(StringComparer.Ordinal).Take(MaximumQueryTokens).ToArray();
    }

    internal static string[] TokenizeForDatabase(string value)
        => TokenizeForRanking(value);

    internal static decimal ScoreForRanking(
        string text, string normalizedQuery, IReadOnlyCollection<string> tokens) =>
        MatchForRanking(text, normalizedQuery, tokens).Score;

    private static TextMatch MatchForRanking(
        string text, string normalizedQuery, IReadOnlyCollection<string> tokens)
    {
        string normalizedText = NormalizeForRanking(text);
        string paddedText = " " + normalizedText + " ";
        string paddedQuery = " " + normalizedQuery + " ";
        int phraseCount = Count(paddedText, paddedQuery);
        Dictionary<string, int> wordCounts = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        int tokenScore = tokens.Where(token => !token.Contains(' '))
            .Sum(token => wordCounts.TryGetValue(token, out int count) ? Math.Min(3, count) : 0);
        return new(phraseCount * 100m + tokenScore, phraseCount > 0);
    }

    private static int Count(string value, string search)
    {
        if (search.Length == 0)
            return 0;
        int count = 0;
        for (int index = 0; (index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0;
            index += search.Length)
            count++;
        return count;
    }

    private static string[] DetectSectionIntents(string query, IReadOnlyCollection<string>? detected = null)
    {
        HashSet<string> queryTokens = NormalizeForRanking(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        IEnumerable<string> legacy = SectionIntentTokens.Where(section => section.Value.Overlaps(queryTokens))
            .Select(section => section.Key);
        IEnumerable<string> expanded = QueryIntents.Where(intent => (detected ?? DetectIntentNames(query))
                .Contains(intent.Name, StringComparer.Ordinal)).SelectMany(intent => intent.Sections);
        return legacy.Concat(expanded).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal static string[] DetectIntentNames(string query)
    {
        string[] queryTokens = NormalizeForRanking(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        HashSet<string> detected = QueryIntents.Where(intent => intent.QueryPrefixes.Any(prefix => queryTokens.Any(token =>
                MatchesQueryPrefix(token, prefix))))
            .Select(intent => intent.Name).ToHashSet(StringComparer.Ordinal);
        if (detected.Contains("conditions"))
            detected.Add("findings");
        return QueryIntents.Where(intent => detected.Contains(intent.Name)).Select(intent => intent.Name).ToArray();
    }

    private static bool MatchesQueryPrefix(string token, string prefix)
    {
        if (token.Equals(prefix, StringComparison.Ordinal))
            return true;
        if (!token.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        string suffix = token[prefix.Length..];
        return suffix is "s" or "es" or "i" or "in" or "ini" or "ler" or "lar" or "leri" or "lari" or
            "lerini" or "larini" or "de" or "da" or "den" or "dan" or "inde" or "inda" or "inden" or
            "indan" or "lerinde" or "larinda" or "larindan" or "lik" or "ligi" or "ligiyle" or "sel" or
            "seli" or "sal" or "sali";
    }

    internal static string[] ExpandedIntentTerms(IReadOnlyCollection<string> intentNames) => QueryIntents
        .Where(intent => intentNames.Contains(intent.Name, StringComparer.Ordinal))
        .SelectMany(intent => intent.SourceTerms.Order(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)
        .Take(MaximumExpandedDatabaseTokens).ToArray();

    private static string[] MatchIntentNames(string text, IReadOnlyCollection<string> intentNames)
    {
        string padded = " " + NormalizeForRanking(text) + " ";
        return QueryIntents.Where(intent => intentNames.Contains(intent.Name, StringComparer.Ordinal) &&
                intent.SourceTerms.Any(term => padded.Contains(" " + term + " ", StringComparison.Ordinal)))
            .Select(intent => intent.Name).ToArray();
    }

    private static int CountIntentTermMatches(string text, IReadOnlyCollection<string> intentNames)
    {
        string padded = " " + NormalizeForRanking(text) + " ";
        return QueryIntents.Where(intent => intentNames.Contains(intent.Name, StringComparer.Ordinal))
            .SelectMany(intent => intent.SourceTerms)
            .Count(term => padded.Contains(" " + term + " ", StringComparison.Ordinal));
    }

    private static bool HasExplicitConditionEvidence(string text)
    {
        string padded = " " + NormalizeForRanking(text) + " ";
        return new[] { "condition", "conditions", "assume", "assumes", "assumption", "assumptions",
            "gamma" }.Any(term => padded.Contains(" " + term + " ", StringComparison.Ordinal)) || text.Contains('γ') ||
            padded.Contains(" bounded ", StringComparison.Ordinal) &&
            padded.Contains(" gradient", StringComparison.Ordinal) &&
            padded.Contains(" distance ", StringComparison.Ordinal);
    }

    private static bool HasExplicitLimitationEvidence(string text)
    {
        string padded = " " + NormalizeForRanking(text) + " ";
        return new[] { "limitation", "limitations", "constraint", "constraints", "caveat", "drawback",
            "does not apply", "future work" }.Any(term =>
                padded.Contains(" " + term + " ", StringComparison.Ordinal));
    }

    private static int DiversityScore(RankedSpan value, string intent)
    {
        string padded = " " + NormalizeForRanking(value.Value.Span.Text) + " ";
        bool Has(string term) => padded.Contains(" " + term + " ", StringComparison.Ordinal);
        int verifiedSection = value.Value.VerifiedSectionIntentNames.Contains(intent) ? 1_000 : 0;
        if (intent == "conditions")
            return verifiedSection +
                (Has("assume") || Has("assumes") || Has("assumption") || Has("assumptions") ? 100 : 0) +
                (Has("gamma") || value.Value.Span.Text.Contains('γ') ? 100 : 0) + (Has("bounded") ? 50 : 0) +
                (Has("gradient") || Has("gradients") ? 50 : 0) + (Has("distance") ? 50 : 0) +
                (Has("condition") || Has("conditions") ? 20 : 0);
        if (intent == "limitations")
            return verifiedSection + (Has("does not apply") ? 200 : 0) +
                (Has("limitation") || Has("limitations") ? 100 : 0) +
                (Has("constraint") || Has("constraints") || Has("caveat") || Has("drawback") ? 80 : 0) +
                (Has("future work") ? 50 : 0);
        QueryIntent definition = QueryIntents.Single(definition => definition.Name == intent);
        return verifiedSection + definition.SourceTerms.Count(Has) * 10;
    }

    private static HashSet<string> Tokens(params string[] values) => values
        .Select(NormalizeForRanking).ToHashSet(StringComparer.Ordinal);

    private static string NormalizeSection(string value) => NormalizeForRanking(value);

    private static Expression<Func<ArticleSourceSpanSnapshot, bool>> ContainsAnySpanToken(
        IReadOnlyCollection<string> tokens)
    {
        ParameterExpression span = Expression.Parameter(typeof(ArticleSourceSpanSnapshot), "span");
        return Expression.Lambda<Func<ArticleSourceSpanSnapshot, bool>>(
            ContainsAnyToken(Expression.Property(span, nameof(ArticleSourceSpanSnapshot.Text)), tokens), span);
    }

    private static Expression<Func<CanonicalArticleClaim, bool>> ContainsClaimCandidate(
        IReadOnlyCollection<string> tokens, IReadOnlyCollection<string> sections)
    {
        ParameterExpression claim = Expression.Parameter(typeof(CanonicalArticleClaim), "claim");
        Expression body = ContainsAnyToken(Expression.Property(claim, nameof(CanonicalArticleClaim.Text)), tokens);
        MemberExpression section = Expression.Property(claim, nameof(CanonicalArticleClaim.Section));
        Expression foldedSection = FoldForDatabase(section);
        foreach (string value in sections)
            body = Expression.OrElse(body, Expression.Equal(foldedSection, Expression.Constant(value)));
        return Expression.Lambda<Func<CanonicalArticleClaim, bool>>(body, claim);
    }

    private static Expression ContainsAnyToken(MemberExpression text, IReadOnlyCollection<string> tokens)
    {
        Expression folded = FoldForDatabase(text);
        Expression body = Expression.Constant(false);
        foreach (string token in tokens)
        {
            MethodCallExpression contains = Expression.Call(folded, nameof(string.Contains),
                Type.EmptyTypes, Expression.Constant(token));
            body = Expression.OrElse(body, contains);
        }
        return body;
    }

    private static Expression FoldForDatabase(MemberExpression text)
    {
        MethodCallExpression lower = Expression.Call(text, nameof(string.ToLower), Type.EmptyTypes);
        MethodCallExpression folded = Expression.Call(lower, nameof(string.Replace), Type.EmptyTypes,
            Expression.Constant("ı"), Expression.Constant("i"));
        return Expression.Call(typeof(RelationalDbFunctionsExtensions),
            nameof(RelationalDbFunctionsExtensions.Collate), [typeof(string)],
            Expression.Property(null, typeof(EF).GetProperty(nameof(EF.Functions))!), folded,
            Expression.Constant("Latin1_General_100_CI_AI"));
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    private sealed record RunProjection(long Id, int CanonicalWorkId,
        long ArticleSourceSnapshotId, string Language, string VerificationStatus,
        bool IsPartial, string? ScopeReason, int ProcessedChunks, int TotalChunks,
        int ProcessedPages, int TextBearingPages, int TotalPages, string SourceKind,
        string ExtractionVersion, string ExtractedTextHash);

    private sealed record SpanProjection(long Id, long ArticleSourceSnapshotId,
        string SourceId, int Ordinal, int? PageNumber, int StartOffset, int EndOffset, string Text);

    private sealed record ClaimProjection(long Id, long RunId, int WorkId, long SnapshotId,
        string Language, string Section, int SectionOrder, int Ordinal, string Text);

    private sealed record ClaimEvidenceProjection(long Id, long ClaimId, SpanProjection Span);
    private sealed record MatchedClaim(ClaimProjection Claim, decimal ClaimScore,
        bool HasExactPhrase, bool SectionMatch);
    private sealed record MatchProvenance(string Kind, long AnalysisRunId, string Language,
        long? ClaimId, string? Section);
    private sealed record TextMatch(decimal Score, bool HasExactPhrase);
    private sealed record QueryIntent(string Name, HashSet<string> QueryPrefixes,
        HashSet<string> SourceTerms, string[] Sections);
    private sealed record RankedSpan(AccumulatedSpan Value, decimal Score);

    private sealed class AccumulatedSpan(SpanProjection span, RunProjection run)
    {
        public SpanProjection Span { get; } = span;
        public RunProjection Run { get; } = run;
        public decimal DirectScore { get; set; }
        public bool HasDirectExactPhrase { get; set; }
        public decimal ClaimScore { get; set; }
        public bool HasSectionMatch { get; set; }
        public int IntentTermScore { get; set; }
        public HashSet<string> DiversityIntentNames { get; } = new(StringComparer.Ordinal);
        public HashSet<string> VerifiedSectionIntentNames { get; } = new(StringComparer.Ordinal);
        public HashSet<string> IntentNames { get; } = new(StringComparer.Ordinal);
        public HashSet<MatchProvenance> Provenance { get; } = [];
    }
}
