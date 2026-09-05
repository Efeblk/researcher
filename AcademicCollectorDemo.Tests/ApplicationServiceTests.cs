using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests;

[Collection("SQL Server")]
public sealed class ApplicationServiceTests(SqlServerFixture fixture)
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
        var response = await service.GetResearcherAsync(new() { ResearcherId = id });
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
}
