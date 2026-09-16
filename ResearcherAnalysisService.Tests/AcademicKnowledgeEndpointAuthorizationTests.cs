using System.Net;
using System.Net.Http.Json;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class AcademicKnowledgeEndpointAuthorizationTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task KnowledgeEndpoints_Unauthenticated_FailBeforeResourceLookupOrProviderCall()
    {
        using HostProcess host = new(fixture.ConnectionString);
        await host.WaitUntilReadyAsync();
        object[] requests =
        [
            new AcademicEvidenceSearchRequest
            {
                PersonelId = "missing-subject", Query = "evidence", Take = 5
            },
            new ReferencePopulationImportRequest
            {
                PersonelId = "missing-subject", ManifestVersion = "test-v1",
                CohortDefinition = "Synthetic.", EligibilityPolicyVersion = "policy-v1",
                Provenance = "Synthetic.", SamplingAndCoverage = "Synthetic.",
                Members = [new() { StableMemberId = "one", ClassificationId = "field:1",
                    PublicationYear = 2024, WorkType = "article", Category = "Article",
                    CitationCount = 0 }]
            },
            new ReferencePopulationManifestRequest
            {
                PersonelId = "missing-subject", ManifestVersion = "test-v1"
            },
            new GraphProjectionRequest
            {
                PersonelId = "missing-subject",
                CanonicalWorkIds = Enumerable.Range(1, 20).ToList()
            }
        ];
        string[] routes =
        [
            "knowledge/search",
            "knowledge/reference-population/import", "knowledge/reference-population", "knowledge/graph/export"
        ];

        for (int index = 0; index < routes.Length; index++)
        {
            using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
                "/api/v1/" + routes[index], requests[index]);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task CollectionRequests_ExplicitNull_AreRejectedBeforeAuthorization()
    {
        using HostProcess host = new(fixture.ConnectionString);
        await host.WaitUntilReadyAsync();
        (string Route, object Body)[] cases =
        [
            ("knowledge/search", new { PersonelID = "subject", Query = "evidence",
                CanonicalWorkIds = (int[]?)null, Take = 5 }),
            ("knowledge/reference-population/import", new { PersonelID = "subject", ManifestVersion = "v1",
                CohortDefinition = "x", EligibilityPolicyVersion = "p", Provenance = "x",
                SamplingAndCoverage = "x", Members = (object[]?)null }),
            ("knowledge/graph/export", new { PersonelID = "subject",
                CanonicalWorkIds = (int[]?)null })
        ];

        foreach ((string route, object body) in cases)
        {
            using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
                "/api/v1/" + route, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
