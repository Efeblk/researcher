using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class PersonnelKeySchemaTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Schema_DirectResearcherRelationsReferencePersonnelPrimaryKey()
    {
        string[] directTables =
        [
            "OrcidProfiles", "GoogleScholarProfiles", "OpenAlexProfiles",
            "WebOfScienceProfiles", "YoksisRecords", "AcademicWorks",
            "PublicationSummaries", "PublicationDisplayApprovals",
            "ResearcherAnalyses"
        ];

        await using SqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();

        await using (SqlCommand primaryKey = connection.CreateCommand())
        {
            primaryKey.CommandText = """
                SELECT c.name
                FROM sys.key_constraints kc
                JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id
                    AND ic.index_id = kc.unique_index_id
                JOIN sys.columns c ON c.object_id = ic.object_id
                    AND c.column_id = ic.column_id
                WHERE kc.parent_object_id = OBJECT_ID('Researchers')
                    AND kc.type = 'PK';
                """;
            Assert.Equal("PersonelID", (string?)await primaryKey.ExecuteScalarAsync());
        }

        await using (SqlCommand tcKimlikNo = connection.CreateCommand())
        {
            tcKimlikNo.CommandText = """
                SELECT COUNT(*)
                FROM sys.columns c
                WHERE c.object_id = OBJECT_ID('Researchers')
                    AND c.name = 'TcKimlikNo'
                    AND TYPE_NAME(c.user_type_id) = 'nvarchar'
                    AND c.max_length = 22
                    AND c.is_nullable = 1
                    AND EXISTS (
                        SELECT 1
                        FROM sys.indexes i
                        WHERE i.object_id = c.object_id
                            AND i.name = 'IX_Researchers_TcKimlikNo'
                            AND i.is_unique = 1
                            AND i.filter_definition LIKE '%[[]TcKimlikNo]%IS NOT NULL%');
                """;
            Assert.Equal(1, Convert.ToInt32(await tcKimlikNo.ExecuteScalarAsync()));
        }

        foreach (string table in directTables)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM sys.foreign_key_columns fkc
                JOIN sys.columns parent_column ON parent_column.object_id = fkc.parent_object_id
                    AND parent_column.column_id = fkc.parent_column_id
                JOIN sys.columns referenced_column ON referenced_column.object_id = fkc.referenced_object_id
                    AND referenced_column.column_id = fkc.referenced_column_id
                WHERE fkc.parent_object_id = OBJECT_ID(@table)
                    AND fkc.referenced_object_id = OBJECT_ID('Researchers')
                    AND parent_column.name = 'PersonelID'
                    AND referenced_column.name = 'PersonelID'
                    AND NOT EXISTS (
                        SELECT 1 FROM sys.columns old_column
                        WHERE old_column.object_id = OBJECT_ID(@table)
                            AND old_column.name = 'ResearcherId');
                """;
            command.Parameters.AddWithValue("@table", table);
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
    }
}
