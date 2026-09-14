using System.Net;
using System.Net.Http.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Tests.Infrastructure;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class AcademicAiProductEndpointAuthorizationTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task PrivateProductEndpoints_Unauthenticated_FailBeforeDatabaseOrProviderUse()
    {
        using HostProcess host = new(fixture.ConnectionString, "http://127.0.0.1:1/");
        await host.WaitUntilReadyAsync();

        (string Action, object Body)[] requests =
        [
            ("CreateHrEvidenceDossier", new CreateHrEvidenceDossierRequest
            {
                PersonelId = "missing-subject", CanonicalWorkIds = [1], Language = "tr"
            }),
            ("GetHrEvidenceDossier", new GetHrEvidenceDossierRequest
            {
                PersonelId = "missing-subject", DossierId = 1
            }),
            ("AppendHrDossierReviewAction", new AppendHrDossierReviewActionRequest
            {
                PersonelId = "missing-subject", DossierId = 1, ClientRequestId = Guid.NewGuid(),
                ActionType = "NoteAdded", Note = "Synthetic."
            }),
            ("ListHrDossierReviewActions", new ListHrDossierReviewActionsRequest
            {
                PersonelId = "missing-subject", DossierId = 1
            }),
            ("SaveFacultyAssistantContext", new SaveFacultyAssistantContextRequest
            {
                PersonelId = "missing-subject", ExpectedVersion = 0,
                Context = new FacultyPrivateContext { Language = "tr" }
            }),
            ("GetFacultyAssistantContext", new GetFacultyAssistantContextRequest
            {
                PersonelId = "missing-subject", Version = 1
            }),
            ("StartFacultyAssistant", new StartFacultyAssistantRequest
            {
                PersonelId = "missing-subject", ClientRequestId = Guid.NewGuid(),
                Mode = "OwnPaperMethods", Language = "tr", Query = "Synthetic question.",
                CanonicalWorkIds = [1], ContextVersion = 1
            }),
            ("GetFacultyAssistantRun", new GetFacultyAssistantRunRequest
            {
                PersonelId = "missing-subject", RunId = Guid.NewGuid()
            })
        ];

        foreach ((string action, object body) in requests)
        {
            using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
                "/Services/AcademicPerformance/V1/" + action, body);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
