using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;

public sealed record PublicationMetricSource(
    IReadOnlyCollection<PublicationMetricObservation> Observations,
    int UnmappedAcademicWorkCount,
    PublicationProviderMetricSource ProviderMetrics,
    IReadOnlyCollection<PublicationContextMetricObservation> ContextObservations);

public sealed record PublicationContextMetricObservation(
    int CanonicalWorkId,
    int AcademicWorkId,
    string ParseQuality,
    string PrimaryTopicQuality,
    string? PrimaryTopicId,
    string? PrimaryTopicName,
    string? SubfieldId,
    string? SubfieldName,
    string? FieldId,
    string? FieldName,
    int? SourcePublicationYear,
    string? RawType,
    string? PrimarySourceType,
    decimal? Fwci,
    string FwciQuality,
    decimal? CitationNormalizedPercentile,
    string CitationNormalizedPercentileQuality);

public sealed record PublicationProviderMetricSource(
    int? OpenAlexCitationCount,
    int? OpenAlexHIndex,
    int? OpenAlexI10Index,
    int? OpenAlexDocumentsCount,
    decimal? OpenAlexTwoYearMeanCitedness,
    DateTime? OpenAlexMetricsUpdatedAt,
    int? ScholarCitationCount,
    int? ScholarHIndex,
    int? ScholarI10Index,
    int? ScholarDocumentsCount,
    int? ScholarCitationCountRecent,
    int? ScholarHIndexRecent,
    int? ScholarI10IndexRecent,
    int? ScholarMetricsSinceYear,
    DateTime? ScholarMetricsUpdatedAt,
    int? WosCitationCount,
    int? WosHIndex,
    int? WosDocumentsCount,
    DateTime? WosMetricsUpdatedAt);

