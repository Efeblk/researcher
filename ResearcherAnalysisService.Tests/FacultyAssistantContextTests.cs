using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.FacultyAssistant;
using ResearcherAnalysisService.Products.ProductAccess;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class FacultyAssistantContextTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task SaveAndGet_VersionConflictAndOwnerBoundaryAreEnforced()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        string owner = "faculty-context-" + Guid.NewGuid().ToString("N");
        string other = "faculty-context-" + Guid.NewGuid().ToString("N");
        database.Researchers.AddRange(new Researcher { PersonelId = owner },
            new Researcher { PersonelId = other });
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        FacultyAssistantContextService service = new(database);
        AcademicProductAccessGrant ownerGrant = new("grant-owner", "actor-owner", owner,
            AcademicProductOperation.FacultyContextWrite);
        SaveFacultyAssistantContextRequest request = new()
        {
            PersonelId = owner, ExpectedVersion = 0,
            Context = new() { Language = "en", ResearchGoals = ["Synthetic goal"] }
        };

        FacultyAssistantContextResponse saved = (await service.SaveAsync(ownerGrant, request, default))!;
        Assert.Equal(1, saved.Version);
        await Assert.ThrowsAsync<FacultyAssistantConflictException>(() =>
            service.SaveAsync(ownerGrant, request, default));

        AcademicProductAccessGrant otherGrant = new("grant-other", "actor-other", other,
            AcademicProductOperation.FacultyContextRead);
        Assert.Null(await service.GetAsync(otherGrant, saved.Version, default));
        FacultyAssistantContextResponse own = (await service.GetAsync(
            ownerGrant with { Operation = AcademicProductOperation.FacultyContextRead }, saved.Version, default))!;
        Assert.Equal(owner, own.PersonelID);
    }

    [Fact]
    public async Task SaveAsync_ContextBeyondSharedAnalysisLimitIsRejectedWithoutPersistence()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        string owner = "faculty-context-limit-" + Guid.NewGuid().ToString("N");
        database.Researchers.Add(new Researcher { PersonelId = owner });
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        FacultyAssistantContextService service = new(database);
        AcademicProductAccessGrant grant = new("grant-owner", "actor-owner", owner,
            AcademicProductOperation.FacultyContextWrite);
        string entry = new('x', 300);
        SaveFacultyAssistantContextRequest request = new()
        {
            PersonelId = owner, ExpectedVersion = 0,
            Context = new()
            {
                Language = "en",
                ResearchGoals = Enumerable.Repeat(entry, 20).ToList(),
                Courses = Enumerable.Repeat(entry, 20).ToList(),
                Preferences = new string('y', 2000)
            }
        };

        Assert.True(System.Text.Json.JsonSerializer.Serialize(request.Context).Length >
            FacultyAssistantAnalysisLimits.MaximumPrivateContextCharacters);
        await Assert.ThrowsAsync<FacultyAssistantInputException>(() =>
            service.SaveAsync(grant, request, default));
        Assert.False(await database.FacultyAssistantContextVersions.AnyAsync(value =>
            value.PersonelId == owner));
    }
}
