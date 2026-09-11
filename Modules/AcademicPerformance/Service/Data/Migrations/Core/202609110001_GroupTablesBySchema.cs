using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609110001, "Group academic tables into SQL Server schemas")]
public sealed class GroupTablesBySchema : Migration
{
    private static readonly (string Schema, string[] Tables)[] Groups =
    [
        ("core", ["Researchers", "AcademicWorks", "AcademicWorkSources", "PublicationSummaries",
            "PublicationDisplayApprovals"]),
        ("orcid", ["OrcidProfiles", "OrcidWorks"]),
        ("googlescholar", ["GoogleScholarProfiles", "GoogleScholarWorks"]),
        ("openalex", ["OpenAlexProfiles", "OpenAlexWorks"]),
        ("wos", ["WebOfScienceProfiles", "WebOfScienceWorks", "WebOfSciencePeerReviews"]),
        ("yoksis", ["YoksisRecords"]),
        ("trdizin", ["TrDizinProfiles", "TrDizinWorks"]),
        ("crossref", ["CrossrefWorks"]),
        ("semanticscholar", ["SemanticScholarPapers", "SemanticScholarCitations",
            "SemanticScholarCitationContexts"]),
        ("analysis", ["ArticleSummaries", "ResearcherAnalyses"]),
        ("bulk", ["BulkCollectionBatches", "BulkCollectionJobs"]),
        ("integrations", ["ProviderRequestBudgets", "ProviderStatusObservations"])
    ];

    public override void Up()
    {
        foreach ((string schema, string[] tables) in Groups)
        {
            Execute.Sql($"IF SCHEMA_ID(N'{schema}') IS NULL EXEC(N'CREATE SCHEMA [{schema}] AUTHORIZATION [dbo]');");
            foreach (string table in tables)
                Transfer("dbo", schema, table);
        }
    }

    public override void Down()
    {
        foreach ((string schema, string[] tables) in Groups.Reverse())
            foreach (string table in tables.Reverse())
                Transfer(schema, "dbo", table);
    }

    private void Transfer(string sourceSchema, string targetSchema, string table) =>
        Execute.Sql($"ALTER SCHEMA [{targetSchema}] TRANSFER [{sourceSchema}].[{table}];");
}
