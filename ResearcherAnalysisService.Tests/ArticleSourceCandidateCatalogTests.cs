using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.SourceData.Works;

namespace ResearcherAnalysisService.Tests;

public sealed class ArticleSourceCandidateCatalogTests
{
    [Fact]
    public void GetCandidates_PdfsAcrossWorksPrecedeLandingAndDoi()
    {
        AcademicWork selected = new()
        {
            Doi = "https://doi.org/10.1234/CaseSensitive",
            Link = "https://publisher.test/article"
        };
        AcademicWork sibling = new()
        {
            Sources = [new()
            {
                Url = "https://repo.test/Paper.pdf",
                Kind = "Pdf",
                Origin = "OpenAlex.Location[1].Pdf"
            }]
        };

        IReadOnlyList<ArticleSourceCandidate> candidates =
            ArticleSourceCandidateCatalog.GetCandidates([selected, sibling]);

        Assert.Equal("https://repo.test/Paper.pdf", candidates[0].Url);
        Assert.Contains(candidates, value =>
            value.Url == "https://doi.org/10.1234/casesensitive");
    }

    [Fact]
    public void GetCandidates_DifferentlyCasedPaths_AreNotCollapsed()
    {
        AcademicWork work = new()
        {
            Sources =
            [
                new() { Url = "https://repo.test/File.pdf", Kind = "Pdf", Origin = "A" },
                new() { Url = "https://repo.test/file.pdf", Kind = "Pdf", Origin = "B" }
            ]
        };

        Assert.Equal(2, ArticleSourceCandidateCatalog.GetCandidates([work]).Count);
    }
}
