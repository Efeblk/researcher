using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.GraphProjection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class AcademicGraphProjectionPersistenceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task ExportAsync_TwentyWorks_GraphEvidenceTraversalMatchesAuthoritativeSql()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string personelId = "graph-" + Guid.NewGuid().ToString("N");
        Researcher researcher = new() { PersonelId = personelId };
        database.Researchers.Add(researcher);
        List<CanonicalWork> works = [];
        for (int index = 0; index < 20; index++)
        {
            CanonicalWork work = new()
            {
                NormalizedDoi = $"10.9200/{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            works.Add(work);
            database.CanonicalResearcherWorks.Add(new()
            {
                CanonicalWork = work,
                Researcher = researcher,
                PersonelId = personelId,
                LastObservedAt = DateTime.UtcNow
            });
            SavedArticleSummary summary = new()
            {
                PersonelId = personelId, OriginalAcademicWorkId = 1000 + index,
                SavedAt = DateTimeOffset.UtcNow, SourceHash = $"{index:x64}", SourceKind = "Pdf",
                ExtractionVersion = "test-v1", SnapshotJson = "{}", ReportJson = "{}"
            };
            string evidenceText = $"public evidence {index}";
            ArticleSourceSnapshot source = new()
            {
                CanonicalWork = work, ExtractedTextHash = $"{index + 100:x64}",
                SourceKind = "Pdf", ExtractionVersion = "test-v1", CreatedAt = DateTimeOffset.UtcNow,
                Spans = [new() { SourceId = "p1-s1", Ordinal = 0, PageNumber = 1,
                    StartOffset = 0, EndOffset = evidenceText.Length, Text = evidenceText }]
            };
            CanonicalArticleAnalysisRun run = new()
            {
                CanonicalWork = work, ArticleSourceSnapshot = source, SavedArticleSummary = summary,
                AnalyzedAt = DateTimeOffset.UtcNow, SourceAcquiredAt = DateTimeOffset.UtcNow,
                SourceOrigin = "Synthetic", Language = "en", PolicyVersion = "test-policy",
                Model = "synthetic", PromptVersion = "test-prompt", ExtractionMethod = "test",
                ProcessedChunks = 1, TotalChunks = 1, ProcessedPages = 1, TextBearingPages = 1,
                TotalPages = 1, OmissionReasonsJson = "[]", VerificationStatus = "synthetic",
                VerificationModel = "synthetic", VerificationPromptVersion = "synthetic",
                Claims = [new() { Section = "findings", SectionOrder = 0, Ordinal = 0,
                    Text = $"claim {index}" }]
            };
            database.ArticleSummaries.Add(summary);
            database.ArticleSourceSnapshots.Add(source);
            database.CanonicalArticleAnalysisRuns.Add(run);
        }
        await database.SaveChangesAsync();
        List<long> claimIds = await database.CanonicalArticleClaims.Where(value =>
            works.Select(work => work.Id).Contains(value.CanonicalArticleAnalysisRun!.CanonicalWorkId))
            .OrderBy(value => value.Id).Select(value => value.Id).ToListAsync();
        List<long> spanIds = await database.ArticleSourceSpans.Where(value =>
            works.Select(work => work.Id).Contains(value.ArticleSourceSnapshot!.CanonicalWorkId))
            .OrderBy(value => value.Id).Select(value => value.Id).ToListAsync();
        for (int index = 0; index < claimIds.Count; index++)
            database.CanonicalArticleClaimEvidence.Add(new()
            {
                CanonicalArticleClaimId = claimIds[index], ArticleSourceSpanId = spanIds[index], Ordinal = 0
            });
        await database.SaveChangesAsync();

        AcademicGraphProjectionBundle bundle = (await new AcademicGraphProjectionService(database)
            .ExportAsync(personelId, works.Select(work => work.Id).ToArray(), CancellationToken.None))!;
        string[] sqlEvidence = await database.CanonicalArticleClaimEvidence.AsNoTracking()
            .Where(value => claimIds.Contains(value.CanonicalArticleClaimId))
            .OrderBy(value => value.ArticleSourceSpanId)
            .Select(value => "span:" + value.ArticleSourceSpanId).ToArrayAsync();
        string[] graphEvidence = bundle.Relationships.Where(value => value.Type == "CITES_EVIDENCE")
            .SelectMany(value => value.EvidenceIds).Order().ToArray();
        var evaluation = AcademicGraphProjectionService.Evaluate(
            [(sqlEvidence, (IReadOnlyCollection<string>)graphEvidence)]);

        Assert.Equal(20, bundle.Nodes.Count(value => value.Type == "CanonicalWork"));
        Assert.Equal(1m, evaluation.ExactSetAgreementProportion);
        Assert.Equal(0, evaluation.MissingFromGraphCount);
        Assert.Equal(0, evaluation.UnexpectedGraphCount);
        Assert.Equal("NotRun", bundle.Neo4jExecutionStatus);
        Assert.Equal("NotRun", bundle.AdoptionEvaluationStatus);
    }
}
