using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ResearcherProviderInputNormalizerTests
{
    private readonly ResearcherProviderInputNormalizer _normalizer = new(new());

    [Fact]
    public void Normalize_MessyAndCanonicalInput_ProducesSameProviderTargets()
    {
        ResearcherProviderInputNormalizationResult messy = _normalizer.Normalize(new()
        {
            Orcid = " (https://orcid.org/0000-0002-1825-009X) ,",
            GoogleScholarId = "https://scholar.google.com.tr/citations?user=AbCdEfGhIjKl&hl=tr",
            WebOfScienceResearcherId = "https://www.webofscience.com/wos/author/record/A-1234-2020."
        });
        ResearcherProviderInputNormalizationResult canonical = _normalizer.Normalize(new()
        {
            Orcid = "0000-0002-1825-009X", GoogleScholarId = "AbCdEfGhIjKl",
            WebOfScienceResearcherId = "A-1234-2020"
        });
        Assert.Equal(ResearcherProviderInputNormalizer.ToCollectionRequest(canonical.Input).Identifiers,
            ResearcherProviderInputNormalizer.ToCollectionRequest(messy.Input).Identifiers);
    }

    [Fact]
    public void Normalize_ValidAndBadFields_UsesValidSubsetAndReturnsSafeWarnings()
    {
        const string raw = "https://example.test/citations?user=sensitive12";
        ResearcherProviderInputNormalizationResult result = _normalizer.Normalize(new()
        {
            Orcid = "ORCID. 0000 0002 1825 009X", GoogleScholarId = raw, ScopusId = "private-scopus"
        });
        Assert.Equal("0000-0002-1825-009X", result.Input.Orcid);
        Assert.Null(result.Input.GoogleScholarId);
        Assert.Null(result.RejectionReason);
        Assert.Equal(2, result.Warnings.Count);
        Assert.DoesNotContain(raw, string.Join(' ', result.Warnings));
        Assert.DoesNotContain("private-scopus", string.Join(' ', result.Warnings));
    }

    [Fact]
    public void Normalize_AllInvalid_ReturnsSafeRejection()
    {
        ResearcherProviderInputNormalizationResult result = _normalizer.Normalize(new()
        {
            Orcid = "A-1234-2020", GoogleScholarId = "1.23456789E+11", ScopusId = "unsupported"
        });
        Assert.NotNull(result.RejectionReason);
        Assert.DoesNotContain("1.23456789E+11", result.RejectionReason);
    }
}
