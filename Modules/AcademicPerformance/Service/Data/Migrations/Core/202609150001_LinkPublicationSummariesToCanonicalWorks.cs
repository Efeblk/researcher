using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609150001, "Link publication summaries to canonical works")]
public sealed class LinkPublicationSummariesToCanonicalWorks : Migration
{
    public override void Up()
    {
        Alter.Table("PublicationSummaries").InSchema("core")
            .AddColumn("CanonicalWorkId").AsInt32().Nullable();
        Create.ForeignKey("FK_PublicationSummaries_CanonicalWorks_CanonicalWorkId")
            .FromTable("PublicationSummaries").InSchema("core")
            .ForeignColumn("CanonicalWorkId")
            .ToTable("CanonicalWorks").InSchema("core")
            .PrimaryColumn("Id")
            .OnDelete(System.Data.Rule.None);
        Execute.Sql("""
            CREATE UNIQUE INDEX [UX_PublicationSummaries_PersonelID_CanonicalWorkId]
                ON [core].[PublicationSummaries]([PersonelID], [CanonicalWorkId])
                WHERE [CanonicalWorkId] IS NOT NULL;
            """);
    }

    public override void Down()
    {
        Delete.Index("UX_PublicationSummaries_PersonelID_CanonicalWorkId")
            .OnTable("PublicationSummaries").InSchema("core");
        Delete.ForeignKey("FK_PublicationSummaries_CanonicalWorks_CanonicalWorkId")
            .OnTable("PublicationSummaries").InSchema("core");
        Delete.Column("CanonicalWorkId").FromTable("PublicationSummaries").InSchema("core");
    }
}
