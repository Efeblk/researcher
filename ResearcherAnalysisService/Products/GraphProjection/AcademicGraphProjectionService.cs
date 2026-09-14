using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;

namespace ResearcherAnalysisService.Products.GraphProjection;

public sealed class AcademicGraphProjectionService(AnalysisDbContext database)
{
    private const int MaximumExportedSpans = 25000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AcademicGraphProjectionBundle?> ExportAsync(
        string personelId,
        IReadOnlyCollection<int> canonicalWorkIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonicalWorkIds);
        int[] ids = canonicalWorkIds.Where(id => id > 0).Distinct().Order().ToArray();
        if (ids.Length is < 20 or > 50)
            throw new ArgumentException("Select between 20 and 50 distinct canonical works.", nameof(canonicalWorkIds));
        int authorized = await database.CanonicalResearcherWorks.AsNoTracking()
            .CountAsync(value => value.PersonelId == personelId && ids.Contains(value.CanonicalWorkId),
                cancellationToken);
        if (authorized != ids.Length)
            return null;

        List<WorkRow> works = await database.CanonicalWorks.AsNoTracking()
            .Where(value => ids.Contains(value.Id)).OrderBy(value => value.Id)
            .Select(value => new WorkRow(value.Id, value.NormalizedDoi,
                value.HasRetractionObservation)).ToListAsync(cancellationToken);
        List<RunRow> runs = await database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Where(value => ids.Contains(value.CanonicalWorkId) &&
                !database.CanonicalArticleAnalysisRuns.Any(later =>
                    later.CanonicalWorkId == value.CanonicalWorkId &&
                    later.Language == value.Language && later.Id > value.Id))
            .OrderBy(value => value.CanonicalWorkId).ThenBy(value => value.Language)
            .ThenBy(value => value.Id)
            .Select(value => new RunRow(value.Id, value.CanonicalWorkId,
                value.ArticleSourceSnapshotId, value.Language, value.PolicyVersion,
                value.PromptVersion, value.IsPartial)).ToListAsync(cancellationToken);
        long[] sourceIds = runs.Select(value => value.SourceSnapshotId).Distinct().ToArray();
        List<SourceRow> sources = await database.ArticleSourceSnapshots.AsNoTracking()
            .Where(value => sourceIds.Contains(value.Id)).OrderBy(value => value.Id)
            .Select(value => new SourceRow(value.Id, value.CanonicalWorkId,
                value.ExtractedTextHash, value.SourceKind, value.ExtractionVersion))
            .ToListAsync(cancellationToken);
        List<SpanRow> spans = await database.ArticleSourceSpans.AsNoTracking()
            .Where(value => sourceIds.Contains(value.ArticleSourceSnapshotId))
            .OrderBy(value => value.ArticleSourceSnapshotId).ThenBy(value => value.Ordinal)
            .ThenBy(value => value.Id).Select(value => new SpanRow(value.Id,
                value.ArticleSourceSnapshotId, value.SourceId, value.Ordinal, value.PageNumber,
                value.StartOffset, value.EndOffset, value.Text))
            .Take(MaximumExportedSpans + 1).ToListAsync(cancellationToken);
        if (spans.Count > MaximumExportedSpans)
            throw new InvalidOperationException("The selected evidence exceeds the bounded graph export size.");
        long[] runIds = runs.Select(value => value.Id).ToArray();
        List<ClaimRow> claims = await database.CanonicalArticleClaims.AsNoTracking()
            .Where(value => runIds.Contains(value.CanonicalArticleAnalysisRunId))
            .OrderBy(value => value.CanonicalArticleAnalysisRunId).ThenBy(value => value.SectionOrder)
            .ThenBy(value => value.Ordinal).ThenBy(value => value.Id)
            .Select(value => new ClaimRow(value.Id, value.CanonicalArticleAnalysisRunId,
                value.Section, value.Ordinal, value.Text)).ToListAsync(cancellationToken);
        long[] claimIds = claims.Select(value => value.Id).ToArray();
        List<EvidenceRow> evidence = await database.CanonicalArticleClaimEvidence.AsNoTracking()
            .Where(value => claimIds.Contains(value.CanonicalArticleClaimId))
            .OrderBy(value => value.CanonicalArticleClaimId).ThenBy(value => value.Ordinal)
            .ThenBy(value => value.Id)
            .Select(value => new EvidenceRow(value.Id, value.CanonicalArticleClaimId,
                value.ArticleSourceSpanId)).ToListAsync(cancellationToken);

