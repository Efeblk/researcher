namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

public sealed class AcademicWorkResearchContext
{
    public int AcademicWorkId { get; set; }
    public AcademicWork? AcademicWork { get; set; } = null;
    public string Provider { get; set; } = "OpenAlex";
    public string? SourceWorkId { get; set; } = null;
    public string ParserVersion { get; set; } = string.Empty;
    public string PayloadFingerprint { get; set; } = string.Empty;
    public DateTime SourceSyncedAt { get; set; }
    public DateTime? ProviderUpdatedAt { get; set; } = null;
    public int? SourcePublicationYear { get; set; } = null;
    public string? RawType { get; set; } = null;
    public string? PrimarySourceType { get; set; } = null;
    public decimal? Fwci { get; set; } = null;
    public decimal? CitationNormalizedPercentile { get; set; } = null;
    public bool? IsInTopOnePercent { get; set; } = null;
    public bool? IsInTopTenPercent { get; set; } = null;
    public string ParseQuality { get; set; } = "Unknown";
    public string? ParseQualityReason { get; set; } = null;
    public string PrimaryTopicQuality { get; set; } = "Unknown";
    public string? PrimaryTopicQualityReason { get; set; } = null;
    public string ValueQualityJson { get; set; } = "{}";
    public List<AcademicWorkTopic> Topics { get; set; } = [];
}

public sealed class AcademicWorkTopic
{
    public long Id { get; set; }
    public int AcademicWorkId { get; set; }
    public AcademicWorkResearchContext? ResearchContext { get; set; } = null;
    public string TopicId { get; set; } = string.Empty;
    public string? TopicName { get; set; } = null;
    public string? SubfieldId { get; set; } = null;
    public string? SubfieldName { get; set; } = null;
    public string? FieldId { get; set; } = null;
    public string? FieldName { get; set; } = null;
    public string? DomainId { get; set; } = null;
    public string? DomainName { get; set; } = null;
    public int OriginalRank { get; set; }
    public double? AssignmentScore { get; set; } = null;
    public string ScoreQuality { get; set; } = "Unknown";
    public string? ScoreQualityReason { get; set; } = null;
    public bool IsPrimary { get; set; }
}
