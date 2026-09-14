using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class AcademicWorkSourceDiscoveryTests
{
    [Fact]
    public void FromPayload_OpenAlexLaterLocationPdf_ReturnsArticleSourcesOnly()
    {
        const string payload = """
        {"primary_location":{"landing_page_url":"https://publisher.test/article"},
         "locations":[{"is_oa":false,"landing_page_url":"https://closed.test/article"},
                      {"is_oa":true,"pdf_url":"https://repository.test/paper.pdf"}],
         "authorships":[{"author":{"url":"https://wrong.test/author"}}]}
        """;

        IReadOnlyList<AcademicWorkSource> sources = AcademicWorkSourceDiscovery.FromPayload(payload, "OpenAlex");

        Assert.Contains(sources, x => x.Url == "https://repository.test/paper.pdf" && x.Kind == "Pdf" && x.IsOpenAccess == true);
        Assert.DoesNotContain(sources, x => x.Url.Contains("wrong.test", StringComparison.Ordinal));
    }

    [Fact]
    public void FromPayload_CrossrefLink_KeepsPdfContentTypeOnly()
    {
        const string payload = """
        {"message":{"URL":"https://doi.test/article","link":[
          {"URL":"https://publisher.test/full?download=1","content-type":"application/pdf"},
          {"URL":"https://wrong.test/data","content-type":"application/xml"}]}}
        """;

        IReadOnlyList<AcademicWorkSource> sources = AcademicWorkSourceDiscovery.FromPayload(payload, "Crossref");

        AcademicWorkSource pdf = Assert.Single(sources);
        Assert.Equal("https://publisher.test/full?download=1", pdf.Url);
        Assert.Equal("Pdf", pdf.Kind);
    }

}
