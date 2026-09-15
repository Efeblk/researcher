using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.Analysis;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Products.ArticleSummaries;

namespace ResearcherAnalysisService.Tests;

public sealed class ResearcherSnapshotBuilderTests
{
    [Fact]
    public void Build_CanonicalDoiLessPublications_AttachesEvidenceByCanonicalMembership()
    {
        const string personelId = "00123-A";
        List<PublicationSummary> summaries =
        [
            new() { Id = 1, PersonelId = personelId, CanonicalWorkId = 101, Title = "Same title", PublicationYear = 2025 },
            new() { Id = 2, PersonelId = personelId, CanonicalWorkId = 102, Title = "Same title", PublicationYear = 2025 }
        ];
        List<AcademicWork> works =
        [
            new() { Id = 10, PersonelId = personelId, Title = "Same title", PublicationYear = 2025,
                Abstract = "First abstract", CanonicalObservation = new() { AcademicWorkId = 10, PersonelId = personelId, CanonicalWorkId = 101 } },
            new() { Id = 11, PersonelId = personelId, Title = "Same title", PublicationYear = 2025,
                Abstract = "Second abstract", CanonicalObservation = new() { AcademicWorkId = 11, PersonelId = personelId, CanonicalWorkId = 102 } }
        ];

        AnalyzeResearcherRequest snapshot = ResearcherSnapshotBuilder.Build(
            new Researcher { PersonelId = personelId }, summaries, works, new());

        Assert.Equal("First abstract", snapshot.Publications.Single(value => value.Id == "publication-1").Abstract);
        Assert.Equal("Second abstract", snapshot.Publications.Single(value => value.Id == "publication-2").Abstract);
        Assert.Equal(2, snapshot.SourceCoverage!.AbstractOnly);
    }

    [Fact]
    public void Build_AmbiguousTitles_DoesNotAttachAnotherPublicationsAbstract()
    {
        const string personelId = "00123-A";
        var summaries = new List<PublicationSummary>
        {
            new() { Id = 1, PersonelId = personelId, CanonicalWorkId = 101, Title = "Same title", PublicationYear = 2025 },
            new() { Id = 2, PersonelId = personelId, CanonicalWorkId = 102, Title = "Same title", PublicationYear = 2025 }
        };
        var works = new List<AcademicWork>
        {
            new() { Id = 1, PersonelId = personelId, Abstract = "Unrelated source.", CanonicalObservation = new() { PersonelId = personelId, CanonicalWorkId = 999 } },
            new() { Id = 2, PersonelId = personelId, Abstract = "Correct source for the second publication.", CanonicalObservation = new() { PersonelId = personelId, CanonicalWorkId = 102 } }
        };
        var snapshot = ResearcherSnapshotBuilder.Build(new Researcher
        { PersonelId = personelId }, summaries, works, new());
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
        const string personelId = "coverage";
        List<PublicationSummary> summaries =
        [
            new() { Id = 1, PersonelId = personelId, CanonicalWorkId = 101, Title = "PDF" },
            new() { Id = 2, PersonelId = personelId, CanonicalWorkId = 102, Title = "HTML" },
            new() { Id = 3, PersonelId = personelId, CanonicalWorkId = 103, Title = "Abstract" },
            new() { Id = 4, PersonelId = personelId, CanonicalWorkId = 104, Title = "Metadata" }
        ];
        List<AcademicWork> works =
        [
            LinkedWork(10, personelId, 101, fullTextUrl: "https://example.org/a.pdf"),
            LinkedWork(11, personelId, 101, fullTextUrl: "https://example.org/duplicate.pdf"),
            LinkedWork(12, personelId, 102),
            LinkedWork(13, personelId, 103, abstractText: "Available abstract")
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
        const string personelId = "doi-less";
        List<PublicationSummary> summaries =
        [
            new() { Id = 1, PersonelId = personelId, CanonicalWorkId = 101, Title = "Saved full text" },
            new() { Id = 2, PersonelId = personelId, CanonicalWorkId = 102, Title = "Recovered abstract" },
            new() { Id = 3, PersonelId = personelId, CanonicalWorkId = 103, Title = "No evidence" },
            new() { Id = 4, PersonelId = personelId, CanonicalWorkId = 104, Title = "Canonical evidence" }
        ];
        List<AcademicWork> works =
        [
            LinkedWork(10, personelId, 101),
            LinkedWork(11, personelId, 102, abstractText: "Evidence"),
            LinkedWork(12, personelId, 104, abstractText: "Canonical evidence")
        ];
        List<SavedArticleSummary> saved =
        [
            new() { Id = 1, OriginalAcademicWorkId = 10, SourceKind = "html", ExtractionVersion = "html-v1", SnapshotJson = Snapshot(false) }
        ];

        ResearcherSourceCoverage coverage = ResearcherSnapshotBuilder.BuildCoverage(summaries, works, saved, []);

        Assert.Equal(1, coverage.FullTextAvailable);
        Assert.Equal(1, coverage.HtmlAvailable);
        Assert.Equal(2, coverage.AbstractOnly);
        Assert.Equal(1, coverage.MetadataOnly);

        static string Snapshot(bool partial) => JsonSerializer.Serialize(
            new SummarizeArticleRequest("en", "html", "hash", "v1", [new(null, "text")], 1, partial, null),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static AcademicWork LinkedWork(
        int id, string personelId, int canonicalWorkId,
        string? abstractText = null, string? fullTextUrl = null) => new()
    {
        Id = id,
        PersonelId = personelId,
        Abstract = abstractText,
        FullTextUrl = fullTextUrl,
        CanonicalObservation = new()
        {
            AcademicWorkId = id,
            PersonelId = personelId,
            CanonicalWorkId = canonicalWorkId
        }
    };
}
