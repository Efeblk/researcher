using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using System.Text.Json;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class AcademicPerformanceDtoMapperTests
{
    [Fact]
    public void MapResearcher_DatabasePagesPresent_MapsDistinctWosAndWokTotals()
    {
        Researcher researcher = new()
        {
            WebOfScienceProfile = new()
            {
                DocumentsCount = 6,
                DocumentPagesJson = """{"WOS":[{"metadata":{"total":4}}],"WOK":[{"metadata":{"total":5}}]}"""
            }
        };

        var profile = AcademicPerformanceDtoMapper.MapResearcher(researcher)!.WebOfScienceProfile!;

        Assert.Equal(4, profile.WosDocumentsCount);
        Assert.Equal(5, profile.WokDocumentsCount);
        Assert.Equal(6, profile.DocumentsCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"WOS\":[{\"metadata\":{\"total\":\"unknown\"}}]}")]
    public void MapResearcher_DatabaseTotalUnavailable_DoesNotInventZero(string? pagesJson)
    {
        Researcher researcher = new()
        {
            WebOfScienceProfile = new WebOfScienceProfile
            {
                DocumentPagesJson = pagesJson
            }
        };

        var profile = AcademicPerformanceDtoMapper.MapResearcher(researcher)!.WebOfScienceProfile!;

        Assert.Null(profile.WosDocumentsCount);
        Assert.Null(profile.WokDocumentsCount);
    }

    [Fact]
    public void ReadCategoryMetrics_SavedSnapshot_ExposesOnlySuccessfulCounters()
    {
        YoksisCollectResponse response = new()
        {
            IsSaved = true,
            Categories =
            [
                new() { CategoryName = "Projeler", OperationName = "projects", IsSuccess = true,
                    RecordCount = 0, Records = [new() { ["secret"] = "not-exposed" }] },
                new() { CategoryName = "Ödüller", OperationName = "awards", IsSuccess = false,
                    RecordCount = 3 }
            ]
        };

        var metric = Assert.Single(AcademicPerformanceApplicationService.ReadCategoryMetrics(
            JsonSerializer.Serialize(response)));

        Assert.Equal("Projeler", metric.CategoryName);
        Assert.Equal("projects", metric.OperationName);
        Assert.Equal(0, metric.RecordCount);
    }

    [Fact]
    public void ReadCategoryMetrics_UnsavedSnapshot_ReturnsNoCounters()
    {
        string responseJson = JsonSerializer.Serialize(new YoksisCollectResponse
        {
            IsSaved = false,
            Categories = [new() { CategoryName = "Projeler", OperationName = "projects",
                IsSuccess = true, RecordCount = 5 }]
        });

        Assert.Empty(AcademicPerformanceApplicationService.ReadCategoryMetrics(responseJson));
    }
}
