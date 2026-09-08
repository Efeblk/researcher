using AcademicCollectorDemo.Tests.Infrastructure;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class AcademicPerformanceApplicationServiceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task GetResearcherAsync_PartialOpenAlexCollection_ReturnsStoredCount()
    {
        int id;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            var researcher = new Researcher
            {
                OpenAlexProfile = new OpenAlexProfile
                {
                    OpenAlexAuthorId = "https://openalex.org/A" + Guid.NewGuid().ToString("N"),
                    WorksCount = 7, LastUpdatedAt = DateTime.UtcNow,
                    Works = [new() { OpenAlexWorkId = "https://openalex.org/W1" }]
                }
            };
            db.Researchers.Add(researcher);
            await db.SaveChangesAsync();
            id = researcher.Id;
        }

        using var readScope = fixture.Services.CreateScope();
        var service = readScope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
        var response = await service.GetResearcherAsync(new() { Id = id });
        Assert.Equal(7, response.Researcher!.OpenAlexProfile!.WorksCount);
        Assert.Equal(1, response.Researcher.OpenAlexProfile.CollectedWorksCount);
    }

    [Fact]
    public async Task CollectAsync_ResearcherIdInOrcidField_RejectsBeforeCallingProviders()
    {
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
        await Assert.ThrowsAsync<ArgumentException>(() => service.CollectAsync(new() { Orcid = "A-1009-2008" }));
    }

    [Fact]
    public async Task CollectAsync_ValidMessyInputWithInvalidOptionalFields_PreservesRequestAndWarns()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        const string researcherId = "A-1234-2020";
        db.Researchers.Add(new Researcher
        {
            WebOfScienceResearcherId = researcherId,
            WebOfScienceProfile = new WebOfScienceProfile
            {
                LastUpdatedAt = DateTime.UtcNow,
                DocumentPagesJson = "{\"WOS\":[{}]}",
                Works = []
            }
        });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
        const string wos = "https://www.webofscience.com/wos/author/record/A-1234-2020.";
        const string orcid = "A-1234-2020";
        var request = new AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts.AcademicDataCollectRequest
        {
            WebOfScienceResearcherId = wos, Orcid = orcid, ScopusId = "unsupported-scopus"
        };

        var response = await service.CollectAsync(request);

        Assert.Equal(orcid, request.Orcid);
        Assert.Equal(wos, request.WebOfScienceResearcherId);
        Assert.Equal("unsupported-scopus", request.ScopusId);
        Assert.True(response.IsSaved);
        Assert.Equal(researcherId, response.Researcher!.WebOfScienceResearcherId);
        Assert.Equal(2, response.Warnings.Count);
        Assert.Equal(2, response.Messages.Count(message => message.StartsWith("[UYARI] ")));
        Assert.DoesNotContain(orcid, string.Join(' ', response.Warnings));
    }

    [Fact]
    public async Task CollectAsync_PersonelId_PersistsAndCannotBeReassigned()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        const string researcherId = "C-1234-2020";
        var stored = new Researcher
        {
            WebOfScienceResearcherId = researcherId,
            WebOfScienceProfile = new WebOfScienceProfile
            {
                LastUpdatedAt = DateTime.UtcNow,
                DocumentPagesJson = "{\"WOS\":[{}]}",
                Works = []
            }
        };
        db.Researchers.Add(stored);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();

        var first = await service.CollectAsync(new()
        {
            PersonelId = "person-collect",
            WebOfScienceResearcherId = researcherId
        });
        var repeated = await service.CollectAsync(new()
        {
            PersonelId = "person-collect",
            WebOfScienceResearcherId = researcherId,
            ScopusId = "#NAME?"
        });

        Assert.True(first.IsSaved);
        Assert.Equal(stored.Id, first.Researcher!.Id);
        Assert.Equal(stored.Id, repeated.Researcher!.Id);
        Assert.Equal("person-collect", repeated.Researcher.PersonelId);
        Assert.Equal("person-collect", (await db.Researchers.FindAsync(stored.Id))!.PersonelId);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CollectAsync(new()
        {
            PersonelId = "different-person",
            WebOfScienceResearcherId = researcherId
        }));
    }

    [Fact]
    public async Task Researchers_DuplicatePersonelId_IsRejectedByDatabase()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        db.Researchers.AddRange(new Researcher { PersonelId = "unique-person" },
            new Researcher { PersonelId = "unique-person" });

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CollectAsync_ScopusOnly_RejectsWithSafeReason()
    {
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
        const string scopus = "private-scopus-value";
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.CollectAsync(new() { ScopusId = scopus }));
        Assert.DoesNotContain(scopus, exception.Message);
        Assert.Contains("Scopus ID", exception.Message);
        Assert.Contains("unsupported", exception.Message);
    }

    [Fact]
    public async Task FindByIdentifiersAsync_IdentifiersBelongToDifferentResearchers_RejectsCombination()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        db.Researchers.AddRange(new Researcher { Orcid = "0000-0001-8560-7482" },
            new Researcher { GoogleScholarId = "AbCdEfGhIjKl" });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => new ResearcherRepository(db).FindByIdentifiersAsync(
            new() { Orcid = "0000-0001-8560-7482", GoogleScholarId = "AbCdEfGhIjKl" }));
    }

    [Fact]
    public async Task FindByIdentifiersAsync_PersonelAndProviderBelongToDifferentResearchers_RejectsCombination()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        db.Researchers.AddRange(new Researcher { PersonelId = "person-a" },
            new Researcher { Orcid = "0000-0002-1825-009X" });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => new ResearcherRepository(db)
            .FindByIdentifiersAsync(new()
            {
                PersonelId = "person-a",
                Orcid = "0000-0002-1825-009X"
            }));
    }
}
