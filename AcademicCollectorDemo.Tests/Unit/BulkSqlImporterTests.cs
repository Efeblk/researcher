using System.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.SqlImport;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class BulkSqlImporterTests
{
    [Fact]
    public void ReadRow_OnlyWebOfScienceColumn_UsesRowNumberAndLeavesOtherIdsEmpty()
    {
        DataTable data = new();
        data.Columns.Add("webofscienceID");
        data.Rows.Add(" A-1234-2020 ");
        using var reader = data.CreateDataReader();
        reader.Read();
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["webofscienceID"] = 0 };
        var row = BulkSqlImporter.ReadRow(reader, columns, new(), 1);
        Assert.Equal("A-1234-2020", row.WebOfScienceId);
        Assert.Equal("row-1", row.SourceResearcherId);
        Assert.Null(row.Orcid);
        Assert.Null(row.GoogleScholarId);
    }
}
