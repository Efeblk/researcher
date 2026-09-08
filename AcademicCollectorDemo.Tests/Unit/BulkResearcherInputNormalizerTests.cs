using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class BulkResearcherInputNormalizerTests
{
    private readonly BulkResearcherInputNormalizer _normalizer =
        new(new(new ResearcherIdentifierParser()));

    [Theory]
    [InlineData(" (https://orcid.org/0000-0002-1825-009X) ,", "0000-0002-1825-009X")]
    [InlineData("ORCID. 0000 0002 1825 009X", "0000-0002-1825-009X")]
    public void Normalize_SafeOrcidForms_ReturnsCanonicalId(string value, string expected)
    {
        var result = _normalizer.Normalize(Input(orcid: value));
        Assert.Equal(expected, result.Input.Orcid);
        Assert.Null(result.RejectionReason);
    }

    [Theory]
    [InlineData("https://www.webofscience.com/wos/author/record/A-1234-2020.")]
    [InlineData("https://www.webofscience.com/wos/author/rid/A-1234-2020")]
    public void Normalize_WebOfScienceLink_ReturnsCanonicalId(string value)
    {
        var result = _normalizer.Normalize(Input(wos: value));
        Assert.Equal("A-1234-2020", result.Input.WebOfScienceId);
    }

    [Theory]
    [InlineData("https://scholar.google.com.tr/citations?user=AbCdEfGhIjKl&hl=tr")]
    [InlineData("AbCdEfGhIjKl&hl")]
    public void Normalize_ScholarLinkOrSuffix_PreservesCase(string value)
    {
        var result = _normalizer.Normalize(Input(scholar: value));
        Assert.Equal("AbCdEfGhIjKl", result.Input.GoogleScholarId);
    }

    [Theory]
    [InlineData("https://example.test/citations?user=AbCdEfGhIjKl")]
    [InlineData("https://scholar.google.com/citations?user=AbCdEfGhIjKl&user=ZyXwVuTsRqPo")]
    [InlineData("https://scholar.google.com/citations?user=AbCdEfGhIjKl&user")]
    [InlineData("AbCdEfGhIjKl&user=ZyXwVuTsRqPo")]
    [InlineData("AbCdEfGhIjKl&user")]
    [InlineData("1.23456789E+11")]
    [InlineData("#NAME?")]
    public void Normalize_BogusAmbiguousOrNumericScholar_DiscardsOnlyScholar(string value)
    {
        var result = _normalizer.Normalize(Input(orcid: "0000-0002-1825-009X", scholar: value));
        Assert.Null(result.Input.GoogleScholarId);
        Assert.Equal("0000-0002-1825-009X", result.Input.Orcid);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("Google Scholar ID:"));
        Assert.Null(result.RejectionReason);
    }

    [Theory]
    [InlineData("1234567890123456")]
    [InlineData("person@example.test")]
    [InlineData("A-1234-2020")]
    public void Normalize_ForeignOrDamagedOrcid_DoesNotGuessOrMove(string value)
    {
        var result = _normalizer.Normalize(Input(orcid: value));
        Assert.Null(result.Input.Orcid);
        Assert.Null(result.Input.WebOfScienceId);
        Assert.NotNull(result.RejectionReason);
    }

    [Fact]
    public void Normalize_OversizedOptionalField_KeepsOtherProviderWithoutEchoingRawWarning()
    {
        string oversized = new('x', BulkResearcherInputNormalizer.MaximumProviderInputLength + 1);
        var result = _normalizer.Normalize(Input(wos: "A-1234-2020", scholar: oversized));
        Assert.Equal("A-1234-2020", result.Input.WebOfScienceId);
        Assert.Null(result.Input.GoogleScholarId);
        Assert.DoesNotContain(oversized, string.Join(' ', result.Warnings));
    }

    [Fact]
    public void Normalize_ScopusOnly_DoesNotMutateOriginalAndRejectsUnsupportedProvider()
    {
        var input = Input();
        input.ScopusId = "raw-scopus-value";
        var result = _normalizer.Normalize(input);
        Assert.Null(result.Input.ScopusId);
        Assert.NotNull(result.RejectionReason);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("Scopus ID:"));
        Assert.Equal("raw-scopus-value", input.ScopusId);
    }

    private static BulkResearcherInput Input(string? orcid = null, string? scholar = null, string? wos = null) => new()
    {
        SourceResearcherId = "synthetic-person", Orcid = orcid,
        GoogleScholarId = scholar, WebOfScienceId = wos
    };
}
