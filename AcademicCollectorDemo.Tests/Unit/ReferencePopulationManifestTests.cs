using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ReferencePopulationManifestTests
{
    [Fact]
    public void Fingerprint_WhitespaceAndInputOrder_DoesNotChangeIdentity()
    {
        ReferencePopulationImportRequest first = Request();
        ReferencePopulationImportRequest second = Request();
        second.ManifestVersion = "  reference-v1 ";
        second.Members.Reverse();

        Assert.Equal(ReferencePopulationManifestService.Fingerprint(first),
            ReferencePopulationManifestService.Fingerprint(second));
    }

    [Fact]
    public void Validate_DuplicateOrUnknownCategory_RejectsManifest()
    {
        ReferencePopulationImportRequest duplicate = Request();
        duplicate.Members[1].StableMemberId = duplicate.Members[0].StableMemberId;
        Assert.Throws<ArgumentException>(() => ReferencePopulationManifestService.Validate(duplicate));

        ReferencePopulationImportRequest unknown = Request();
        unknown.Members[0].Category = "InventedProviderTruth";
        Assert.Throws<ArgumentException>(() => ReferencePopulationManifestService.Validate(unknown));
    }

    [Fact]
    public void Validate_NullMembersOrBlankReview_RejectsManifest()
    {
        ReferencePopulationImportRequest missing = Request();
        missing.Members = null!;
        Assert.Throws<ArgumentException>(() => ReferencePopulationManifestService.Validate(missing));

        ReferencePopulationImportRequest blankReview = Request();
        blankReview.Review = new() { Reviewer = null!, Method = null!, ReviewedAt = default };
        Assert.Throws<ArgumentException>(() => ReferencePopulationManifestService.Validate(blankReview));
    }

    private static ReferencePopulationImportRequest Request() => new()
    {
        ManifestVersion = "reference-v1",
        CohortDefinition = "Public DOI works in a documented external cohort.",
        EligibilityPolicyVersion = "reviewed-policy-v1",
        Provenance = "Synthetic unit-test provenance only.",
        SamplingAndCoverage = "Two mechanical fixture rows; not scientific validation.",
        Members =
        [
            new() { StableMemberId = "b", ClassificationId = "field:2", PublicationYear = 2024,
                WorkType = "article", Category = "Article", CitationCount = 2 },
            new() { StableMemberId = "a", ClassificationId = "field:1", PublicationYear = 2023,
                WorkType = "article", Category = "Article", CitationCount = 1 }
        ]
    };
}