        AcademicGraphProjectionBundle bundle = Build(works, runs, sources, spans, claims, evidence);
        AcademicGraphProjectionValidator.Validate(bundle);
        return bundle;
    }

    internal static AcademicGraphProjectionBundle Build(
        IReadOnlyCollection<WorkRow> works,
        IReadOnlyCollection<RunRow> runs,
        IReadOnlyCollection<SourceRow> sources,
        IReadOnlyCollection<SpanRow> spans,
        IReadOnlyCollection<ClaimRow> claims,
        IReadOnlyCollection<EvidenceRow> evidence)
    {
        Dictionary<long, long> runSources = runs.ToDictionary(value => value.Id,
            value => value.SourceSnapshotId);
        Dictionary<long, long> claimRuns = claims.ToDictionary(value => value.Id, value => value.RunId);
        Dictionary<long, long> spanSources = spans.ToDictionary(value => value.Id,
            value => value.SourceSnapshotId);
        if (evidence.Any(value => !claimRuns.TryGetValue(value.ClaimId, out long runId) ||
            !runSources.TryGetValue(runId, out long runSourceId) ||
            !spanSources.TryGetValue(value.SpanId, out long spanSourceId) ||
            runSourceId != spanSourceId))
            throw new InvalidDataException("Claim evidence must belong to the source snapshot used by its analysis run.");
        List<AcademicGraphNodeDto> nodes = [];
        nodes.AddRange(works.Select(value => Node($"work:{value.Id}", "CanonicalWork",
            ("canonicalWorkId", value.Id.ToString()), ("normalizedDoi", value.NormalizedDoi),
            ("hasRetractionObservation", value.HasRetractionObservation.ToString()))));
        nodes.AddRange(sources.Select(value => Node($"source:{value.Id}", "SourceSnapshot",
            ("sourceSnapshotId", value.Id.ToString()), ("canonicalWorkId", value.CanonicalWorkId.ToString()),
            ("extractedTextHash", value.ExtractedTextHash), ("sourceKind", value.SourceKind),
            ("extractionVersion", value.ExtractionVersion))));
        nodes.AddRange(spans.Select(value => Node($"span:{value.Id}", "EvidenceSpan",
            ("spanId", value.Id.ToString()), ("sourceSnapshotId", value.SourceSnapshotId.ToString()),
            ("sourceId", value.SourceId), ("ordinal", value.Ordinal.ToString()),
            ("pageNumber", value.PageNumber?.ToString()), ("startOffset", value.StartOffset.ToString()),
            ("endOffset", value.EndOffset.ToString()), ("exactText", value.Text))));
        nodes.AddRange(runs.Select(value => Node($"run:{value.Id}", "AnalysisRun",
            ("analysisRunId", value.Id.ToString()), ("canonicalWorkId", value.CanonicalWorkId.ToString()),
            ("language", value.Language), ("policyVersion", value.PolicyVersion),
            ("promptVersion", value.PromptVersion), ("isPartial", value.IsPartial.ToString()))));
        nodes.AddRange(claims.Select(value => Node($"claim:{value.Id}", "Claim",
            ("claimId", value.Id.ToString()), ("section", value.Section),
            ("ordinal", value.Ordinal.ToString()), ("text", value.Text))));

        List<AcademicGraphRelationshipDto> relationships = [];
        relationships.AddRange(sources.Select(value => Edge($"work-source:{value.Id}",
            $"work:{value.CanonicalWorkId}", $"source:{value.Id}", "HAS_SOURCE")));
        relationships.AddRange(spans.Select(value => Edge($"source-span:{value.Id}",
            $"source:{value.SourceSnapshotId}", $"span:{value.Id}", "HAS_SPAN")));
        relationships.AddRange(runs.Select(value => Edge($"work-run:{value.Id}",
            $"work:{value.CanonicalWorkId}", $"run:{value.Id}", "HAS_ANALYSIS")));
        relationships.AddRange(runs.Select(value => Edge($"run-source:{value.Id}",
            $"run:{value.Id}", $"source:{value.SourceSnapshotId}", "USED_SOURCE")));
        relationships.AddRange(claims.Select(value => Edge($"run-claim:{value.Id}",
            $"run:{value.RunId}", $"claim:{value.Id}", "ASSERTS")));
        relationships.AddRange(evidence.Select(value => Edge($"claim-evidence:{value.Id}",
            $"claim:{value.ClaimId}", $"span:{value.SpanId}", "CITES_EVIDENCE",
            [$"span:{value.SpanId}"])));

        nodes = nodes.OrderBy(value => value.Type, StringComparer.Ordinal)
            .ThenBy(value => value.Id, StringComparer.Ordinal).ToList();
        relationships = relationships.OrderBy(value => value.Type, StringComparer.Ordinal)
            .ThenBy(value => value.Id, StringComparer.Ordinal).ToList();
        string content = JsonSerializer.Serialize(new { nodes, relationships }, JsonOptions);
        string hash = Hash(content);
        return new()
        {
            ProjectionId = $"academic-public-evidence-graph-v1:{hash}",
            ContentHash = hash,
            Nodes = nodes,
            Relationships = relationships,
            RebuildCommands = Commands()
        };
    }

    internal static GraphRetrievalEvaluationDto Evaluate(
        IReadOnlyCollection<(IReadOnlyCollection<string> SqlEvidenceIds,
            IReadOnlyCollection<string> GraphEvidenceIds)> queries)
    {
        int sqlCount = queries.Sum(value => value.SqlEvidenceIds.Count);
        int graphCount = queries.Sum(value => value.GraphEvidenceIds.Count);
        int missing = queries.Sum(value => value.SqlEvidenceIds.Except(
            value.GraphEvidenceIds, StringComparer.Ordinal).Count());
        int unexpected = queries.Sum(value => value.GraphEvidenceIds.Except(
            value.SqlEvidenceIds, StringComparer.Ordinal).Count());
        int exact = queries.Count(value => value.SqlEvidenceIds.ToHashSet(StringComparer.Ordinal)
            .SetEquals(value.GraphEvidenceIds));
        return new(queries.Count, sqlCount, graphCount, missing, unexpected,
            queries.Count == 0 ? null : (decimal)exact / queries.Count);
    }

    private static AcademicGraphNodeDto Node(string id, string type,
        params (string Key, string? Value)[] properties) => new()
    {
        Id = id,
        Type = type,
        Properties = properties.ToDictionary(value => value.Key, value => value.Value,
            StringComparer.Ordinal)
    };

    private static AcademicGraphRelationshipDto Edge(string id, string from, string to,
        string type, List<string>? evidence = null) => new()
    {
        Id = id, FromId = from, ToId = to, Type = type, EvidenceIds = evidence ?? []
    };

    private static List<ParameterizedCypherCommandDto> Commands() =>
    [
        new() { Name = "delete-projection", Parameter = "projectionId", Cypher =
            "MATCH (n:AcademicEvidence {projectionId: $projectionId}) DETACH DELETE n" },
        new() { Name = "upsert-nodes-by-type", Parameter = "nodes", Cypher =
            "UNWIND $nodes AS row MERGE (n:AcademicEvidence {projectionId: $projectionId, id: row.id}) SET n += row.properties, n.type = row.type" },
        new() { Name = "upsert-relationships", Parameter = "relationships", Cypher =
            "UNWIND $relationships AS row MATCH (a:AcademicEvidence {id: row.fromId, projectionId: $projectionId}), (b:AcademicEvidence {id: row.toId, projectionId: $projectionId}) MERGE (a)-[r:ACADEMIC_RELATION {id: row.id}]->(b) SET r.type = row.type, r.status = row.status, r.evidenceIds = row.evidenceIds, r.projectionId = $projectionId" }
    ];

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal sealed record WorkRow(int Id, string? NormalizedDoi, bool HasRetractionObservation);
    internal sealed record RunRow(long Id, int CanonicalWorkId, long SourceSnapshotId,
        string Language, string? PolicyVersion, string PromptVersion, bool IsPartial);
    internal sealed record SourceRow(long Id, int CanonicalWorkId, string ExtractedTextHash,
        string SourceKind, string ExtractionVersion);
    internal sealed record SpanRow(long Id, long SourceSnapshotId, string SourceId, int Ordinal,
        int? PageNumber, int StartOffset, int EndOffset, string Text);
    internal sealed record ClaimRow(long Id, long RunId, string Section, int Ordinal, string Text);
    internal sealed record EvidenceRow(long Id, long ClaimId, long SpanId);
}

