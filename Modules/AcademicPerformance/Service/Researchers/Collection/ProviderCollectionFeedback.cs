namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

public sealed class ProviderCollectionFeedback
{
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public string Unit { get; set; } = "publication";
    public int RetrievedCount { get; set; }
    public int? RetainedCount { get; set; } = null;
    public int? ExpectedCount { get; set; } = null;
    public List<ProviderCollectionReason> Reasons { get; set; } = [];
}

public sealed class ProviderCollectionReason
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? AffectedCount { get; set; } = null;
}
