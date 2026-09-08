using System.Data;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AcademicCollector.Analysis.Contracts;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;

public sealed class ResearcherAnalysisWorkflow(
    AcademicDbContext database, AnalysisServiceClient client, IOptions<AnalysisServiceOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SavedResearcherAnalysisResponse?> AnalyzeAsync(int researcherId, CancellationToken cancellationToken)
    {
        AnalyzeResearcherRequest snapshot;
        // Capture a consistent input, then release SQL locks before waiting for the model.
        await using (var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            var researcher = await database.Researchers.AsNoTracking()
                .Include(value => value.GoogleScholarProfile).Include(value => value.WebOfScienceProfile)
                .Include(value => value.OpenAlexProfile)
                .SingleOrDefaultAsync(value => value.Id == researcherId, cancellationToken);
            if (researcher is null)
                return null;
            var summaries = await database.PublicationSummaries.AsNoTracking()
                .Where(value => value.ResearcherId == researcherId).ToListAsync(cancellationToken);
            var works = await database.AcademicWorks.AsNoTracking()
                .Where(value => value.ResearcherId == researcherId)
                .Select(value => new Works.Models.AcademicWork
                {
                    Id = value.Id, Title = value.Title, Doi = value.Doi,
                    PublicationYear = value.PublicationYear, Abstract = value.Abstract, Keywords = value.Keywords
                }).ToListAsync(cancellationToken);
            snapshot = ResearcherSnapshotBuilder.Build(researcher, summaries, works, options.Value);
            await transaction.CommitAsync(cancellationToken);
        }
        if (snapshot.Publications.Count == 0)
            throw new AnalysisInputUnavailableException();

        ResearcherAnalysisReport report = await client.AnalyzeAsync(snapshot, cancellationToken);
        SavedResearcherAnalysis saved = new()
        {
            ResearcherId = researcherId,
            SavedAt = DateTimeOffset.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions),
            ReportJson = JsonSerializer.Serialize(report, JsonOptions)
        };
        database.ResearcherAnalyses.Add(saved);
        await database.SaveChangesAsync(cancellationToken);
        return new(saved.Id, saved.SavedAt, report);
    }

    public async Task<SavedResearcherAnalysisResponse?> GetLatestAsync(int researcherId, CancellationToken cancellationToken)
    {
        SavedResearcherAnalysis? saved = await database.ResearcherAnalyses.AsNoTracking()
            .Where(value => value.ResearcherId == researcherId)
            .OrderByDescending(value => value.Id).FirstOrDefaultAsync(cancellationToken);
        return saved is null ? null : new(saved.Id, saved.SavedAt,
            JsonSerializer.Deserialize<ResearcherAnalysisReport>(saved.ReportJson, JsonOptions)!);
    }
}
