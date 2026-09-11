using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

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

    [Fact]
    public void BuildCoverage_ProviderDuplicates_CountsEachPublicationOnceAndSeparatesModelInput()
    {
        List<PublicationSummary> summaries =
        [
            new() { Id = 1, Doi = "10.1/pdf", Title = "PDF" },
            new() { Id = 2, Doi = "10.1/html", Title = "HTML" },
            new() { Id = 3, Doi = "10.1/abstract", Title = "Abstract" },
            new() { Id = 4, Title = "Metadata" }
        ];
        List<AcademicWork> works =
        [
            new() { Id = 10, Doi = "https://doi.org/10.1/pdf", FullTextUrl = "https://example.org/a.pdf" },
            new() { Id = 11, Doi = "10.1/pdf", FullTextUrl = "https://example.org/duplicate.pdf" },
            new() { Id = 12, Doi = "10.1/html" },
            new() { Id = 13, Doi = "10.1/abstract", Abstract = "Available abstract" }
        ];
        List<SavedArticleSummary> saved =
        [
            new() { Id = 1, OriginalAcademicWorkId = 10, SourceKind = "pdf", ExtractionVersion = "pdf-text-ocr-v1", SnapshotJson = Snapshot(false) },
            new() { Id = 2, OriginalAcademicWorkId = 12, SourceKind = "html", ExtractionVersion = "html-v1", SnapshotJson = Snapshot(true) },
            new() { Id = 3, OriginalAcademicWorkId = 11, SourceKind = "html", ExtractionVersion = "html-v1", SnapshotJson = Snapshot(false) }
        ];
        var submitted = new[] { new AcademicCollector.Analysis.Contracts.AnalysisPublication { Abstract = "Used" } };

        var coverage = ResearcherSnapshotBuilder.BuildCoverage(summaries, works, saved, submitted);

        Assert.Equal(4, coverage.TotalPublications);
        Assert.Equal(2, coverage.FullTextAvailable);
        Assert.Equal(1, coverage.PdfAvailable);
        Assert.Equal(1, coverage.OcrAvailable);
        Assert.Equal(1, coverage.HtmlAvailable);
        Assert.Equal(1, coverage.AbstractOnly);
        Assert.Equal(1, coverage.MetadataOnly);
        Assert.Equal(1, coverage.PartialFullText);
        Assert.Equal(3, coverage.ExcludedFromModelInput);
        Assert.Equal(1, coverage.AbstractsSubmittedToModel);

        static string Snapshot(bool partial) => JsonSerializer.Serialize(
            new SummarizeArticleRequest("en", "pdf", "hash", "v1", [new(1, "text")], 1, partial, null),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [Fact]
    public void BuildCoverage_DoiLessPublication_UsesUnambiguousTitleAndYearEvidence()
    {
        List<PublicationSummary> summaries =
        [
            new() { Id = 1, Title = "Saved full text", PublicationYear = 2024 },
            new() { Id = 2, Title = "Recovered abstract", PublicationYear = 2023 },
            new() { Id = 3, Title = "Conflicting DOI", PublicationYear = 2022 },
            new() { Id = 4, Title = "Conflicting DOI", PublicationYear = 2022, Doi = "10.1/owned" }
        ];
        List<AcademicWork> works =
        [
            new() { Id = 10, Title = "Saved full text", PublicationYear = 2024 },
            new() { Id = 11, Title = "Recovered abstract", PublicationYear = 2023, Abstract = "Evidence" },
            new() { Id = 12, Title = "Conflicting DOI", PublicationYear = 2022, Doi = "10.1/owned", Abstract = "Must not attach ambiguously" }
        ];
        List<SavedArticleSummary> saved =
        [
            new() { Id = 1, OriginalAcademicWorkId = 10, SourceKind = "html", ExtractionVersion = "html-v1", SnapshotJson = Snapshot(false) }
        ];

        ResearcherSourceCoverage coverage = ResearcherSnapshotBuilder.BuildCoverage(summaries, works, saved, []);

        Assert.Equal(1, coverage.FullTextAvailable);
        Assert.Equal(1, coverage.HtmlAvailable);
        Assert.Equal(1, coverage.AbstractOnly);
        Assert.Equal(2, coverage.MetadataOnly);

        static string Snapshot(bool partial) => JsonSerializer.Serialize(
            new SummarizeArticleRequest("en", "html", "hash", "v1", [new(null, "text")], 1, partial, null),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
