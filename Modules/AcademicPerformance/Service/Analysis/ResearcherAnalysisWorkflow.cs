using System.Data;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AcademicCollector.Analysis.Contracts;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;

public sealed class ResearcherAnalysisWorkflow(
    AcademicDbContext database, AnalysisServiceClient client, IOptions<AnalysisServiceOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SavedResearcherAnalysisResponse?> AnalyzeAsync(string? personelId, int? researcherId,
        DateTimeOffset? snapshotAt, CancellationToken cancellationToken)
    {
        AnalyzeResearcherRequest snapshot;
        // Capture a consistent input, then release SQL locks before waiting for the model.
        await using (var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            Researcher? researcher = await ResolveResearcherAsync(personelId, researcherId,
                database.Researchers.AsNoTracking()
                .Include(value => value.GoogleScholarProfile).Include(value => value.WebOfScienceProfile)
                .Include(value => value.OpenAlexProfile), cancellationToken);
            if (researcher is null)
                return null;
            int collectorResearcherId = researcher.Id;
            var summaries = await database.PublicationSummaries.AsNoTracking()
                .Where(value => value.ResearcherId == collectorResearcherId).ToListAsync(cancellationToken);
            var works = await database.AcademicWorks.AsNoTracking()
                .Where(value => value.ResearcherId == collectorResearcherId)
                .Select(value => new Works.Models.AcademicWork
                {
                    Id = value.Id, Title = value.Title, Doi = value.Doi,
                    PublicationYear = value.PublicationYear, Abstract = value.Abstract, Keywords = value.Keywords
                }).ToListAsync(cancellationToken);
            snapshot = ResearcherSnapshotBuilder.Build(researcher, summaries, works, options.Value);
            snapshot.SnapshotAt = snapshotAt?.ToUniversalTime() ?? snapshot.SnapshotAt;
            await transaction.CommitAsync(cancellationToken);
        }
        if (snapshot.Publications.Count == 0)
            throw new AnalysisInputUnavailableException();

        ResearcherAnalysisReport report = await client.AnalyzeAsync(snapshot, cancellationToken);
        SavedResearcherAnalysis saved = new()
        {
            ResearcherId = snapshot.ResearcherId,
            SavedAt = DateTimeOffset.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions),
            ReportJson = JsonSerializer.Serialize(report, JsonOptions)
        };
        database.ResearcherAnalyses.Add(saved);
        await database.SaveChangesAsync(cancellationToken);
        return new(saved.Id, saved.SavedAt, report);
    }

    public async Task<SavedResearcherAnalysisResponse?> GetLatestAsync(string? personelId, int? researcherId,
        CancellationToken cancellationToken)
    {
        Researcher? researcher = await ResolveResearcherAsync(personelId, researcherId,
            database.Researchers.AsNoTracking(), cancellationToken);
        if (researcher is null)
            return null;
        SavedResearcherAnalysis? saved = await database.ResearcherAnalyses.AsNoTracking()
            .Where(value => value.ResearcherId == researcher.Id)
            .OrderByDescending(value => value.Id).FirstOrDefaultAsync(cancellationToken);
        return saved is null ? null : new(saved.Id, saved.SavedAt,
            JsonSerializer.Deserialize<ResearcherAnalysisReport>(saved.ReportJson, JsonOptions)!);
    }

    private static async Task<Researcher?> ResolveResearcherAsync(string? personelId, int? researcherId,
        IQueryable<Researcher> researchers, CancellationToken cancellationToken)
    {
        if (personelId is not null)
        {
            string normalizedPersonelId = personelId.Trim();
            Researcher? researcher = await researchers.SingleOrDefaultAsync(
                value => value.PersonelId == normalizedPersonelId, cancellationToken);
            if (researcher is not null && researcherId is not null && researcher.Id != researcherId)
                throw new ResearcherIdentityMismatchException();
            return researcher;
        }

        return await researchers.SingleOrDefaultAsync(value => value.Id == researcherId, cancellationToken);
    }
}
