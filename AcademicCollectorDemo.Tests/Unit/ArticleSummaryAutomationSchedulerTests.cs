using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ArticleSummaryAutomationSchedulerTests
{
    [Fact]
    public void CreateInputHash_ReorderedEquivalentCandidates_IsStable()
    {
        AcademicWork first = Work("Same abstract", [
            new() { Kind = "Landing", Origin = "A", Url = "https://example.test/a" },
            new() { Kind = "Pdf", Origin = "B", Url = "https://example.test/paper.pdf" }
        ]);
        AcademicWork second = Work("Same abstract", [
            new() { Kind = "Pdf", Origin = "B", Url = "https://example.test/paper.pdf" },
            new() { Kind = "Landing", Origin = "A", Url = "https://example.test/a" }
        ]);

        Assert.Equal(
            ArticleSummaryAutomationScheduler.CreateInputHash([first]),
            ArticleSummaryAutomationScheduler.CreateInputHash([second]));
    }

    [Fact]
    public void CreateInputHash_ControlCharacters_DoNotCreateAmbiguousInputs()
    {
        AcademicWork first = Work("a\u001eb\u001fc", []);
        AcademicWork second = Work("a\u001eb", [
            new() { Kind = "Landing", Origin = "c", Url = "https://example.test/" }
        ]);

        Assert.NotEqual(
            ArticleSummaryAutomationScheduler.CreateInputHash([first]),
            ArticleSummaryAutomationScheduler.CreateInputHash([second]));
    }

    private static AcademicWork Work(string abstractText, List<AcademicWorkSource> sources) => new()
    {
        Provider = AcademicWorkProvider.OpenAlex,
        ProviderWorkId = "shared",
        Abstract = abstractText,
        Sources = sources,
        SyncedAt = DateTime.UtcNow
    };
}
