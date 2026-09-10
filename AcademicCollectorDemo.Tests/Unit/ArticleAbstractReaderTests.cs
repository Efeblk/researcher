using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries.Enrichment;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ArticleAbstractReaderTests
{
    [Fact]
    public void FromPayload_OpenAlexRepeatedTokenPositions_ReconstructsPositionOrder()
    {
        const string payload = """
            {"abstract_inverted_index":{"Alpha":[0,2],"beta":[1],"again.":[3]}}
            """;

        string? result = ArticleAbstractReader.FromPayload(payload, "OpenAlex");

        Assert.Equal("Alpha beta Alpha again.", result);
    }

    [Theory]
    [InlineData("{\"abstract_inverted_index\":{\"one\":[0],\"three\":[2]}}")]
    [InlineData("{\"abstract_inverted_index\":{\"one\":[0],\"other\":[0]}}")]
    [InlineData("{\"abstract_inverted_index\":{\"one\":[\"0\"]}}")]
    [InlineData("{\"abstract_inverted_index\":{\"one\":[]}}")]
    [InlineData("not-json")]
    public void FromPayload_OpenAlexMalformedIndex_ReturnsNull(string payload)
    {
        Assert.Null(ArticleAbstractReader.FromPayload(payload, "OpenAlex"));
    }

    [Fact]
    public void FromPayload_OpenAlexOversizedAbstract_ReturnsNull()
    {
        string token = new('x', ArticleAbstractReader.MaximumAbstractCharacters + 1);
        string payload = "{\"abstract_inverted_index\":{\"" + token + "\":[0]}}";

        Assert.Null(ArticleAbstractReader.FromPayload(payload, "OpenAlex"));
    }

    [Fact]
    public void FromPayload_CrossrefJats_CleansMarkupAndPreservesParagraphs()
    {
        const string payload = """
            {"message":{"abstract":"<jats:p>First &amp; measured <jats:bold>result</jats:bold>.</jats:p><jats:p>Second paragraph.</jats:p>"}}
            """;

        string? result = ArticleAbstractReader.FromPayload(payload, "Crossref");

        Assert.Equal("First & measured result.\n\nSecond paragraph.", result);
    }

    [Fact]
    public void FromPayload_CrossrefEntityDeclaration_RejectsPayloadWithoutExpansion()
    {
        const string payload = """
            {"message":{"abstract":"<!DOCTYPE x [<!ENTITY secret SYSTEM 'file:///secret'>]><p>&secret;</p>"}}
            """;

        Assert.Null(ArticleAbstractReader.FromPayload(payload, "Crossref"));
    }
}
