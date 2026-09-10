using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609100001, "Materialize provider metrics on researchers")]
public sealed class AddResearcherProviderMetrics : Migration
{
    private static readonly string[] Columns =
    [
        "WosCitationCount", "WosHIndex", "WosDocumentsCount", "WosMetricsUpdatedAt",
        "OpenAlexCitationCount", "OpenAlexHIndex", "OpenAlexI10Index",
        "OpenAlexDocumentsCount", "OpenAlexTwoYearMeanCitedness", "OpenAlexMetricsUpdatedAt",
        "ScholarCitationCount", "ScholarHIndex", "ScholarI10Index", "ScholarDocumentsCount",
        "ScholarCitationCountRecent", "ScholarHIndexRecent", "ScholarI10IndexRecent",
        "ScholarMetricsSinceYear", "ScholarMetricsUpdatedAt"
    ];

    public override void Up()
    {
        Alter.Table("Researchers")
            .AddColumn("WosCitationCount").AsInt32().Nullable()
            .AddColumn("WosHIndex").AsInt32().Nullable()
            .AddColumn("WosDocumentsCount").AsInt32().Nullable()
            .AddColumn("WosMetricsUpdatedAt").AsDateTime2().Nullable()
            .AddColumn("OpenAlexCitationCount").AsInt32().Nullable()
            .AddColumn("OpenAlexHIndex").AsInt32().Nullable()
            .AddColumn("OpenAlexI10Index").AsInt32().Nullable()
            .AddColumn("OpenAlexDocumentsCount").AsInt32().Nullable()
            .AddColumn("OpenAlexTwoYearMeanCitedness").AsDecimal(18, 4).Nullable()
            .AddColumn("OpenAlexMetricsUpdatedAt").AsDateTime2().Nullable()
            .AddColumn("ScholarCitationCount").AsInt32().Nullable()
            .AddColumn("ScholarHIndex").AsInt32().Nullable()
            .AddColumn("ScholarI10Index").AsInt32().Nullable()
            .AddColumn("ScholarDocumentsCount").AsInt32().Nullable()
            .AddColumn("ScholarCitationCountRecent").AsInt32().Nullable()
            .AddColumn("ScholarHIndexRecent").AsInt32().Nullable()
            .AddColumn("ScholarI10IndexRecent").AsInt32().Nullable()
            .AddColumn("ScholarMetricsSinceYear").AsInt32().Nullable()
            .AddColumn("ScholarMetricsUpdatedAt").AsDateTime2().Nullable();

        Execute.Sql("""
            UPDATE researcher
            SET WosCitationCount = profile.TotalTimesCited,
                WosHIndex = profile.HIndex,
                WosDocumentsCount = profile.DocumentsCount,
                WosMetricsUpdatedAt = profile.LastUpdatedAt
            FROM Researchers researcher
            JOIN WebOfScienceProfiles profile ON profile.PersonelID = researcher.PersonelID;

            UPDATE researcher
            SET OpenAlexCitationCount = profile.CitedByCount,
                OpenAlexHIndex = profile.HIndex,
                OpenAlexI10Index = profile.I10Index,
                OpenAlexDocumentsCount = profile.WorksCount,
                OpenAlexTwoYearMeanCitedness = profile.TwoYearMeanCitedness,
                OpenAlexMetricsUpdatedAt = profile.LastUpdatedAt
            FROM Researchers researcher
            JOIN OpenAlexProfiles profile ON profile.PersonelID = researcher.PersonelID;

            UPDATE researcher
            SET ScholarCitationCount = profile.CitationCount,
                ScholarHIndex = profile.HIndex,
                ScholarI10Index = profile.I10Index,
                ScholarDocumentsCount = profile.DocumentsCount,
                ScholarCitationCountRecent = profile.CitationCountRecent,
                ScholarHIndexRecent = profile.HIndexRecent,
                ScholarI10IndexRecent = profile.I10IndexRecent,
                ScholarMetricsSinceYear = profile.MetricsSinceYear,
                ScholarMetricsUpdatedAt = profile.LastUpdatedAt
            FROM Researchers researcher
            JOIN GoogleScholarProfiles profile ON profile.PersonelID = researcher.PersonelID;
            """);
    }

    public override void Down()
    {
        foreach (string column in Columns.Reverse())
            Delete.Column(column).FromTable("Researchers");
    }
}
