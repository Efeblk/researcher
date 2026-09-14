using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Products.ProductAccess;

namespace ResearcherAnalysisService.Products.Evaluations;

public sealed class ArticleEvaluationScheduler(
    AnalysisDbContext database,
    ArticleEvaluationServiceClient client,
    IOptions<ArticleEvaluationOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StartArticleEvaluationResponse> EnqueueAsync(
        AcademicProductAccessGrant grant,
        StartArticleEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        List<string> profileIds = request.ProfileIds.Select(value => value.Trim()).ToList();
        if (profileIds.Any(value => value.Length is < 1 or > 100) ||
            profileIds.Distinct(StringComparer.Ordinal).Count() != profileIds.Count)
            throw new ArticleEvaluationValidationException("Choose one to three distinct profile IDs.");
        if (request.EnableBlindCrossCheck && profileIds.Count < 2)
            throw new ArticleEvaluationValidationException("Blind cross-check requires at least two profiles.");
        if (request.RealCases.Select(value => (value.CanonicalWorkId, value.Language.Trim()))
            .Distinct().Count() != request.RealCases.Count)
            throw new ArticleEvaluationValidationException("Real cases must be distinct.");

        ArticleEvaluationProfilesResponse available = await client.GetProfilesAsync(cancellationToken);
        Dictionary<string, ArticleEvaluationProfile> byId = available.Profiles
            .ToDictionary(value => value.ProfileId, StringComparer.Ordinal);
        List<ArticleEvaluationProfile> profiles = [];
        foreach (string profileId in profileIds)
        {
            if (!byId.TryGetValue(profileId, out ArticleEvaluationProfile? profile))
                throw new ArticleEvaluationValidationException($"Evaluation profile '{profileId}' is not allowed.");
            if (!string.Equals(profile.Availability, "configured", StringComparison.OrdinalIgnoreCase))
                throw new ArticleEvaluationValidationException($"Evaluation profile '{profileId}' is not available.");
            if (!profile.TaskKinds.Contains(ArticleEvaluationTaskKinds.Calibration, StringComparer.Ordinal) ||
                request.RealCases.Count > 0 && !profile.TaskKinds.Contains(ArticleEvaluationTaskKinds.Review, StringComparer.Ordinal))
                throw new ArticleEvaluationValidationException($"Evaluation profile '{profileId}' does not support this run.");
            profiles.Add(profile);
        }
        if (request.EnableBlindCrossCheck && profiles.Skip(1).Any(profile =>
            !profile.TaskKinds.Contains(ArticleEvaluationTaskKinds.CrossCheck, StringComparer.Ordinal)))
            throw new ArticleEvaluationValidationException("Every checker profile must support blind cross-check.");
        List<int> advertisedInputLimits = profiles.Select(profile =>
        {
            if (profile.ExecutionSettings is null ||
                !profile.ExecutionSettings.TryGetValue("maximumInputBytes", out string? raw) ||
                !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < 1024)
                throw new ArticleEvaluationValidationException(
                    $"Evaluation profile '{profile.ProfileId}' has no valid maximum-input bound.");
            return parsed;
        }).ToList();
        int maximumSourceBytes = Math.Min(options.Value.MaximumSourceBytes, advertisedInputLimits.Min());

        Guid publicRunId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ArticleEvaluationRun run = new()
        {
            RunId = publicRunId,
            OwnerPersonelId = grant.SubjectPersonelId,
            ActorAuditId = grant.ActorAuditId,
            AuthorizationGrantId = grant.AuthorizationGrantId,
            DatasetVersion = ArticleEvaluationCalibrationCatalog.DatasetVersion,
            EvaluatorVersion = options.Value.EvaluatorVersion.Trim(),
            PolicyVersion = options.Value.PolicyVersion.Trim(),
            ProfilesJson = JsonSerializer.Serialize(profiles, JsonOptions),
            IncludesRealCases = request.RealCases.Count > 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        int caseOrdinal = 0;
        foreach (CalibrationCaseDefinition definition in ArticleEvaluationCalibrationCatalog.Create())
            run.Cases.Add(CreateCalibrationCase(definition, profiles, publicRunId, caseOrdinal++));
        foreach (ArticleEvaluationRealCaseRequest real in request.RealCases)
            run.Cases.Add(await CreateRealCaseAsync(run.OwnerPersonelId!, real, profiles,
                request.EnableBlindCrossCheck, maximumSourceBytes, caseOrdinal++, cancellationToken));

        run.TotalCases = run.Cases.Count;
        run.TotalWorkItems = run.Cases.Sum(value => value.WorkItems.Count);
        run.WorstCaseModelCalls = run.Cases.SelectMany(value => value.WorkItems).Sum(value => value.Phase switch
        {
            ArticleEvaluationTaskKinds.Calibration => 1,
            ArticleEvaluationTaskKinds.Review => 8,
            ArticleEvaluationTaskKinds.CrossCheck => 4,
            _ => throw new InvalidOperationException("Unknown evaluation phase.")
        });
        if (run.TotalWorkItems > 36 || run.WorstCaseModelCalls > 114)
            throw new ArticleEvaluationValidationException("The requested evaluation exceeds the bounded work or model-call budget.");
        database.ArticleEvaluationRuns.Add(run);
        await database.SaveChangesAsync(cancellationToken);
        return new(run.RunId, run.Status, run.TotalCases, run.TotalWorkItems, run.WorstCaseModelCalls,
            run.DatasetVersion, run.EvaluatorVersion, "NotValidated");
    }

    private ArticleEvaluationCase CreateCalibrationCase(
        CalibrationCaseDefinition definition,
        IReadOnlyList<ArticleEvaluationProfile> profiles,
        Guid runId,
        int ordinal)
    {
        List<ArticlePage> pages = [new(null, string.Concat(definition.Snapshot.SourceSpans.Select(value => value.Text)))];
        IReadOnlyList<ArticleSourceSpan> spans = ArticleSourceCatalog.Create(pages);
        Dictionary<string, string> sourceMap = definition.Snapshot.SourceSpans.Zip(spans)
            .ToDictionary(value => value.First.SourceId, value => value.Second.SourceId, StringComparer.Ordinal);
        List<(EvaluationClaim Claim, CalibrationReference Reference)> shuffled = definition.Snapshot.Claims
            .Join(definition.References, claim => claim.ClaimId, reference => reference.ClaimId,
                (claim, reference) => (claim, reference))
            .OrderBy(value => StableOrder(runId, definition.CaseId, value.claim.ClaimId)).ToList();
        List<EvaluationClaim> claims = [];
        List<CalibrationReference> references = [];
        for (int index = 0; index < shuffled.Count; index++)
        {
            string opaqueId = $"i{index + 1:D2}-{StableOrder(runId, definition.CaseId, index.ToString())[..6]}";
            claims.Add(new(opaqueId, shuffled[index].Claim.Text)
                { Section = shuffled[index].Claim.Section });
            references.Add(new(opaqueId, shuffled[index].Reference.ExpectedVerdict,
                shuffled[index].Reference.Derivation));
        }
        ReviewArticleRequest source = new(definition.Snapshot.Language, "abstract",
            definition.Snapshot.SourceHash, definition.Snapshot.ExtractionVersion,
            options.Value.PolicyVersion.Trim(),
            pages, 1, true, definition.Snapshot.ScopeReason) { SourceSpans = spans };
        ArticleEvaluationCase value = new()
        {
            Ordinal = ordinal,
            CaseId = definition.CaseId,
            Kind = ArticleEvaluationTaskKinds.Calibration,
            Language = source.Language,
            SourceHash = source.SourceHash,
            SourceSnapshotJson = JsonSerializer.Serialize(source, JsonOptions),
            ReferenceJson = JsonSerializer.Serialize(new
            {
                Provenance = "Mechanically authored synthetic calibration; no scientific or expert gold.",
                ImmutableSourceIds = sourceMap.Values,
                Derivations = references
            }, JsonOptions),
            ExpectedVerdictsJson = JsonSerializer.Serialize(references, JsonOptions),
            RequestPayloadJson = JsonSerializer.Serialize(new
            {
                Source = source,
                CalibrationClaims = claims.Select(claim => new ArticleEvaluationCalibrationClaim(
                    claim.ClaimId, claim.Text, claim.Section, spans.Select(span => span.SourceId).ToList())).ToList()
            }, JsonOptions)
        };
        AddProfileItems(value, profiles, ArticleEvaluationTaskKinds.Calibration);
        return value;
    }

    private async Task<ArticleEvaluationCase> CreateRealCaseAsync(
        string personelId,
        ArticleEvaluationRealCaseRequest requested,
        IReadOnlyList<ArticleEvaluationProfile> profiles,
        bool crossCheck,
        int maximumSourceBytes,
        int ordinal,
        CancellationToken cancellationToken)
    {
        string language = requested.Language.Trim();
        string? sourceIdentityHash = await CanonicalSourceIdentity.LoadAsync(
            database, requested.CanonicalWorkId, cancellationToken);
        CanonicalArticleAnalysisRun? captured = await database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Include(run => run.SavedArticleSummary)
            .Include(run => run.ArticleSourceSnapshot)!.ThenInclude(snapshot => snapshot!.Pages)
            .Include(run => run.ArticleSourceSnapshot)!.ThenInclude(snapshot => snapshot!.Spans)
            .Where(run => run.CanonicalWorkId == requested.CanonicalWorkId && run.Language == language &&
                sourceIdentityHash != null && run.SourceIdentityHash == sourceIdentityHash &&
                database.CanonicalResearcherWorks.Any(association => association.CanonicalWorkId == requested.CanonicalWorkId &&
                    association.PersonelId == personelId))
            .OrderByDescending(run => run.Id).AsSplitQuery().FirstOrDefaultAsync(cancellationToken);
        if (captured?.ArticleSourceSnapshot is null || captured.SavedArticleSummary is null)
            throw new ArticleEvaluationValidationException("A real case lacks a current saved canonical analysis and source snapshot.");
        List<ArticlePage> pages = captured.ArticleSourceSnapshot.Pages.OrderBy(value => value.Ordinal)
            .Select(value => new ArticlePage(value.PageNumber, value.Text)).ToList();
        List<ArticleSourceSpan> spans = captured.ArticleSourceSnapshot.Spans.OrderBy(value => value.Ordinal)
            .Select(value => new ArticleSourceSpan(value.SourceId, value.PageNumber, value.StartOffset, value.EndOffset, value.Text)).ToList();
        SummarizeArticleRequest? original = JsonSerializer.Deserialize<SummarizeArticleRequest>(
            captured.SavedArticleSummary.SnapshotJson, JsonOptions);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(pages, JsonOptions)))).ToLowerInvariant();
        if (original is null || original.SourceHash != hash || original.SourceHash != captured.ArticleSourceSnapshot.ExtractedTextHash ||
            original.Language != language || original.SourceKind != captured.ArticleSourceSnapshot.SourceKind ||
            original.ExtractionVersion != captured.ArticleSourceSnapshot.ExtractionVersion || original.Pages is null ||
            !original.Pages.SequenceEqual(pages) || original.SourceSpans is null || !original.SourceSpans.SequenceEqual(spans) ||
            original.TotalSourcePages < pages.Count || original.TotalSourcePages <= 0 ||
            original.TotalSourcePages > pages.Count && !original.IsPartial ||
            original.SourceKind == "abstract" && !original.IsPartial ||
            original.IsPartial && string.IsNullOrWhiteSpace(original.ScopeReason) || original.ScopeReason?.Length > 4000 ||
            !ArticleSourceCatalog.IsValid(pages, spans, original.SourceKind))
            throw new ArticleEvaluationValidationException("A real case source snapshot failed immutable catalog validation.");
        ReviewArticleRequest source = new(language, original.SourceKind, hash, original.ExtractionVersion,
            options.Value.PolicyVersion.Trim(),
            pages, original.TotalSourcePages, original.IsPartial, original.ScopeReason) { SourceSpans = spans };
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(source, JsonOptions)) > maximumSourceBytes)
            throw new ArticleEvaluationValidationException("A real case exceeds the configured immutable source budget.");
        ArticleEvaluationCase value = new()
        {
            Ordinal = ordinal,
            CaseId = $"real-{ordinal:D2}",
            Kind = ArticleEvaluationTaskKinds.Review,
            CanonicalWorkId = captured.CanonicalWorkId,
            BaseAnalysisRunId = captured.Id,
            Language = language,
            SourceHash = hash,
            SourceSnapshotJson = JsonSerializer.Serialize(source, JsonOptions),
            ReferenceJson = JsonSerializer.Serialize(new
            {
                BaseAnalysisRunId = captured.Id,
                captured.ArticleSourceSnapshotId,
                OriginalCoverage = new { original.TotalSourcePages, original.IsPartial, original.ScopeReason },
                Provenance = "Pinned historical canonical source; no expert scientific gold."
            }, JsonOptions),
            RequestPayloadJson = JsonSerializer.Serialize(new { Source = source }, JsonOptions)
        };
        AddProfileItems(value, profiles, ArticleEvaluationTaskKinds.Review);
        if (crossCheck)
        {
            ArticleEvaluationWorkItem primary = value.WorkItems[0];
            foreach (ArticleEvaluationProfile checker in profiles.Skip(1).Take(2))
            {
                value.WorkItems.Add(CreateItem(value.WorkItems.Count, checker,
                    ArticleEvaluationTaskKinds.CrossCheck, primary));
            }
        }
        return value;
    }

    private static void AddProfileItems(ArticleEvaluationCase evaluationCase,
        IReadOnlyList<ArticleEvaluationProfile> profiles, string phase)
    {
        foreach (ArticleEvaluationProfile profile in profiles)
            evaluationCase.WorkItems.Add(CreateItem(evaluationCase.WorkItems.Count, profile, phase, null));
    }

    private static ArticleEvaluationWorkItem CreateItem(
        int ordinal, ArticleEvaluationProfile profile, string phase, ArticleEvaluationWorkItem? dependency) => new()
    {
        Ordinal = ordinal,
        Phase = phase,
        ProfileId = profile.ProfileId,
        ProfileFingerprint = profile.SettingsFingerprint,
        ProfileSnapshotJson = JsonSerializer.Serialize(profile, JsonOptions),
        DependsOn = dependency
    };

    private static string StableOrder(Guid runId, string caseId, string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{runId:N}:{caseId}:{value}"))).ToLowerInvariant();
}

public sealed class ArticleEvaluationValidationException(string message) : Exception(message);
