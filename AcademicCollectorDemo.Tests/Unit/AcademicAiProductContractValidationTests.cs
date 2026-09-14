using System.ComponentModel.DataAnnotations;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class AcademicAiProductContractValidationTests
{
    [Fact]
    public void PublicRequests_NullCollectionsFailValidationWithoutThrowing()
    {
        FacultyPrivateContext context = new() { ResearchGoals = null!, Courses = null! };
        StartFacultyAssistantRequest assistant = new()
        {
            PersonelId = "subject", ClientRequestId = Guid.NewGuid(), Mode = "TeachingHelp",
            Query = "question", CanonicalWorkIds = null!
        };
        CreateHrEvidenceDossierRequest dossier = new()
        {
            PersonelId = "subject", CanonicalWorkIds = null!
        };

        Assert.False(Valid(context));
        Assert.False(Valid(assistant));
        Assert.False(Valid(dossier));
    }

    [Fact]
    public void FacultyPrivateContext_OversizedEntryFailsValidation()
    {
        Assert.False(Valid(new FacultyPrivateContext { ResearchGoals = [new string('x', 301)] }));
    }

    [Fact]
    public void FacultyAssistantAnalysisRequest_NullExactTextFailsValidationWithoutThrowing()
    {
        FacultyAssistantAnalysisRequest request = new()
        {
            Mode = "OwnPaperMethods", Language = "en", Query = "question",
            EvidenceCatalogHash = new string('a', 64),
            Evidence = [new("work:1:snapshot:2:span:3", 1, 3, "src-1", 1, 0, 1,
                null!, "pdf", false)]
        };

        Assert.False(Valid(request));
    }

    private static bool Valid(object value) => Validator.TryValidateObject(value,
        new ValidationContext(value), [], validateAllProperties: true);
}
