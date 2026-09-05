using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ResearcherIdentifierParserTests
{
    [Theory]
    [InlineData("--orcid", "invalid")]
    [InlineData("--orcid", "A-1009-2008")]
    [InlineData("--researcherid", "invalid")]
    [InlineData("--wos", "0000-0001-8560-7482")]
    [InlineData("--scholar", "invalid")]
    [InlineData("--orcid", "００００-０００１-８５６０-７４８２")]
    public void Create_InvalidNamedIdentifier_Throws(string name, string value)
    {
        Assert.Throws<ArgumentException>(() => new ResearcherIdentifierParser()
            .Create(new() { Identifiers = [name, value] }));
    }

    [Fact]
    public void Create_NamedIdentifiers_NormalizesWhitespaceAndCase()
    {
        var researcher = new ResearcherIdentifierParser().Create(new()
        {
            Identifiers = ["--orcid", " 0000-0002-1825-009x ", "--wos", " a-1009-2008 ", "--scholar", " AbCdEfGhIjKl "]
        });
        Assert.Equal("0000-0002-1825-009X", researcher.Orcid);
        Assert.Equal("A-1009-2008", researcher.WebOfScienceResearcherId);
        Assert.Equal("AbCdEfGhIjKl", researcher.GoogleScholarId);
    }

    [Fact]
    public void Create_DuplicateProvider_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ResearcherIdentifierParser()
            .Create(new() { Identifiers = ["0000-0001-8560-7482", "--orcid", "0000-0002-1825-009X"] }));
    }
}
