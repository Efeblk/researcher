using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

public sealed class AcademicWorkResearchContextSynchronizer(AcademicDbContext database)
{
    public async Task SynchronizeAsync(string personelId, CancellationToken cancellationToken = default)
    {
        List<AcademicWork> works = await database.AcademicWorks
            .Include(work => work.ResearchContext!).ThenInclude(context => context.Topics)
            .Where(work => work.PersonelId == personelId &&
                work.Provider == AcademicWorkProvider.OpenAlex)
            .OrderBy(work => work.Id)
            .ToListAsync(cancellationToken);
        foreach (AcademicWork work in works)
            Reconcile(work, OpenAlexResearchContextParser.Parse(work));
        await database.SaveChangesAsync(cancellationToken);
    }

    private void Reconcile(AcademicWork work, AcademicWorkResearchContext parsed)
    {
        AcademicWorkResearchContext target = work.ResearchContext ?? new()
        {
            AcademicWorkId = work.Id,
            AcademicWork = work
        };
        if (work.ResearchContext is null)
        {
            work.ResearchContext = target;
            database.AcademicWorkResearchContexts.Add(target);
        }

        target.Provider = parsed.Provider;
        target.SourceWorkId = parsed.SourceWorkId;
        target.ParserVersion = parsed.ParserVersion;
        target.PayloadFingerprint = parsed.PayloadFingerprint;
        target.SourceSyncedAt = parsed.SourceSyncedAt;
        target.ProviderUpdatedAt = parsed.ProviderUpdatedAt;
        target.SourcePublicationYear = parsed.SourcePublicationYear;
        target.RawType = parsed.RawType;
        target.PrimarySourceType = parsed.PrimarySourceType;
        target.Fwci = parsed.Fwci;
        target.CitationNormalizedPercentile = parsed.CitationNormalizedPercentile;
        target.IsInTopOnePercent = parsed.IsInTopOnePercent;
        target.IsInTopTenPercent = parsed.IsInTopTenPercent;
        target.ParseQuality = parsed.ParseQuality;
        target.ParseQualityReason = parsed.ParseQualityReason;
        target.PrimaryTopicQuality = parsed.PrimaryTopicQuality;
        target.PrimaryTopicQualityReason = parsed.PrimaryTopicQualityReason;
        target.ValueQualityJson = parsed.ValueQualityJson;

        Dictionary<string, AcademicWorkTopic> existing = target.Topics
            .ToDictionary(topic => topic.TopicId, StringComparer.Ordinal);
        HashSet<string> current = parsed.Topics.Select(topic => topic.TopicId)
            .ToHashSet(StringComparer.Ordinal);
        List<AcademicWorkTopic> removed = target.Topics.Where(topic =>
            !current.Contains(topic.TopicId)).ToList();
        database.AcademicWorkTopics.RemoveRange(removed);
        foreach (AcademicWorkTopic topic in removed)
            target.Topics.Remove(topic);
        foreach (AcademicWorkTopic source in parsed.Topics)
        {
            if (!existing.TryGetValue(source.TopicId, out AcademicWorkTopic? topic))
            {
                topic = new() { AcademicWorkId = work.Id, TopicId = source.TopicId };
                target.Topics.Add(topic);
            }
            topic.TopicName = source.TopicName;
            topic.SubfieldId = source.SubfieldId;
            topic.SubfieldName = source.SubfieldName;
            topic.FieldId = source.FieldId;
            topic.FieldName = source.FieldName;
            topic.DomainId = source.DomainId;
            topic.DomainName = source.DomainName;
            topic.OriginalRank = source.OriginalRank;
            topic.AssignmentScore = source.AssignmentScore;
            topic.ScoreQuality = source.ScoreQuality;
            topic.ScoreQualityReason = source.ScoreQualityReason;
            topic.IsPrimary = source.IsPrimary;
        }
    }
}
