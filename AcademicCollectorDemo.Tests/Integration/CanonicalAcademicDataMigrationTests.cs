using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class CanonicalAcademicDataMigrationTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Migration_PreexistingPublicationStateIsPreservedAndEfModelMatchesSchema()
    {
        string personelId = "canonical-migration-" + Guid.NewGuid().ToString("N");
        MigrateDown();
        try
        {
            await using (SqlConnection connection = new(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using SqlCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO [core].[Researchers] ([PersonelID]) VALUES (@personelId);
                    INSERT INTO [core].[AcademicWorks]
                        ([PersonelID], [Provider], [Title], [Doi], [Category], [CategorySource], [SyncedAt])
                    VALUES (@personelId, N'Orcid', N'Preserved', N'10.6000/preserved',
                        N'Article', N'Provider', SYSUTCDATETIME());
                    INSERT INTO [core].[PublicationSummaries]
                        ([PersonelID], [Fingerprint], [Title], [Doi], [Category], [Sources], [UpdatedAt])
                    VALUES (@personelId, REPLICATE(N'a', 64), N'Preserved', N'10.6000/preserved',
                        N'Article', N'Orcid', SYSUTCDATETIME());
                    DECLARE @summaryId int = SCOPE_IDENTITY();
                    INSERT INTO [core].[PublicationDisplayApprovals]
                        ([PersonelID], [PublicationSummaryId], [ApprovedAt])
                    VALUES (@personelId, @summaryId, SYSUTCDATETIME());
                    """;
                seed.Parameters.AddWithValue("@personelId", personelId);
                await seed.ExecuteNonQueryAsync();
            }

            MigrateUp();

            await using SqlConnection migrated = new(fixture.ConnectionString);
            await migrated.OpenAsync();
            await using SqlCommand verify = migrated.CreateCommand();
            verify.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM [core].[AcademicWorks] WHERE [PersonelID] = @personelId) +
                    (SELECT COUNT(*) FROM [core].[PublicationSummaries] WHERE [PersonelID] = @personelId) +
                    (SELECT COUNT(*) FROM [core].[PublicationDisplayApprovals] WHERE [PersonelID] = @personelId) +
                    (SELECT COUNT(*) FROM [core].[PublicationSummaries]
                        WHERE [PersonelID] = @personelId AND [CanonicalWorkId] IS NULL) +
                    (SELECT COUNT(*) FROM sys.foreign_keys
                        WHERE [name] = N'FK_PublicationSummaries_CanonicalWorks_CanonicalWorkId') +
                    (SELECT COUNT(*) FROM sys.indexes
                        WHERE [object_id] = OBJECT_ID(N'[core].[PublicationSummaries]')
                          AND [name] = N'UX_PublicationSummaries_PersonelID_CanonicalWorkId'
                          AND [is_unique] = 1 AND [filter_definition] IS NOT NULL) +
                    (SELECT COUNT(*) FROM sys.check_constraints
                        WHERE [name] = N'CK_CanonicalWorks_ExactlyOneIdentity') +
                    (SELECT COUNT(*) FROM sys.indexes
                        WHERE [object_id] = OBJECT_ID(N'[core].[CanonicalWorks]')
                          AND [name] IN (N'UX_CanonicalWorks_NormalizedDoi', N'UX_CanonicalWorks_SourceScopedKey'));
                """;
            verify.Parameters.AddWithValue("@personelId", personelId);
            Assert.Equal(9, Convert.ToInt32(await verify.ExecuteScalarAsync()));

            using IServiceScope scope = fixture.Services.CreateScope();
            AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            IEntityType canonical = db.Model.FindEntityType(typeof(CanonicalWork))!;
            Assert.Equal("CanonicalWorks", canonical.GetTableName());
            Assert.Equal("core", canonical.GetSchema());
            Assert.Equal("CanonicalWorkObservations",
                db.Model.FindEntityType(typeof(CanonicalWorkObservation))!.GetTableName());
            Assert.Equal("CanonicalResearcherWorks",
                db.Model.FindEntityType(typeof(CanonicalResearcherWork))!.GetTableName());
        }
        finally
        {
            MigrateUp();
        }
    }

    private void MigrateDown()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202609140002);
    }

    private void MigrateUp()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
