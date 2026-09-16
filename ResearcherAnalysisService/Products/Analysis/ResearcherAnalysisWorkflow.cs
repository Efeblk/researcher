using System.Data;
using System.Text.Json;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Products.Analysis;

public sealed class ResearcherAnalysisWorkflow(
    AnalysisDbContext database, AnalysisServiceClient client, IOptions<AnalysisServiceOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SavedResearcherAnalysisResponse?> AnalyzeAsync(string personelId, DateTimeOffset? snapshotAt, CancellationToken cancellationToken)
    {
        AnalyzeResearcherRequest snapshot;
        // Capture a consistent input, then release SQL locks before waiting for the model.
        await using (var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            var researcher = await database.Researchers.AsNoTracking()
                .Include(value => value.GoogleScholarProfile).Include(value => value.WebOfScienceProfile)
                .Include(value => value.OpenAlexProfile)
                .SingleOrDefaultAsync(value => value.PersonelId == personelId, cancellationToken);
            if (researcher is null)
                return null;
            var summaries = await database.PublicationSummaries.AsNoTracking()
                .Where(value => value.PersonelId == personelId).ToListAsync(cancellationToken);
            var works = await database.AcademicWorks.AsNoTracking()
                .Where(value => value.PersonelId == personelId)
                .Select(value => new ResearcherAnalysisService.SourceData.Works.AcademicWork
                {
                    Id = value.Id, PersonelId = value.PersonelId, Title = value.Title, Doi = value.Doi,
                    PublicationYear = value.PublicationYear, Abstract = value.Abstract, Keywords = value.Keywords,
                    FullTextUrl = value.FullTextUrl, Sources = value.Sources.ToList(),
                    CanonicalObservation = value.CanonicalObservation == null ? null : new()
                    {
                        CanonicalWorkId = value.CanonicalObservation.CanonicalWorkId,
                        PersonelId = value.CanonicalObservation.PersonelId,
                        AcademicWorkId = value.CanonicalObservation.AcademicWorkId
                    }
                }).ToListAsync(cancellationToken);
            var articleSummaries = await database.ArticleSummaries.AsNoTracking()
                .Where(value => value.PersonelId == personelId).ToListAsync(cancellationToken);
            snapshot = ResearcherSnapshotBuilder.Build(researcher, summaries, works, options.Value, articleSummaries);
            snapshot.SnapshotAt = snapshotAt?.ToUniversalTime() ?? snapshot.SnapshotAt;
            await transaction.CommitAsync(cancellationToken);
        }
        if (snapshot.Publications.Count == 0)
            throw new AnalysisInputUnavailableException();

        ResearcherAnalysisReport report = await client.AnalyzeAsync(snapshot, cancellationToken);
        report.SourceCoverage = snapshot.SourceCoverage;
        SavedResearcherAnalysis saved = new()
        {
            PersonelId = personelId,
            SavedAt = DateTimeOffset.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions),
            ReportJson = JsonSerializer.Serialize(report, JsonOptions)
        };
        database.ResearcherAnalyses.Add(saved);
        await database.SaveChangesAsync(cancellationToken);
        return new(saved.Id, saved.SavedAt, report, snapshot.SourceCoverage!);
    }

    public async Task<SavedResearcherAnalysisResponse?> GetLatestAsync(string personelId, CancellationToken cancellationToken)
    {
        SavedResearcherAnalysis? saved = await database.ResearcherAnalyses.AsNoTracking()
            .Where(value => value.PersonelId == personelId)
            .OrderByDescending(value => value.Id).FirstOrDefaultAsync(cancellationToken);
        if (saved is null) return null;
        ResearcherAnalysisReport report = JsonSerializer.Deserialize<ResearcherAnalysisReport>(saved.ReportJson, JsonOptions)!;
        AnalyzeResearcherRequest? snapshot = JsonSerializer.Deserialize<AnalyzeResearcherRequest>(saved.SnapshotJson, JsonOptions);
        return new(saved.Id, saved.SavedAt, report, snapshot?.SourceCoverage ?? report.SourceCoverage);
    }

    public async Task<ResearcherSourceCoverage?> GetCoverageAsync(string personelId, CancellationToken cancellationToken)
    {
        bool exists = await database.Researchers.AsNoTracking().AnyAsync(value => value.PersonelId == personelId, cancellationToken);
        if (!exists) return null;
        List<ResearcherAnalysisService.SourceData.Works.PublicationSummary> summaries = await database.PublicationSummaries.AsNoTracking()
            .Where(value => value.PersonelId == personelId).ToListAsync(cancellationToken);
        List<ResearcherAnalysisService.SourceData.Works.AcademicWork> works = await database.AcademicWorks.AsNoTracking()
            .Include(value => value.Sources).Include(value => value.CanonicalObservation)
            .Where(value => value.PersonelId == personelId).ToListAsync(cancellationToken);
        List<ArticleSummaries.SavedArticleSummary> saved = await database.ArticleSummaries.AsNoTracking()
            .Where(value => value.PersonelId == personelId).ToListAsync(cancellationToken);
        return ResearcherSnapshotBuilder.BuildCoverage(summaries, works, saved, []);
    }

    public async Task<ResearcherAnalysisReadResponse?> GetAnalysisAsync(
        string personelId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        ResearcherSourceCoverage? coverage = await GetCoverageAsync(personelId, cancellationToken);
        if (coverage is null)
            return null;
        SavedResearcherAnalysisResponse? analysis = await GetLatestAsync(personelId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(coverage, analysis);
    }
}
