using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609140002, "Add the collector-owned normalized-data change feed")]
public sealed class AddCollectionChanges : Migration
{
    public override void Up()
    {
        Create.Table("CollectionChanges").InSchema("core")
            .WithColumn("EventId").AsGuid().PrimaryKey()
            .WithColumn("ChangeKind").AsString(40).NotNullable()
            .WithColumn("PersonelID").AsString(200).Nullable()
            .WithColumn("CanonicalWorkId").AsInt32().Nullable()
            .WithColumn("AcademicWorkId").AsInt32().Nullable()
            .WithColumn("OccurredAtUtc").AsDateTime2().NotNullable()
            .WithColumn("PayloadVersion").AsInt32().NotNullable().WithDefaultValue(1);

        Create.Index("IX_CollectionChanges_OccurredAtUtc_EventId")
            .OnTable("CollectionChanges").InSchema("core")
            .OnColumn("OccurredAtUtc").Ascending()
            .OnColumn("EventId").Ascending();
        Create.Index("IX_CollectionChanges_PersonelID_OccurredAtUtc")
            .OnTable("CollectionChanges").InSchema("core")
            .OnColumn("PersonelID").Ascending()
            .OnColumn("OccurredAtUtc").Ascending();

        Execute.Sql("""
            INSERT INTO [core].[CollectionChanges]
                ([EventId], [ChangeKind], [PersonelID], [CanonicalWorkId], [AcademicWorkId],
                 [OccurredAtUtc], [PayloadVersion])
            SELECT NEWID(), N'ResearcherCollected', researcher.[PersonelID], NULL, NULL,
                   SYSUTCDATETIME(), 1
            FROM [core].[Researchers] researcher;

            INSERT INTO [core].[CollectionChanges]
                ([EventId], [ChangeKind], [PersonelID], [CanonicalWorkId], [AcademicWorkId],
                 [OccurredAtUtc], [PayloadVersion])
            SELECT NEWID(), N'CanonicalWorkChanged', relation.[PersonelID],
                   relation.[CanonicalWorkId], observation.[AcademicWorkId], SYSUTCDATETIME(), 1
            FROM [core].[CanonicalResearcherWorks] relation
            OUTER APPLY
            (
                SELECT MIN(candidate.[AcademicWorkId]) AS [AcademicWorkId]
                FROM [core].[CanonicalWorkObservations] candidate
                WHERE candidate.[PersonelID] = relation.[PersonelID]
                  AND candidate.[CanonicalWorkId] = relation.[CanonicalWorkId]
            ) observation;
            """);
    }

    public override void Down() => Delete.Table("CollectionChanges").InSchema("core");
}
