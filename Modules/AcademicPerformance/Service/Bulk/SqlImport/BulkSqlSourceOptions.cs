namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.SqlImport;

public sealed class BulkSqlSourceOptions
{
    public bool Enabled { get; set; }
    public string Query { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public string SourceResearcherIdColumn { get; set; } = "ResearcherId";
    public string OrcidColumn { get; set; } = "Orcid";
    public string GoogleScholarIdColumn { get; set; } = "GoogleScholarId";
    public string WebOfScienceIdColumn { get; set; } = "WebOfScienceID";
}
