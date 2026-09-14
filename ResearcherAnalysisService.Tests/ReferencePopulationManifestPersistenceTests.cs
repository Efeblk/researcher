using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.Metrics;
using ResearcherAnalysisService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class ReferencePopulationManifestPersistenceTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task ImportAsync_ReviewedAttestation_RemainsExecutionUnavailableAndImmutable()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        ReferencePopulationManifestService service = new(database);
        string version = "reference-" + Guid.NewGuid().ToString("N");
        DateTimeOffset reviewedAt = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);
        ReferencePopulationImportRequest request = new()
        {
            PersonelId = "authorization-subject",
            ManifestVersion = version,
            CohortDefinition = "Documented external cohort.",
            EligibilityPolicyVersion = "external-reviewed-policy-v1",
            Provenance = "Synthetic persistence fixture only.",
            SamplingAndCoverage = "Two rows; no scientific representativeness claim.",
            Review = new()
            {
                Reviewer = "reviewer-a",
                ReviewedAt = reviewedAt,
                Method = "External attestation fixture.",
                ApprovedForInternalNormalization = true
            },
            Members =
            [
                new() { StableMemberId = "b", ClassificationId = "field:2", PublicationYear = 2024,
                    WorkType = "article", Category = "Article", CitationCount = 2 },
                new() { StableMemberId = "a", ClassificationId = "field:1", PublicationYear = 2023,
                    WorkType = "article", Category = "Article", CitationCount = 1 }
            ]
        };

        ReferencePopulationManifestResponse first = await service.ImportAsync(
            "actor-audit", request, CancellationToken.None);
        request.ManifestVersion = " " + version + " ";
        request.Members.Reverse();
        ReferencePopulationManifestResponse repeated = await service.ImportAsync(
            "different-actor", request, CancellationToken.None);

        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal(first.Fingerprint, repeated.Fingerprint);
        Assert.Equal("ReviewedApproved", first.ReviewStatus);
        Assert.False(first.ReadyForInternalNormalization);
        Assert.Equal("NotEstablishedBySoftware", first.ScientificValidationStatus);
        Assert.Equal(1, await database.ReferencePopulationManifests.CountAsync(value =>
            value.ManifestVersion == version));
        Assert.Equal(2, await database.ReferencePopulationMembers.CountAsync(value =>
            value.ReferencePopulationManifestId == first.Id));
    }
}
