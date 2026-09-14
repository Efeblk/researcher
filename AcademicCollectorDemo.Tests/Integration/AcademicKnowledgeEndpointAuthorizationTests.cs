using System.Net;
using System.Net.Http.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Tests.Infrastructure;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class AcademicKnowledgeEndpointAuthorizationTests(SqlServerFixture fixture)
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
        string[] actions =
        [
            "SearchAcademicEvidence",
            "ImportReferencePopulation", "GetReferencePopulation", "ExportAcademicEvidenceGraph"
        ];

        for (int index = 0; index < actions.Length; index++)
        {
            using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
                "/Services/AcademicPerformance/V1/" + actions[index], requests[index]);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task CollectionRequests_ExplicitNull_AreRejectedBeforeAuthorization()
    {
        using HostProcess host = new(fixture.ConnectionString);
        await host.WaitUntilReadyAsync();
        (string Action, object Body)[] cases =
        [
            ("SearchAcademicEvidence", new { PersonelID = "subject", Query = "evidence",
                CanonicalWorkIds = (int[]?)null, Take = 5 }),
            ("ImportReferencePopulation", new { PersonelID = "subject", ManifestVersion = "v1",
                CohortDefinition = "x", EligibilityPolicyVersion = "p", Provenance = "x",
                SamplingAndCoverage = "x", Members = (object[]?)null }),
            ("ExportAcademicEvidenceGraph", new { PersonelID = "subject",
                CanonicalWorkIds = (int[]?)null })
        ];

        foreach ((string action, object body) in cases)
        {
            using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
                "/Services/AcademicPerformance/V1/" + action, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
