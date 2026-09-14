using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.Api.Contracts;

public sealed class GraphProjectionRequest
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;

    [Required, MinLength(20), MaxLength(50)]
    public List<int> CanonicalWorkIds { get; set; } = [];
}

public sealed class AcademicGraphProjectionBundle
{
    public string SchemaVersion { get; set; } = "academic-public-evidence-graph-v1";
    public string ProjectionId { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string Status { get; set; } = "ExportReady";
    public string Neo4jExecutionStatus { get; set; } = "NotRun";
    public string AdoptionEvaluationStatus { get; set; } = "NotRun";
    public List<AcademicGraphNodeDto> Nodes { get; set; } = [];
    public List<AcademicGraphRelationshipDto> Relationships { get; set; } = [];
    public List<ParameterizedCypherCommandDto> RebuildCommands { get; set; } = [];
}

public sealed class AcademicGraphNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, string?> Properties { get; set; } = [];
}

public sealed class AcademicGraphRelationshipDto
{
    public string Id { get; set; } = string.Empty;
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = "AcceptedDeterministic";
    public List<string> EvidenceIds { get; set; } = [];
}

public sealed class ParameterizedCypherCommandDto
{
    public string Name { get; set; } = string.Empty;
    public string Cypher { get; set; } = string.Empty;
    public string Parameter { get; set; } = string.Empty;
}

public sealed class ProposedGraphRelationshipDto
{
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> EvidenceIds { get; set; } = [];
}

public sealed record GraphRetrievalEvaluationDto(
    int QueryCount,
    int SqlEvidenceCount,
    int GraphEvidenceCount,
    int MissingFromGraphCount,
    int UnexpectedGraphCount,
    decimal? ExactSetAgreementProportion);
