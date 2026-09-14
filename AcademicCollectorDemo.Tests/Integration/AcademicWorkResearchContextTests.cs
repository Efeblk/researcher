using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class AcademicWorkResearchContextTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task SynchronizeAsync_ProviderPayloadChanges_ReplacesTopicsAndQueryProjection()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string personelId = "context-owner-" + suffix;
        string workId = "https://openalex.org/W" + suffix;
        string doi = "10.9911/" + suffix;
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        database.Researchers.Add(new Researcher { PersonelId = personelId });
        database.AcademicWorks.Add(Work(personelId, workId, doi, Payload(workId, "T1", "T2", 1m)));
        await database.SaveChangesAsync();

        CanonicalWorkSynchronizer canonical = new(database);
        await canonical.SyncAsync(personelId);
        AcademicWorkResearchContextSynchronizer synchronizer = scope.ServiceProvider
            .GetRequiredService<AcademicWorkResearchContextSynchronizer>();
        await synchronizer.SynchronizeAsync(personelId);

        AcademicWorkResearchContext initial = await database.AcademicWorkResearchContexts
            .AsNoTracking().Include(value => value.Topics)
            .SingleAsync(value => value.AcademicWorkId == database.AcademicWorks
                .Where(work => work.PersonelId == personelId && work.ProviderWorkId == workId)
                .Select(work => work.Id).Single());
        Assert.Equal(["T1", "T2"], initial.Topics.OrderBy(value => value.OriginalRank)
            .Select(value => value.TopicId));
        Assert.Equal(1m, initial.Fwci);

        AcademicWork tracked = await database.AcademicWorks.SingleAsync(value =>
            value.PersonelId == personelId && value.ProviderWorkId == workId);
        tracked.ProviderPayload = Payload(workId, "T3", "T1", 4m);
        tracked.SyncedAt = tracked.SyncedAt.AddMinutes(1);
        await database.SaveChangesAsync();
        await synchronizer.SynchronizeAsync(personelId);

        CanonicalPublicationListResponse publications = await scope.ServiceProvider
            .GetRequiredService<CanonicalWorkQueryService>()
            .ListAsync(personelId, new CanonicalPublicationListRequest { Take = 10 });
        AcademicWorkResearchContextDto projected = Assert.IsType<AcademicWorkResearchContextDto>(
            Assert.Single(Assert.Single(publications.Entities).Observations).ResearchContext);
        Assert.Equal(["T3", "T1"], projected.Topics.Select(value => value.TopicId));
        Assert.Equal(4m, projected.Fwci.Value);
    }

    private static AcademicWork Work(
        string personelId, string providerWorkId, string doi, string payload) => new()
    {
        PersonelId = personelId,
        Provider = AcademicWorkProvider.OpenAlex,
        ProviderWorkId = providerWorkId,
        Doi = doi,
        PublicationYear = 2024,
        RawType = "article",
        Category = AcademicWorkCategory.Article,
        ProviderPayload = payload,
        SyncedAt = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc)
    };

    private static string Payload(
        string workId, string primaryTopicId, string secondaryTopicId, decimal fwci) => $$$"""
        {
          "id":"{{{workId}}}","updated_date":"2026-04-03","publication_year":2024,
          "type":"article","primary_location":{"source":{"type":"journal"}},
          "fwci":{{{fwci.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},
          "citation_normalized_percentile":{"value":0,"is_in_top_1_percent":false,"is_in_top_10_percent":false},
          "primary_topic":{"id":"{{{primaryTopicId}}}","subfield":{"id":2740},"field":{"id":27},"domain":{"id":4}},
          "topics":[
            {"id":"{{{primaryTopicId}}}","display_name":"Primary {{{primaryTopicId}}}","score":0.9,
             "subfield":{"id":2740,"display_name":"AI"},"field":{"id":27,"display_name":"Computer Science"},
             "domain":{"id":4,"display_name":"Physical Sciences"}},
            {"id":"{{{secondaryTopicId}}}","display_name":"Secondary {{{secondaryTopicId}}}","score":0.4}
          ]
        }
        """;
}
