using System.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.SqlImport;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class BulkSqlImporterTests
{
    [Fact]
    public void ReadRow_OnlyWebOfScienceColumn_LeavesPersonnelAndOtherIdsEmpty()
    {
        DataTable data = new();
        data.Columns.Add("ResearcherID");
        data.Rows.Add(" A-1234-2020 ");
        using var reader = data.CreateDataReader();
        reader.Read();
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["ResearcherID"] = 0 };
        var row = BulkSqlImporter.ReadRow(reader, columns, new(), 1);
        Assert.Equal(" A-1234-2020 ", row.WebOfScienceId);
        Assert.Empty(row.PersonelId);
        Assert.Null(row.Orcid);
        Assert.Null(row.GoogleScholarId);
    }

    [Fact]
    public void ReadRow_PersonnelExportProfile_MapsResearcherIdToWebOfScience()
    {
        DataTable data = new();
        data.Columns.Add("PersonelID");
        data.Columns.Add("ORCID");
        data.Columns.Add("ResearcherID");
        data.Columns.Add("ScholarID");
        data.Columns.Add("ScopusID");
        data.Rows.Add(" person-7 ", " 0000-0002-1825-009X ", "A-1234-2020", "AbCdEfGhIjKl", " raw-scopus ");
        using var reader = data.CreateDataReader();
        reader.Read();
        var columns = Enumerable.Range(0, reader.FieldCount)
            .ToDictionary(reader.GetName, index => index, StringComparer.OrdinalIgnoreCase);
        var options = new BulkSqlSourceOptions
        {
            PersonelIdColumn = "PersonelID", OrcidColumn = "ORCID",
            WebOfScienceIdColumn = "ResearcherID", GoogleScholarIdColumn = "ScholarID",
            ScopusIdColumn = "ScopusID"
        };

        var row = BulkSqlImporter.ReadRow(reader, columns, options, 1);

        Assert.Equal(" person-7 ", row.PersonelId);
        Assert.Equal(" 0000-0002-1825-009X ", row.Orcid);
        Assert.Equal("A-1234-2020", row.WebOfScienceId);
        Assert.Equal("AbCdEfGhIjKl", row.GoogleScholarId);
        Assert.Equal(" raw-scopus ", row.ScopusId);
    }
}
