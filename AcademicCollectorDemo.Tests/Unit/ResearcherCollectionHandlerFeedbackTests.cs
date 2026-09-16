using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ResearcherCollectionHandlerFeedbackTests
{
    [Fact]
    public void PartialEnrichment_FirstCallUnauthorized_ReportsAuthAndRemainingAfterCache()
    {
        ProviderCollectionFeedback feedback = new() { ExpectedCount = 6 };
        feedback.Reasons.Add(new() { Code = "Cached", Description = "cached", AffectedCount = 2 });

        ResearcherCollectionHandler.PartialEnrichment(feedback, 0,
            new HttpRequestException("secret", null, HttpStatusCode.Unauthorized));

        Assert.Contains(feedback.Reasons, reason =>
            reason.Code == "AuthenticationOrConfiguration" && reason.AffectedCount == 1);
        Assert.Contains(feedback.Reasons, reason =>
            reason.Code == "NotAttempted" && reason.AffectedCount == 3);
        Assert.DoesNotContain(feedback.Reasons, reason => reason.Description.Contains("secret"));
    }
}
