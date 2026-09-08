using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ResearcherSnapshotBuilderTests
{
    [Fact]
    public void Build_AmbiguousTitles_DoesNotAttachAnotherPublicationsAbstract()
    {
        var summaries = new List<PublicationSummary>
        {
            new() { Id = 1, Title = "Same title", PublicationYear = 2025, Doi = "10.1/first" },
            new() { Id = 2, Title = "Same title", PublicationYear = 2025, Doi = "10.1/second" }
        };
        var works = new List<AcademicWork>
        {
            new() { Id = 1, Title = "Same title", PublicationYear = 2025, Abstract = "Ambiguous source without DOI." },
            new() { Id = 2, Title = "Different provider title", Doi = "https://doi.org/10.1/second", Abstract = "Correct source for the second publication." }
        };
        var snapshot = ResearcherSnapshotBuilder.Build(new Researcher
        {
            PersonelId = "00123-A" }, summaries, works, new());
        Assert.Null(snapshot.Publications[0].Abstract);
        Assert.Equal(works[1].Abstract, snapshot.Publications[1].Abstract);
    }

    [Fact]
    public void Build_LargeTurkishInput_BoundsUtf8BytesAndKeepsTotalCount()
    {
        var summaries = Enumerable.Range(1, 20).Select(id => new PublicationSummary
        {
            Id = id, Title = "Kıyı sularında kalite izleme", PublicationYear = 2000 + id
        }).ToList();
        var snapshot = ResearcherSnapshotBuilder.Build(new Researcher
        {
            PersonelId = "00123-A" }, summaries, [],
            new() { MaximumPublications = 3, MaximumTextBytes = 200, Language = "tr" });
        Assert.Equal(20, snapshot.TotalPublicationCount);
        Assert.Equal(3, snapshot.Publications.Count);
        Assert.Equal("publication-20", snapshot.Publications[0].Id);
        Assert.Equal("tr", snapshot.Language);
        Assert.True(snapshot.Publications.Sum(value => Encoding.UTF8.GetByteCount(value.Title)) <= 200);
    }
}
