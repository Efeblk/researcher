using AcademicCollectorDemo.Tests.Infrastructure;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class AcademicPerformanceApplicationServiceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task GetResearcherEndpoint_InvalidOrUnknownSelector_ReturnsSafeValidationError()
    {
        using HostProcess host = new(fixture.ConnectionString);
        await host.WaitUntilReadyAsync();

        using HttpResponseMessage invalid = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/GetResearcher", new { ScopusID = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using HttpResponseMessage unknown = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/GetResearcher",
            new { PersonelID = "unknown-" + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        string responseBody = await unknown.Content.ReadAsStringAsync();
        Assert.Contains("Akademisyen kaydı bulunamadı.", responseBody);
    }

    [Fact]
    public async Task RecalculateMetricsEndpoint_InvalidOrUnknownPersonelId_ReturnsValidationError()
    {
        using HostProcess host = new(fixture.ConnectionString);
        await host.WaitUntilReadyAsync();

        using HttpResponseMessage missing = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/RecalculateMetrics", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        using HttpResponseMessage unknown = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/RecalculateMetrics",
            new { PersonelID = "unknown-" + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task GetResearcherAsync_PartialOpenAlexCollection_ReturnsStoredCount()
    {
        string personelId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"),
                OpenAlexProfile = new OpenAlexProfile
                {
                    OpenAlexAuthorId = "https://openalex.org/A" + Guid.NewGuid().ToString("N"),
                    WorksCount = 7, LastUpdatedAt = DateTime.UtcNow,
                    Works = [new() { OpenAlexWorkId = "https://openalex.org/W1" }]
                }
            };
            db.Researchers.Add(researcher);
            await db.SaveChangesAsync();
            personelId = researcher.PersonelId;
        }

        using var readScope = fixture.Services.CreateScope();
        var service = readScope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
        var response = await service.GetResearcherAsync(new() { PersonelId = personelId });
        Assert.Equal(7, response.Researcher!.OpenAlexProfile!.WorksCount);
        Assert.Equal(1, response.Researcher.OpenAlexProfile.CollectedWorksCount);
    }

    [Fact]
    public async Task GetResearcherAsync_EachIdentityWithoutPersonelId_ReturnsStoredDataWithoutWrites()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string suffix = Guid.NewGuid().ToString("N");
        string digits = string.Concat(suffix.Select(value => (char)('0' + (value % 10))));
        string personelId = "lookup-" + suffix;
        string orcid = $"{digits[..4]}-{digits[4..8]}-{digits[8..12]}-{digits[12..16]}";
        string scholarId = suffix[..12];
        string researcherId = $"A-{digits[16..20]}-{digits[20..24]}";
        string scopusId = "57" + digits[24..31];
        string tcKimlikNo = "1" + digits[..10];
        DateTime lastUpdatedAt = DateTime.UtcNow.AddDays(-2);
        var researcher = new Researcher
        {
            PersonelId = personelId,
            Orcid = orcid.ToUpperInvariant(),
            GoogleScholarId = scholarId,
            WebOfScienceResearcherId = researcherId.ToUpperInvariant(),
            ScopusId = scopusId,
            TcKimlikNo = tcKimlikNo,
            LastUpdatedAt = lastUpdatedAt,
            ScopusProfile = new ScopusProfile
            {
                ScopusAuthorId = scopusId,
                DisplayName = "Synthetic Researcher",
                DocumentsCount = 2,
                HIndex = 1,
                LastUpdatedAt = lastUpdatedAt
            }
        };
        db.Researchers.Add(researcher);
        db.AcademicWorks.Add(new AcademicWork
        {
            PersonelId = personelId,
            Provider = AcademicWorkProvider.Yoksis,
            ProviderWorkId = "synthetic-work",
            Title = "Synthetic publication",
            SyncedAt = lastUpdatedAt
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        DateTime? storedLastUpdatedAt = (await db.Researchers.AsNoTracking()
            .SingleAsync(value => value.PersonelId == personelId)).LastUpdatedAt;
        var service = scope.ServiceProvider
            .GetRequiredService<IAcademicPerformanceApplicationService>();
        AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts.AcademicResearcherRequest[] requests =
        [
            new() { PersonelId = "  " + personelId + "  " },
            new() { Orcid = "  " + orcid.ToLowerInvariant() + "  " },
            new() { GoogleScholarId = "  " + scholarId + "  " },
            new() { WebOfScienceResearcherId = "  " + researcherId.ToLowerInvariant() + "  " },
            new() { ScopusId = "  " + scopusId + "  " },
            new() { TcKimlikNo = "  " + tcKimlikNo + "  " }
        ];

        foreach (var request in requests)
        {
            var response = await service.GetResearcherAsync(request);
            Assert.Equal(personelId, response.Researcher!.PersonelId);
            Assert.Equal("Synthetic Researcher", response.Researcher.ScopusProfile!.DisplayName);
            Assert.Equal(1, response.YoksisPublicationCount);
        }

        db.ChangeTracker.Clear();
        Assert.Equal(storedLastUpdatedAt, (await db.Researchers.AsNoTracking()
            .SingleAsync(value => value.PersonelId == personelId)).LastUpdatedAt);
    }

    [Fact]
    public async Task GetResearcherAsync_ConflictingSelectors_FailsClosed()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string suffix = Guid.NewGuid().ToString("N");
        string digits = string.Concat(suffix.Select(value => (char)('0' + (value % 10))));
        string orcid = $"{digits[..4]}-{digits[4..8]}-{digits[8..12]}-{digits[12..16]}";
        string scopusId = "57" + digits[..9];
        db.Researchers.AddRange(
            new Researcher { PersonelId = "lookup-a-" + suffix, Orcid = orcid.ToUpperInvariant() },
            new Researcher { PersonelId = "lookup-b-" + suffix, ScopusId = scopusId });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider
            .GetRequiredService<IAcademicPerformanceApplicationService>();

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetResearcherAsync(new()
        {
            Orcid = orcid,
            ScopusId = scopusId
        }));
    }

    [Fact]
    public async Task GetResearcherAsync_EmptyOrInvalidSelector_RejectsSafely()
    {
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider
            .GetRequiredService<IAcademicPerformanceApplicationService>();

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetResearcherAsync(new()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetResearcherAsync(new()
        {
            TcKimlikNo = "synthetic-invalid"
        }));
    }

    [Fact]
    public async Task CollectAsync_ResearcherIdInOrcidField_RejectsBeforeCallingProviders()
    {
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
        await Assert.ThrowsAsync<ArgumentException>(() => service.CollectAsync(new()
        {
            PersonelId = "invalid-provider-person",
            Orcid = "A-1009-2008"
        }));
    }

    [Fact]
    public async Task CollectAsync_MissingPersonelId_RejectsBeforeCallingProviders()
    {
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
        await Assert.ThrowsAsync<ArgumentException>(() => service.CollectAsync(new()
        {
            Orcid = "0000-0002-1825-0097"
        }));
    }

    [Fact]
    public async Task CollectAsync_ValidMessyInputWithInvalidOptionalFields_PreservesRequestAndWarns()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        const string researcherId = "A-1234-2020";
        db.Researchers.Add(new Researcher
        {
            PersonelId = "messy-person",
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
            PersonelId = "messy-person", WebOfScienceResearcherId = wos,
            Orcid = orcid, ScopusId = "unsupported-scopus"
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
            PersonelId = "person-collect",
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
        Assert.Equal(stored.PersonelId, first.Researcher!.PersonelId);
        Assert.Equal(stored.PersonelId, repeated.Researcher!.PersonelId);
        Assert.Equal("person-collect", repeated.Researcher.PersonelId);
        Assert.Equal("person-collect", (await db.Researchers.FindAsync(stored.PersonelId))!.PersonelId);
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
        db.Researchers.Add(new Researcher { PersonelId = "unique-person" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.Researchers.Add(new Researcher { PersonelId = "unique-person" });

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CollectAsync_InvalidScopusOnly_RejectsWithSafeReason()
    {
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
        const string scopus = "private-scopus-value";
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.CollectAsync(new()
            {
                PersonelId = "scopus-only", ScopusId = scopus
            }));
        Assert.DoesNotContain(scopus, exception.Message);
        Assert.Contains("Scopus ID", exception.Message);
        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindByIdentifiersAsync_IdentifiersBelongToDifferentResearchers_RejectsCombination()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        db.Researchers.AddRange(new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"), Orcid = "0000-0001-8560-7482" },
            new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"), GoogleScholarId = "AbCdEfGhIjKl" });
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
            new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"), Orcid = "0000-0002-1825-009X" });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => new ResearcherRepository(db)
            .FindByIdentifiersAsync(new()
            {
                PersonelId = "person-a",
                Orcid = "0000-0002-1825-009X"
            }));
    }
}
