namespace AcademicCollectorDemo.Modules.AcademicPerformance.HrDossiers;

public sealed class HrEvidenceDossier
{
    public long Id { get; set; }
    public string PersonelId { get; set; } = string.Empty;
    public string CreatedByActorId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public long? PublicationMetricSnapshotId { get; set; }
    public string InputFingerprint { get; set; } = string.Empty;
    public string InputManifestJson { get; set; } = string.Empty;
    public string DossierJson { get; set; } = string.Empty;
    public List<HrDossierReviewAction> ReviewActions { get; set; } = [];
}

public sealed class HrDossierReviewAction
{
    public long Id { get; set; }
    public long DossierId { get; set; }
    public HrEvidenceDossier? Dossier { get; set; }
    public string ActorAuditId { get; set; } = string.Empty;
    public Guid ClientRequestId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string? EvidenceReference { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
