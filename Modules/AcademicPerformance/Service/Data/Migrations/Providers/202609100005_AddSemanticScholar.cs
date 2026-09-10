using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Providers;

[Migration(202609100005)]
public sealed class AddSemanticScholar : Migration
{
    public override void Up()
    {
        Create.Table("SemanticScholarPapers")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity().WithColumn("NormalizedDoi").AsString(500).NotNullable()
            .WithColumn("PaperId").AsString(100).Nullable().WithColumn("Found").AsBoolean().NotNullable()
            .WithColumn("FetchedAt").AsDateTime2().NotNullable().WithColumn("CitationTotal").AsInt32().Nullable()
            .WithColumn("CitationsFetched").AsInt32().NotNullable().WithColumn("CitationsComplete").AsBoolean().NotNullable()
            .WithColumn("Title").AsString(2000).Nullable().WithColumn("Abstract").AsString(int.MaxValue).Nullable()
            .WithColumn("AuthorsJson").AsString(int.MaxValue).Nullable().WithColumn("Year").AsInt32().Nullable()
            .WithColumn("Venue").AsString(1000).Nullable().WithColumn("PublicationDate").AsDateTime2().Nullable().WithColumn("JournalJson").AsString(int.MaxValue).Nullable().WithColumn("PublicationTypesJson").AsString(int.MaxValue).Nullable()
            .WithColumn("FieldsOfStudyJson").AsString(int.MaxValue).Nullable().WithColumn("OpenAccessPdfJson").AsString(int.MaxValue).Nullable()
            .WithColumn("CitationCount").AsInt32().Nullable().WithColumn("ReferenceCount").AsInt32().Nullable()
            .WithColumn("InfluentialCitationCount").AsInt32().Nullable().WithColumn("Url").AsString(2000).Nullable()
            .WithColumn("TldrJson").AsString(int.MaxValue).Nullable().WithColumn("TextAvailability").AsString(100).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable();
        Create.UniqueConstraint("UQ_SemanticScholarPapers_Doi").OnTable("SemanticScholarPapers").Column("NormalizedDoi");
        Execute.Sql("CREATE UNIQUE INDEX [UX_SemanticScholarPapers_PaperId] ON [SemanticScholarPapers] ([PaperId]) WHERE [PaperId] IS NOT NULL");
        Create.Table("SemanticScholarCitations").WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TargetPaperId").AsInt32().NotNullable().ForeignKey("SemanticScholarPapers", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("CitingPaperId").AsString(100).NotNullable().WithColumn("CitingDoi").AsString(500).Nullable()
            .WithColumn("CitingTitle").AsString(2000).Nullable().WithColumn("CitingAuthorsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("IsInfluential").AsBoolean().Nullable().WithColumn("IntentsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable();
        Create.UniqueConstraint("UQ_SemanticScholarCitations_Target_Citing").OnTable("SemanticScholarCitations").Columns("TargetPaperId", "CitingPaperId");
        Create.Table("SemanticScholarCitationContexts").WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("CitationId").AsInt32().NotNullable().ForeignKey("SemanticScholarCitations", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Ordinal").AsInt32().NotNullable().WithColumn("Context").AsString(int.MaxValue).NotNullable()
            .WithColumn("IntentsJson").AsString(int.MaxValue).Nullable();
        Create.UniqueConstraint("UQ_SemanticScholarContexts_Citation_Ordinal").OnTable("SemanticScholarCitationContexts").Columns("CitationId", "Ordinal");
    }
    public override void Down() { Delete.Table("SemanticScholarCitationContexts"); Delete.Table("SemanticScholarCitations"); Delete.Table("SemanticScholarPapers"); }
}
