using System.Text;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Analysis;

public sealed class ArticleSummarizer(IArticleSummaryGenerator generator, IArticleClaimVerifier verifier,
    IOptions<AiOptions> options)
{
    private const string BudgetReason = "Automatic checker could not finish within its budget; claim excluded without verification.";
    public async Task<ArticleSummaryReport> SummarizeAsync(SummarizeArticleRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        IReadOnlyList<ArticleSourceSpan> catalog = request.SourceSpans ?? ArticleSourceCatalog.Create(request.Pages);
        if (!ArticleSourceCatalog.IsValid(request.Pages, catalog, request.SourceKind))
            throw new BadHttpRequestException("The article source catalog does not match its canonical pages.");

        List<ChunkResult> results = [];
        try
        {
            results.Add(await GenerateAndVerify(request, catalog, catalog, "chunk-1", cancellationToken));
        }
        catch (AnalysisInputTooLargeException) { }
        catch (InvalidAnalysisException exception) when (exception.Reason == AnalysisFailure.IncompleteOutput) { }

        if (results.Count == 0)
        {
            List<IReadOnlyList<ArticleSourceSpan>> chunks = Chunk(catalog, options.Value.ArticleFallbackChunkBytes);
            for (int i = 0; i < chunks.Count; i++)
                await GenerateFallbackAsync(request, chunks[i], catalog, $"chunk-{i + 1}", results, cancellationToken);
        }
        return CreateReport(request, results);
    }

    private async Task<ChunkResult> GenerateAndVerify(SummarizeArticleRequest request,
        IReadOnlyList<ArticleSourceSpan> spans, IReadOnlyList<ArticleSourceSpan> catalog, string chunkId,
        CancellationToken cancellationToken)
    {
        GeneratedArticleChunk generated = await generator.GenerateAsync(request.Language, request.SourceKind, spans, cancellationToken)
            ?? throw new InvalidAnalysisException();
        List<Candidate> candidates = Flatten(generated.Sections, chunkId, spans);
        GeneratedVerificationBatch checkedBatch = await VerifyBatches(request.Language, candidates, catalog, cancellationToken);
        ValidateVerdicts(candidates, checkedBatch.Verdicts);
        return new(generated, checkedBatch, candidates);
    }

    private async Task<GeneratedVerificationBatch> VerifyBatches(string language, IReadOnlyList<Candidate> candidates,
        IReadOnlyList<ArticleSourceSpan> catalog, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return new([], options.Value.ArticleVerifierModel ?? options.Value.ArticleModel, ArticleVerificationPrompt.Version);
        List<GeneratedClaimVerdict> verdicts = []; List<string> models = []; List<string> prompts = [];
        foreach (Candidate[] batch in candidates.Chunk(3))
        {
            IReadOnlyList<GeneratedVerificationBatch> results =
                await VerifyBatchAdaptive(language, batch, catalog, cancellationToken);
            foreach (GeneratedVerificationBatch result in results)
            {
                verdicts.AddRange(result.Verdicts);
                models.Add(result.Model); prompts.Add(result.PromptVersion);
            }
        }
        if (prompts.Distinct(StringComparer.Ordinal).Count() != 1) throw new InvalidAnalysisException();
        return new(verdicts, string.Join(",", models.Distinct()), prompts[0]);
    }

    private async Task<IReadOnlyList<GeneratedVerificationBatch>> VerifyBatchAdaptive(string language,
        IReadOnlyList<Candidate> candidates, IReadOnlyList<ArticleSourceSpan> catalog, CancellationToken cancellationToken)
    {
        try
        {
            return [await verifier.VerifyAsync(language, candidates.Select(x => x.Claim).ToList(), catalog, cancellationToken)];
        }
        catch (Exception exception) when (exception is AnalysisInputTooLargeException ||
            exception is InvalidAnalysisException { Reason: AnalysisFailure.IncompleteOutput })
        {
            if (candidates.Count == 1)
                return [new([new(candidates[0].Claim.ClaimId, "uncertain", BudgetReason)],
                    options.Value.ArticleVerifierModel ?? options.Value.ArticleModel, ArticleVerificationPrompt.Version)];
            int middle = candidates.Count / 2;
            IReadOnlyList<GeneratedVerificationBatch> first = await VerifyBatchAdaptive(
                language, candidates.Take(middle).ToList(), catalog, cancellationToken);
            IReadOnlyList<GeneratedVerificationBatch> second = await VerifyBatchAdaptive(
                language, candidates.Skip(middle).ToList(), catalog, cancellationToken);
            return first.Concat(second).ToList();
        }
    }

    private async Task GenerateFallbackAsync(SummarizeArticleRequest request, IReadOnlyList<ArticleSourceSpan> spans,
        IReadOnlyList<ArticleSourceSpan> catalog, string chunkId, List<ChunkResult> results, CancellationToken cancellationToken)
    {
        try { results.Add(await GenerateAndVerify(request, spans, catalog, chunkId, cancellationToken)); }
        catch (Exception exception) when (exception is AnalysisInputTooLargeException ||
            exception is InvalidAnalysisException { Reason: AnalysisFailure.IncompleteOutput })
        {
            if (spans.Count < 2) throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
            int middle = spans.Count / 2;
            await GenerateFallbackAsync(request, spans.Take(middle).ToList(), catalog, chunkId + "a", results, cancellationToken);
            await GenerateFallbackAsync(request, spans.Skip(middle).ToList(), catalog, chunkId + "b", results, cancellationToken);
        }
    }

    private static List<IReadOnlyList<ArticleSourceSpan>> Chunk(IReadOnlyList<ArticleSourceSpan> spans, int maximumBytes)
    {
        List<IReadOnlyList<ArticleSourceSpan>> chunks = []; List<ArticleSourceSpan> current = []; int bytes = 0;
        foreach (ArticleSourceSpan span in spans)
        {
            int size = Encoding.UTF8.GetByteCount(span.Text) + Encoding.UTF8.GetByteCount(span.SourceId) + 80;
            if (size > maximumBytes) throw new InvalidAnalysisException(AnalysisFailure.IncompleteOutput);
            if (current.Count > 0 && bytes + size > maximumBytes) { chunks.Add(current); current = []; bytes = 0; }
            current.Add(span); bytes += size;
        }
        if (current.Count > 0) chunks.Add(current);
        return chunks;
    }

    private static List<Candidate> Flatten(GeneratedArticleSections sections, string chunkId,
        IReadOnlyList<ArticleSourceSpan> available)
    {
        var groups = new (string Name, IReadOnlyList<GeneratedArticleClaim>? Claims)[]
        {
            ("purpose", sections.Purpose), ("methods", sections.Methods), ("data", sections.Data),
            ("findings", sections.Findings), ("limitations", sections.Limitations)
        };
        if (groups.Any(x => x.Claims is null || x.Claims.Count > 3)) throw new InvalidAnalysisException();
        List<Candidate> result = [];
        foreach (var group in groups)
        foreach (GeneratedArticleClaim claim in group.Claims!)
        {
            if (claim is null || string.IsNullOrWhiteSpace(claim.ClaimId) || string.IsNullOrWhiteSpace(claim.Text) ||
                claim.Text.Length > 1200 || claim.SourceIds is null || claim.SourceIds.Count is 0 or > 2 ||
                claim.SourceIds.Any(id => !available.Any(span => span.SourceId == id && !string.IsNullOrWhiteSpace(span.Text))))
                throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
            result.Add(new(group.Name, claim with { ClaimId = chunkId + ":" + claim.ClaimId, Section = group.Name }));
        }
        if (result.Select(x => x.Claim.ClaimId).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        return result;
    }

    private static void ValidateVerdicts(IReadOnlyList<Candidate> candidates, IReadOnlyList<GeneratedClaimVerdict>? verdicts)
    {
        if (verdicts is null || verdicts.Count != candidates.Count || verdicts.Any(x => x is null) ||
            verdicts.Select(x => x.ClaimId).Distinct(StringComparer.Ordinal).Count() != verdicts.Count ||
            verdicts.Any(x => !candidates.Any(c => c.Claim.ClaimId == x.ClaimId) ||
                x.Verdict is not ("supported" or "unsupported" or "uncertain") || string.IsNullOrWhiteSpace(x.Reason)))
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }

    private static ArticleSummaryReport CreateReport(SummarizeArticleRequest request, IReadOnlyList<ChunkResult> results)
    {
        List<(Candidate Candidate, GeneratedClaimVerdict Verdict)> checkedClaims = results.SelectMany(result =>
            result.Candidates.Select(candidate => (candidate,
                result.Verification.Verdicts.Single(verdict => verdict.ClaimId == candidate.Claim.ClaimId)))).ToList();
        int unsupported = checkedClaims.Count(x => x.Verdict.Verdict == "unsupported");
        int uncertain = checkedClaims.Count(x => x.Verdict.Verdict == "uncertain");
        int budgetUnverified = checkedClaims.Count(x => x.Verdict.Verdict == "uncertain" && x.Verdict.Reason == BudgetReason);
        List<(Candidate Candidate, GeneratedClaimVerdict Verdict)> supported =
            checkedClaims.Where(x => x.Verdict.Verdict == "supported").ToList();
        IReadOnlyList<ArticleClaim> Section(string name)
        {
            List<(Candidate Candidate, GeneratedClaimVerdict Verdict)> distinct = supported
                .Where(x => x.Candidate.Section == name)
                .DistinctBy(x => x.Candidate.Claim.Text, StringComparer.OrdinalIgnoreCase).ToList();
            return distinct.Take(12).Select(x => Resolve(x.Candidate.Claim, request.SourceSpans ??
                ArticleSourceCatalog.Create(request.Pages))).ToList();
        }
        ArticleSummarySections sections = new(Section("purpose"), Section("methods"), Section("data"),
            Section("findings"), Section("limitations"));
        int retained = new[] { sections.Purpose, sections.Methods, sections.Data, sections.Findings, sections.Limitations }.Sum(x => x.Count);
        int duplicateOrCapped = supported.Count - retained;
        int omitted = unsupported + uncertain + duplicateOrCapped;
        List<string> reasons = checkedClaims.Where(x => x.Verdict.Verdict != "supported")
            .Select(x => $"{x.Verdict.Verdict}: {x.Verdict.Reason}").Distinct().Take(20).ToList();
        if (duplicateOrCapped > 0) reasons.Add($"{duplicateOrCapped} duplicate or supported claims exceeding section caps were omitted.");
        string? scope = request.ScopeReason;
        if (unsupported + uncertain > 0)
            scope = JoinReason(scope, $"{unsupported} unsupported and {uncertain} uncertain candidate claims were omitted after automatic checking.");
        if (duplicateOrCapped > 0)
            scope = JoinReason(scope, $"{duplicateOrCapped} duplicate or supported claims exceeding section caps were omitted.");
        string status = supported.Count == 0 ? "insufficient_evidence" : "automatically_checked";
        ArticleCoverage coverage = new(results.Count, results.Count, request.Pages.Count, request.Pages.Count,
            request.TotalSourcePages, omitted, request.IsPartial || omitted > 0 || supported.Count == 0, scope)
        {
            CandidateClaims = checkedClaims.Count, AutomaticallyCheckedClaims = checkedClaims.Count,
            SupportedClaims = supported.Count, UnsupportedClaims = unsupported, UncertainClaims = uncertain,
            DuplicateOrCappedClaims = duplicateOrCapped, BudgetUnverifiedClaims = budgetUnverified,
            OmissionReasons = reasons
        };
        return new(request.Language, request.SourceKind, request.SourceHash, request.ExtractionVersion, coverage, sections,
            string.Join(",", results.Select(x => x.Generated.Model).Distinct()), results[0].Generated.PromptVersion)
        {
            ExtractionMethod = request.SourceKind switch
            {
                "pdf" when request.ExtractionVersion.Contains("ocr", StringComparison.OrdinalIgnoreCase) => "pdf_ocr",
                "pdf" => "pdf_text",
                "html" => "html",
                _ => "abstract"
            },
            Verification = new(status, string.Join(",", results.Select(x => x.Verification.Model).Distinct()),
                results[0].Verification.PromptVersion,
                results.Select(x => x.Generated.Model).Intersect(results.Select(x => x.Verification.Model),
                    StringComparer.OrdinalIgnoreCase).Any(),
                "Automatic model checking reduces unsupported claims but does not guarantee correctness; correlated errors remain possible.")
        };
    }

    private static ArticleClaim Resolve(GeneratedArticleClaim claim, IReadOnlyList<ArticleSourceSpan> catalog) =>
        new(claim.Text, claim.SourceIds.Select(id =>
        {
            ArticleSourceSpan span = catalog.Single(x => x.SourceId == id);
            return new ArticleEvidence(span.Text, span.PageNumber)
                { SourceId = span.SourceId, StartOffset = span.StartOffset, EndOffset = span.EndOffset };
        }).ToList()) { ClaimId = claim.ClaimId };

    private static void ValidateRequest(SummarizeArticleRequest request)
    {
        if (request.Language is not ("tr" or "en") || request.SourceKind is not ("pdf" or "html" or "abstract") ||
            request.Pages is null || request.Pages.Count == 0 || request.Pages.Count > 200 ||
            request.Pages.Any(p => p is null || string.IsNullOrWhiteSpace(p.Text) || p.Text.Length > 500000) ||
            request.TotalSourcePages < request.Pages.Count) throw new BadHttpRequestException("Article text is required.");
    }

    private static string JoinReason(string? existing, string addition) =>
        string.IsNullOrWhiteSpace(existing) ? addition : existing + " " + addition;
    private sealed record Candidate(string Section, GeneratedArticleClaim Claim);
    private sealed record ChunkResult(GeneratedArticleChunk Generated, GeneratedVerificationBatch Verification,
        IReadOnlyList<Candidate> Candidates);
}