public static class AcademicGraphProjectionValidator
{
    private static readonly HashSet<string> AllowedNodeTypes =
        ["CanonicalWork", "SourceSnapshot", "EvidenceSpan", "AnalysisRun", "Claim"];
    private static readonly HashSet<string> AllowedRelationshipTypes =
        ["HAS_SOURCE", "HAS_SPAN", "HAS_ANALYSIS", "USED_SOURCE", "ASSERTS", "CITES_EVIDENCE"];
    private static readonly string[] ForbiddenPropertyFragments =
        ["personel", "researcher", "department", "faculty", "tcKimlik", "actorAudit"];
    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedProperties =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["CanonicalWork"] = ["canonicalWorkId", "normalizedDoi", "hasRetractionObservation"],
            ["SourceSnapshot"] = ["sourceSnapshotId", "canonicalWorkId", "extractedTextHash", "sourceKind", "extractionVersion"],
            ["EvidenceSpan"] = ["spanId", "sourceSnapshotId", "sourceId", "ordinal", "pageNumber", "startOffset", "endOffset", "exactText"],
            ["AnalysisRun"] = ["analysisRunId", "canonicalWorkId", "language", "policyVersion", "promptVersion", "isPartial"],
            ["Claim"] = ["claimId", "section", "ordinal", "text"]
        };

    public static void Validate(AcademicGraphProjectionBundle bundle)
    {
        if (bundle.Nodes.Any(value => !AllowedNodeTypes.Contains(value.Type)) ||
            bundle.Relationships.Any(value => !AllowedRelationshipTypes.Contains(value.Type)))
            throw new InvalidDataException("The graph projection contains an unsupported type.");
        if (bundle.Nodes.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() !=
            bundle.Nodes.Count)
            throw new InvalidDataException("Graph node IDs must be unique.");
        HashSet<string> nodeIds = bundle.Nodes.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> spanIds = bundle.Nodes.Where(value => value.Type == "EvidenceSpan")
            .Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        foreach (AcademicGraphNodeDto node in bundle.Nodes)
        {
            if (!AllowedProperties.TryGetValue(node.Type, out HashSet<string>? allowed) ||
                node.Properties.Keys.Any(key => !allowed.Contains(key)) ||
                node.Properties.Keys.Any(key => ForbiddenPropertyFragments.Any(fragment =>
                key.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("The graph projection contains a private property.");
        }
        foreach (AcademicGraphRelationshipDto relationship in bundle.Relationships)
        {
            if (!nodeIds.Contains(relationship.FromId) || !nodeIds.Contains(relationship.ToId))
                throw new InvalidDataException("A graph relationship endpoint is missing.");
            if (relationship.Status != "AcceptedDeterministic" ||
                relationship.EvidenceIds.Any(id => !spanIds.Contains(id)))
                throw new InvalidDataException("A graph relationship has invalid evidence status.");
        }
    }

    public static AcademicGraphRelationshipDto QuarantineProposed(
        ProposedGraphRelationshipDto proposed,
        AcademicGraphProjectionBundle bundle)
    {
        HashSet<string> nodes = bundle.Nodes.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> spans = bundle.Nodes.Where(value => value.Type == "EvidenceSpan")
            .Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        if (!nodes.Contains(proposed.FromId) || !nodes.Contains(proposed.ToId) ||
            proposed.EvidenceIds.Any(id => !spans.Contains(id)))
            throw new InvalidDataException("A proposed relationship references an unknown endpoint or evidence span.");
        return new()
        {
            Id = $"proposed:{proposed.FromId}:{proposed.Type}:{proposed.ToId}",
            FromId = proposed.FromId,
            ToId = proposed.ToId,
            Type = "PROPOSED_RELATION",
            Status = "PendingEvidenceValidation",
            EvidenceIds = proposed.EvidenceIds.Distinct(StringComparer.Ordinal).Order().ToList()
        };
    }
}
