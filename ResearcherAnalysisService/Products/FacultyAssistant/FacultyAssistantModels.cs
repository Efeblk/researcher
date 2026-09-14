namespace ResearcherAnalysisService.Products.FacultyAssistant;

public sealed class FacultyAssistantContextVersion
{
    public long Id { get; set; }
    public string PersonelId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string ContextJson { get; set; } = string.Empty;
    public string ContextFingerprint { get; set; } = string.Empty;
    public string CreatedByActorId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FacultyAssistantRun
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public string PersonelId { get; set; } = string.Empty;
    public string ActorAuditId { get; set; } = string.Empty;
    public string AuthorizationGrantId { get; set; } = string.Empty;
    public Guid ClientRequestId { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public long? ContextVersionId { get; set; }
    public string RetrievalPolicyVersion { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int AttemptCount { get; set; }
    public Guid? AttemptToken { get; set; }
    public DateTimeOffset? AttemptStartedAt { get; set; }
    public string RequestJson { get; set; } = string.Empty;
    public string RetrievalManifestJson { get; set; } = string.Empty;
    public string? AuthorizedInputJson { get; set; }
    public string? InputFingerprint { get; set; }
    public string? ReportJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