public sealed class PublicationMetricSourceLoader(AcademicDbContext database)
{
    public async Task<PublicationMetricSource> LoadAsync(
        string personelId,
        CancellationToken cancellationToken)
    {
        List<PublicationMetricObservation> observations = await database.CanonicalWorkObservations
            .AsNoTracking()
            .Where(observation => observation.PersonelId == personelId &&
                observation.AcademicWork!.PersonelId == personelId &&
                database.CanonicalResearcherWorks.Any(association =>
                    association.PersonelId == personelId &&
                    association.CanonicalWorkId == observation.CanonicalWorkId))
            .Select(observation => new PublicationMetricObservation(
                observation.CanonicalWorkId,
                observation.CanonicalWork!.NormalizedDoi != null &&
                    observation.CanonicalWork.NormalizedDoi.Trim() != string.Empty,
                observation.PublicationYearObserved,
                observation.PublicationDateObserved,
                observation.CategoryObserved,
                observation.AcademicWork!.Abstract != null &&
                    observation.AcademicWork.Abstract.Replace("\t", string.Empty)
                        .Replace("\r", string.Empty).Replace("\n", string.Empty)
                        .Trim() != string.Empty,
                observation.AcademicWork!.Link != null &&
                    observation.AcademicWork.Link.Replace("\t", string.Empty)
                        .Replace("\r", string.Empty).Replace("\n", string.Empty)
                        .Trim() != string.Empty ||
                observation.AcademicWork.FullTextUrl != null &&
                    observation.AcademicWork.FullTextUrl.Replace("\t", string.Empty)
                        .Replace("\r", string.Empty).Replace("\n", string.Empty)
                        .Trim() != string.Empty ||
                observation.AcademicWork.Sources.Any(source =>
                    source.Url.Replace("\t", string.Empty)
                        .Replace("\r", string.Empty).Replace("\n", string.Empty)
                        .Trim() != string.Empty),
                observation.Provider,
                observation.AcademicWork.CitedByCount))
            .ToListAsync(cancellationToken);

        int unmappedAcademicWorkCount = await database.AcademicWorks.AsNoTracking()
            .CountAsync(work => work.PersonelId == personelId &&
                !database.CanonicalWorkObservations.Any(observation =>
                    observation.AcademicWorkId == work.Id &&
                    observation.PersonelId == personelId &&
                    database.CanonicalResearcherWorks.Any(association =>
                        association.PersonelId == personelId &&
                        association.CanonicalWorkId == observation.CanonicalWorkId)),
                cancellationToken);
        PublicationProviderMetricSource providerMetrics = await database.Researchers.AsNoTracking()
            .Where(researcher => researcher.PersonelId == personelId)
            .Select(researcher => new PublicationProviderMetricSource(
                researcher.OpenAlexCitationCount,
                researcher.OpenAlexHIndex,
                researcher.OpenAlexI10Index,
                researcher.OpenAlexDocumentsCount,
                researcher.OpenAlexTwoYearMeanCitedness,
                researcher.OpenAlexMetricsUpdatedAt,
                researcher.ScholarCitationCount,
                researcher.ScholarHIndex,
                researcher.ScholarI10Index,
                researcher.ScholarDocumentsCount,
                researcher.ScholarCitationCountRecent,
                researcher.ScholarHIndexRecent,
                researcher.ScholarI10IndexRecent,
                researcher.ScholarMetricsSinceYear,
                researcher.ScholarMetricsUpdatedAt,
                researcher.WosCitationCount,
                researcher.WosHIndex,
                researcher.WosDocumentsCount,
                researcher.WosMetricsUpdatedAt))
            .SingleAsync(cancellationToken);
        List<ContextProjection> contextRows = await database.AcademicWorkResearchContexts
            .AsNoTracking()
            .Where(context => context.Provider == "OpenAlex" &&
                context.AcademicWork!.Provider == AcademicWorkProvider.OpenAlex &&
                context.AcademicWork.PersonelId == personelId &&
                context.AcademicWork.CanonicalObservation != null &&
                context.AcademicWork.CanonicalObservation.PersonelId == personelId &&
                database.CanonicalResearcherWorks.Any(association =>
                    association.PersonelId == personelId &&
                    association.CanonicalWorkId ==
                        context.AcademicWork.CanonicalObservation.CanonicalWorkId))
            .Select(context => new ContextProjection(
                context.AcademicWork!.CanonicalObservation!.CanonicalWorkId,
                context.AcademicWorkId,
                context.ParseQuality,
                context.PrimaryTopicQuality,
                context.Topics.Where(topic => topic.IsPrimary)
                    .Select(topic => topic.TopicId).FirstOrDefault(),
                context.Topics.Where(topic => topic.IsPrimary)
                    .Select(topic => topic.TopicName).FirstOrDefault(),
                context.Topics.Where(topic => topic.IsPrimary)
                    .Select(topic => topic.SubfieldId).FirstOrDefault(),
                context.Topics.Where(topic => topic.IsPrimary)
                    .Select(topic => topic.SubfieldName).FirstOrDefault(),
                context.Topics.Where(topic => topic.IsPrimary)
                    .Select(topic => topic.FieldId).FirstOrDefault(),
                context.Topics.Where(topic => topic.IsPrimary)
                    .Select(topic => topic.FieldName).FirstOrDefault(),
                context.SourcePublicationYear,
                context.RawType,
                context.PrimarySourceType,
                context.Fwci,
                context.CitationNormalizedPercentile,
                context.ValueQualityJson))
            .ToListAsync(cancellationToken);
        List<PublicationContextMetricObservation> contextObservations = contextRows.Select(row =>
        {
            Dictionary<string, MetricQuality> quality = ResearchContextQuality.Read(row.ValueQualityJson);
            return new PublicationContextMetricObservation(
                row.CanonicalWorkId, row.AcademicWorkId, row.ParseQuality,
                row.PrimaryTopicQuality, row.PrimaryTopicId, row.PrimaryTopicName,
                row.SubfieldId, row.SubfieldName, row.FieldId, row.FieldName,
                row.SourcePublicationYear, row.RawType, row.PrimarySourceType,
                row.Fwci, quality.GetValueOrDefault("Fwci")?.Quality ??
                    (row.Fwci.HasValue ? "Available" : "Unknown"),
                row.CitationNormalizedPercentile,
                quality.GetValueOrDefault("CitationNormalizedPercentile")?.Quality ??
                    (row.CitationNormalizedPercentile.HasValue ? "Available" : "Unknown"));
        }).ToList();
        return new(observations, unmappedAcademicWorkCount, providerMetrics, contextObservations);
    }

    private sealed record ContextProjection(
        int CanonicalWorkId,
        int AcademicWorkId,
        string ParseQuality,
        string PrimaryTopicQuality,
        string? PrimaryTopicId,
        string? PrimaryTopicName,
        string? SubfieldId,
        string? SubfieldName,
        string? FieldId,
        string? FieldName,
        int? SourcePublicationYear,
        string? RawType,
        string? PrimarySourceType,
        decimal? Fwci,
        decimal? CitationNormalizedPercentile,
        string ValueQualityJson);
}
