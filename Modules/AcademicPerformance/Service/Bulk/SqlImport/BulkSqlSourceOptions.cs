namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.SqlImport;

public sealed class BulkSqlSourceOptions
{
    public bool Enabled { get; set; }
    public string Query { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public string PersonelIdColumn { get; set; } = "PersonelID";
    public string OrcidColumn { get; set; } = "ORCID";
    public string GoogleScholarIdColumn { get; set; } = "ScholarID";
    public string WebOfScienceIdColumn { get; set; } = "ResearcherID";
    public string ScopusIdColumn { get; set; } = "ScopusID";
}
