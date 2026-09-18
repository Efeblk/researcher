using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class AcademicActivityProjectorTests
{
    [Fact]
    public void Project_SavedProviders_MapsSafeFieldsAndKeepsProvenance()
    {
        YoksisRecord[] yoksis =
        [
            Record("Projeler", "getirProjeListesi", "P-7", """{"PROJE_ID":"P-7"}"""),
            Record("Proje ayrıntıları", "getirProjeListesiDetay", "P-7",
                """{"PROJE_ID":"P-7","PROJE_AD":"Güvenli Proje","BAS_TAR":"2024","BIT_TAR":"2025","PROJE_KONUMU_AD":"Yürütücü","KURUM_AD":"Üniversite"}"""),
            Record("Ödüller", "getOdulListesiV1", "A-1",
                """{"ODUL_ADI":"Bilim Ödülü","ODUL_TARIH":"2025","KURULUS_ADI":"Vakıf","TC_KIMLIK_NO":"[GİZLENDİ]"}"""),
            Record("Makaleler", "getMakaleBilgisiV1", "W-1", """{"BASLIK":"Yayın"}""")
        ];
        OrcidProfile orcid = new()
        {
            ActivitiesDetailsJson = """[{"Category":"funding","PutCode":8,"Item":{"title":{"title":{"value":"Araştırma Fonu"}},"organization":{"name":"Fon Kurumu"}}},{"Category":"distinction","PutCode":9,"Item":{"role-title":"ORCID Ödülü","organization":{"name":"Akademi"}}}]"""
        };
        TrDizinProject project = new()
        {
            ProjectId = "T-1", Title = "TR Dizin Projesi", StartedDate = "2023",
            Duty = "Araştırmacı", ProjectGroup = "Konsorsiyum"
        };

        var result = AcademicActivityProjector.Project(yoksis, orcid, [project]);

        Assert.Equal(5, result.Count);
        Assert.Single(result, value => value.Provider == "YÖKSİS" && value.Category == "Proje");
        Assert.Contains(result, value => value.Title == "Güvenli Proje" && value.Role == "Yürütücü");
        Assert.Contains(result, value => value.Title == "Bilim Ödülü" && value.Organization == "Vakıf");
        Assert.Contains(result, value => value.Provider == "ORCID" && value.Title == "Araştırma Fonu");
        Assert.Contains(result, value => value.Provider == "TR Dizin" && value.SourceId == "T-1");
        Assert.DoesNotContain(result, value => value.Title == "Yayın");
    }

    [Fact]
    public void Project_MalformedRecords_SkipsRecordAndFallsBackToProjectBase()
    {
        YoksisRecord[] records =
        [
            Record("Projeler", "getirProjeListesi", "P-1", """{"PROJE_ID":"P-1"}"""),
            Record("Proje ayrıntıları", "getirProjeListesiDetay", "P-1", "not-json"),
            Record("Ödüller", "getOdulListesiV1", "A-1", "not-json")
        ];

        var result = AcademicActivityProjector.Project(records,
            new OrcidProfile { ActivitiesDetailsJson = "not-json" }, []);

        var activity = Assert.Single(result);
        Assert.Equal("P-1", activity.SourceId);
        Assert.Null(activity.Title);
    }

    [Theory]
    [InlineData("getirAkademikGorevListesi", "Akademik görev", "KADRO_UNVAN_ADI", "Profesör")]
    [InlineData("getEditorlukBilgisiV1", "Editörlük", "YAYIN_ADI", "Bilim Dergisi")]
    [InlineData("getirYabanciDilListesi", "Yabancı dil", "DIL_AD", "İngilizce")]
    [InlineData("getTemelAlanBilgisiV1", "Temel alan", "TEMEL_ALAN_AD", "Mühendislik")]
    public void Project_KnownYoksisCategories_UsesVerifiedDisplayField(
        string operation, string expectedCategory, string field, string expectedTitle)
    {
        YoksisRecord record = Record("Sağlayıcı kategorisi", operation, "1",
            $$"""{"{{field}}":"{{expectedTitle}}"}""");

        var activity = Assert.Single(AcademicActivityProjector.Project([record], null, []));

        Assert.Equal(expectedCategory, activity.Category);
        Assert.Equal(expectedTitle, activity.Title);
    }

    [Fact]
    public void Project_OrcidPeerReviewAndResearchResource_UsesCategorySpecificPaths()
    {
        OrcidProfile profile = new()
        {
            ActivitiesDetailsJson = """[{"Category":"peer-review","PutCode":11,"Item":{"subject-name":"Journal review","convening-organization":{"name":"Publisher"},"reviewer-role":"reviewer","review-completion-date":{"year":{"value":"2025"}}}},{"Category":"research-resource","PutCode":12,"Item":{"proposal":{"title":{"title":{"value":"Beam time"}},"host":{"organization":[{"name":"Laboratory"}]}}}}]"""
        };

        var result = AcademicActivityProjector.Project([], profile, []);

        Assert.Contains(result, value => value.Category == "Hakemlik" &&
            value.Title == "Journal review" && value.Organization == "Publisher" &&
            value.Date == "2025" && value.Role == "reviewer");
        Assert.Contains(result, value => value.Category == "Araştırma kaynağı" &&
            value.Title == "Beam time" && value.Organization == "Laboratory");
    }

    private static YoksisRecord Record(string category, string operation, string id, string json) => new()
    {
        CategoryName = category,
        OperationName = operation,
        ExternalRecordId = id,
        RecordJson = json
    };
}
