using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using System.Text.Json;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class AcademicPerformanceDtoMapperTests
{
    [Fact]
    public void MapResearcher_DefaultResponse_OmitsProviderDetails()
    {
        Researcher researcher = DetailsResearcher();

        var result = AcademicPerformanceDtoMapper.MapResearcher(researcher);

        Assert.Null(result!.ProviderDetails);
    }

    [Fact]
    public void MapResearcher_ProviderDetailsRequested_MapsBoundedResearcherFields()
    {
        Researcher researcher = DetailsResearcher();

        var details = AcademicPerformanceDtoMapper.MapResearcher(
            researcher, includeProviderDetails: true)!.ProviderDetails!;

        Assert.Equal("employment",
            details.Orcid!.Activities!.Value[0].GetProperty("Category").GetString());
        Assert.Equal("Alias", details.Orcid.OtherNames!.Value
            .GetProperty("other-name")[0].GetProperty("content").GetString());
        Assert.Equal("A. Person", details.OpenAlex!.AlternativeNames!.Value[0].GetString());
        Assert.Equal("0000-0001-8560-7482", details.Scopus!.Orcid);
        Assert.Equal(4, details.Scopus.CoauthorCount);
        Assert.Equal("Coauthor", details.GoogleScholar!.CoAuthors!.Value[0]
            .GetProperty("name").GetString());
        Assert.Equal("https://images.test/profile", details.GoogleScholar.Thumbnail);
    }

    private static Researcher DetailsResearcher() => new()
    {
        PersonelId = "P-1",
        OrcidProfile = new OrcidProfile
        {
            RawDataJson = """{"person":{"other-names":{"other-name":[{"content":"Alias"}]},"emails":{"email":[{"email":"public@example.test"}]}}}""",
            ActivitiesDetailsJson = """[{"Category":"employment","PutCode":1,"Item":{"put-code":1}}]"""
        },
        OpenAlexProfile = new OpenAlexProfile
        {
            OpenAlexAuthorId = "A1",
            RawDataJson = """{"results":[{"id":"A0","display_name_alternatives":["Wrong"]},{"id":"A1","ids":{"orcid":"https://orcid.org/0000-0001-8560-7482"},"display_name_alternatives":["A. Person"],"affiliations":[],"topics":[]}]}"""
        },
        ScopusProfile = new ScopusProfile
        {
            ScopusAuthorId = "1",
            RawDataJson = """{"author-retrieval-response":[{"coredata":{"orcid":"0000-0001-8560-7482"},"coauthor-count":"4","author-profile":{"name-variant":[],"affiliation-current":{},"affiliation-history":{}},"subject-areas":{}}]}"""
        },
        GoogleScholarProfile = new GoogleScholarProfile
        {
            RawDataJson = """[{"author":{"interests":[],"thumbnail":"https://images.test/profile"},"co_authors":[{"name":"Coauthor"}],"public_access":{"available":2}}]"""
        }
    };

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
