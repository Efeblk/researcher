using System.Net;
using System.Net.Http.Json;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class AcademicAiProductEndpointAuthorizationTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task PrivateProductEndpoints_Unauthenticated_FailBeforeDatabaseOrProviderUse()
    {
        using HostProcess host = new(fixture.ConnectionString, "http://127.0.0.1:1/");
        await host.WaitUntilReadyAsync();

        (string Route, object Body)[] requests =
        [
            ("hr/dossiers/create", new CreateHrEvidenceDossierRequest
            {
                PersonelId = "missing-subject", CanonicalWorkIds = [1], Language = "tr"
            }),
            ("hr/dossiers", new GetHrEvidenceDossierRequest
            {
                PersonelId = "missing-subject", DossierId = 1
            }),
            ("hr/dossiers/actions/append", new AppendHrDossierReviewActionRequest
            {
                PersonelId = "missing-subject", DossierId = 1, ClientRequestId = Guid.NewGuid(),
                ActionType = "NoteAdded", Note = "Synthetic."
            }),
            ("hr/dossiers/actions", new ListHrDossierReviewActionsRequest
            {
                PersonelId = "missing-subject", DossierId = 1
            }),
            ("faculty/context/save", new SaveFacultyAssistantContextRequest
            {
                PersonelId = "missing-subject", ExpectedVersion = 0,
                Context = new FacultyPrivateContext { Language = "tr" }
            }),
            ("faculty/context", new GetFacultyAssistantContextRequest
            {
                PersonelId = "missing-subject", Version = 1
            }),
            ("faculty/assistant/start", new StartFacultyAssistantRequest
            {
                PersonelId = "missing-subject", ClientRequestId = Guid.NewGuid(),
                Mode = "OwnPaperMethods", Language = "tr", Query = "Synthetic question.",
                CanonicalWorkIds = [1], ContextVersion = 1
            }),
            ("faculty/assistant/run", new GetFacultyAssistantRunRequest
            {
                PersonelId = "missing-subject", RunId = Guid.NewGuid()
            }),
            ("evaluations/start", new StartArticleEvaluationRequest
            {
                PersonelId = "missing-subject", ProfileIds = ["paid-profile"]
            }),
            ("evaluations/status", new GetArticleEvaluationRequest
            {
                PersonelId = "missing-subject", RunId = Guid.NewGuid()
            })
        ];

        foreach ((string route, object body) in requests)
        {
            using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
                "/api/v1/" + route, body);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
