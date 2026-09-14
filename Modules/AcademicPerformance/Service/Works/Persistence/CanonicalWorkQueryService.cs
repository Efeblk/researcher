using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Persistence;

public sealed class CanonicalWorkQueryService
{
    private const int MaximumPageSize = 500;

    private readonly AcademicDbContext _dbContext;

    public CanonicalWorkQueryService(AcademicDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CanonicalPublicationListResponse> ListAsync(
        string personelId,
        CanonicalPublicationListRequest request,
        CancellationToken cancellationToken = default)
    {
        int skip = Math.Max(request.Skip, 0);
        int take = Math.Clamp(request.Take, 1, MaximumPageSize);
        IQueryable<CanonicalWork> query = _dbContext.CanonicalWorks.AsNoTracking()
            .Where(work => work.Researchers.Any(association => association.PersonelId == personelId));
        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            string searchText = request.SearchText.Trim();
            string doiSearchText = AcademicDoiNormalizer.Normalize(searchText);
            query = query.Where(work =>
                (work.NormalizedDoi != null && work.NormalizedDoi.Contains(doiSearchText)) ||
                work.Observations.Any(observation => observation.PersonelId == personelId &&
                    observation.AcademicWork!.PersonelId == personelId &&
                    ((observation.TitleObserved != null && observation.TitleObserved.Contains(searchText)) ||
                     (observation.AuthorsObserved != null && observation.AuthorsObserved.Contains(searchText)))));
        }

        int totalCount = await query.CountAsync(cancellationToken);
        List<int> ids = await query
            .OrderByDescending(work => work.Observations
                .Where(observation => observation.PersonelId == personelId &&
                    observation.AcademicWork!.PersonelId == personelId)
                .Max(observation => observation.PublicationYearObserved))
            .ThenBy(work => work.Observations
                .Where(observation => observation.PersonelId == personelId &&
                    observation.AcademicWork!.PersonelId == personelId)
                .Min(observation => observation.TitleObserved))
            .ThenBy(work => work.Id)
            .Skip(skip).Take(take)
            .Select(work => work.Id)
            .ToListAsync(cancellationToken);

        List<CanonicalWork> works = await _dbContext.CanonicalWorks.AsNoTracking()
            .Where(work => ids.Contains(work.Id) &&
                work.Researchers.Any(association => association.PersonelId == personelId))
            .Include(work => work.Observations.Where(observation => observation.PersonelId == personelId &&
                observation.AcademicWork!.PersonelId == personelId))
                .ThenInclude(observation => observation.AcademicWork!)
                    .ThenInclude(work => work.Sources)
            .Include(work => work.Observations.Where(observation => observation.PersonelId == personelId &&
                observation.AcademicWork!.PersonelId == personelId))
                .ThenInclude(observation => observation.AcademicWork!)
                    .ThenInclude(work => work.ResearchContext!)
                        .ThenInclude(context => context.Topics)
            .ToListAsync(cancellationToken);
        Dictionary<int, int> researcherCounts = await _dbContext.CanonicalResearcherWorks.AsNoTracking()
            .Where(association => ids.Contains(association.CanonicalWorkId))
            .GroupBy(association => association.CanonicalWorkId)
            .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);
        Dictionary<int, int> order = ids.Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index);

        return new()
        {
            PersonelId = personelId,
            TotalCount = totalCount,
            Skip = skip,
            Take = take,
            Entities = works.OrderBy(work => order[work.Id])
                .Select(work => Map(work, researcherCounts.GetValueOrDefault(work.Id)))
                .ToList()
        };
    }

    private static CanonicalPublicationDto Map(CanonicalWork work, int researcherCount)
    {
        List<CanonicalWorkObservation> observations = work.Observations
            .OrderByDescending(observation => observation.Provider == AcademicWorkProvider.Orcid)
            .ThenByDescending(MetadataScore)
            .ThenBy(observation => observation.AcademicWorkId)
            .ToList();
        return new()
        {
            Id = work.Id,
            IdentityKind = work.NormalizedDoi is null ? "SourceScoped" : "Doi",
            NormalizedDoi = work.NormalizedDoi,
            Title = FirstText(observations, observation => observation.TitleObserved),
            PublicationYear = observations.Select(observation => observation.PublicationYearObserved)
                .FirstOrDefault(value => value.HasValue),
            PublicationDate = observations.Select(observation => observation.PublicationDateObserved)
                .FirstOrDefault(value => value.HasValue),
            Category = observations.Select(observation => observation.CategoryObserved)
                .FirstOrDefault(category => category != AcademicWorkCategory.Unknown).ToString(),
            AuthorsObserved = FirstText(observations, observation => observation.AuthorsObserved),
            Publication = FirstText(observations, observation => observation.PublicationObserved),
            HasRetractionObservation = work.HasRetractionObservation,
            KnownResearcherCount = researcherCount,
            Observations = observations.Select(MapObservation).ToList()
        };
    }

    private static CanonicalPublicationObservationDto MapObservation(CanonicalWorkObservation observation) => new()
    {
        AcademicWorkId = observation.AcademicWorkId,
        Provider = observation.Provider.ToString(),
        ProviderWorkId = observation.ProviderWorkId,
        TitleObserved = observation.TitleObserved,
        DoiObserved = observation.DoiObserved,
        PublicationYearObserved = observation.PublicationYearObserved,
        PublicationDateObserved = observation.PublicationDateObserved,
        CategoryObserved = observation.CategoryObserved.ToString(),
        AuthorsObserved = observation.AuthorsObserved,
        PublicationObserved = observation.PublicationObserved,
        SourceId = observation.SourceId,
        SourceName = observation.SourceName,
        SourceType = observation.SourceType,
        Link = observation.Link,
        FullTextUrl = observation.FullTextUrl,
        License = observation.License,
        Version = observation.Version,
        IsRetracted = observation.IsRetracted,
        ObservedAt = observation.ObservedAt,
        SourceUrls = (observation.AcademicWork?.Sources ?? [])
            .OrderBy(source => source.Id)
            .Select(source => new AcademicWorkSourceDto
            {
                Url = source.Url,
                Kind = source.Kind,
                Origin = source.Origin,
                IsOpenAccess = source.IsOpenAccess
            }).ToList(),
        ResearchContext = MapResearchContext(observation.AcademicWork?.ResearchContext)
    };

    private static AcademicWorkResearchContextDto? MapResearchContext(
        AcademicWorkResearchContext? context)
    {
        if (context is null)
            return null;
        Dictionary<string, MetricQuality> quality = ResearchContextQuality.Read(context.ValueQualityJson);
        return new()
        {
            Provider = context.Provider,
            SourceWorkId = context.SourceWorkId,
            ParserVersion = context.ParserVersion,
            PayloadFingerprint = context.PayloadFingerprint,
            SourceSyncedAt = Utc(context.SourceSyncedAt),
            ProviderUpdatedAt = context.ProviderUpdatedAt.HasValue ? Utc(context.ProviderUpdatedAt.Value) : null,
            SourcePublicationYear = context.SourcePublicationYear,
            RawType = context.RawType,
            PrimarySourceType = context.PrimarySourceType,
            Fwci = Decimal(context.Fwci, quality.GetValueOrDefault("Fwci")),
            CitationNormalizedPercentile = Decimal(context.CitationNormalizedPercentile,
                quality.GetValueOrDefault("CitationNormalizedPercentile")),
            IsInTopOnePercent = Boolean(context.IsInTopOnePercent,
                quality.GetValueOrDefault("IsInTopOnePercent")),
            IsInTopTenPercent = Boolean(context.IsInTopTenPercent,
                quality.GetValueOrDefault("IsInTopTenPercent")),
            ParseQuality = context.ParseQuality,
            ParseQualityReason = context.ParseQualityReason,
            PrimaryTopicQuality = context.PrimaryTopicQuality,
            PrimaryTopicQualityReason = context.PrimaryTopicQualityReason,
            Topics = context.Topics.OrderBy(topic => topic.OriginalRank).Select(topic => new AcademicWorkTopicDto
            {
                TopicId = topic.TopicId,
                TopicName = topic.TopicName,
                SubfieldId = topic.SubfieldId,
                SubfieldName = topic.SubfieldName,
                FieldId = topic.FieldId,
                FieldName = topic.FieldName,
                DomainId = topic.DomainId,
                DomainName = topic.DomainName,
                OriginalRank = topic.OriginalRank,
                AssignmentScore = topic.AssignmentScore,
                ScoreQuality = topic.ScoreQuality,
                ScoreQualityReason = topic.ScoreQualityReason,
                IsPrimary = topic.IsPrimary
            }).ToList()
        };
    }

    private static ProviderDecimalMetricDto Decimal(decimal? value, MetricQuality? quality) => new()
    {
        Value = value,
        Quality = quality?.Quality ?? (value.HasValue ? "Available" : "Unknown"),
        QualityReason = quality?.Reason
    };

    private static ProviderBooleanMetricDto Boolean(bool? value, MetricQuality? quality) => new()
    {
        Value = value,
        Quality = quality?.Quality ?? (value.HasValue ? "Available" : "Unknown"),
        QualityReason = quality?.Reason
    };

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static int MetadataScore(CanonicalWorkObservation observation)
    {
        int score = 0;
        score += string.IsNullOrWhiteSpace(observation.TitleObserved) ? 0 : 1;
        score += observation.PublicationDateObserved.HasValue ? 1 : 0;
        score += string.IsNullOrWhiteSpace(observation.AuthorsObserved) ? 0 : 1;
        score += string.IsNullOrWhiteSpace(observation.PublicationObserved) ? 0 : 1;
        score += string.IsNullOrWhiteSpace(observation.FullTextUrl) ? 0 : 1;
        return score;
    }

    private static string? FirstText(
        List<CanonicalWorkObservation> observations,
        Func<CanonicalWorkObservation, string?> selector) => observations
            .Select(selector).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
