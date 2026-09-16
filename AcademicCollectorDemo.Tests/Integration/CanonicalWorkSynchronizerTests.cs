using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class CanonicalWorkSynchronizerTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task SyncAsync_ConcurrentDoiVariants_CreatesOneCanonicalWithCompleteProvenance()
    {
        string firstId = Id("canonical-a");
        string secondId = Id("canonical-b");
        await SeedAsync(firstId, Work(firstId, AcademicWorkProvider.Orcid,
            "doi: https://DOI.org/10.1234/Shared", "orcid-work"));
        AcademicWork second = Work(secondId, AcademicWorkProvider.OpenAlex,
            "https://dx.doi.org/10.1234/shared", "openalex-work");
        second.Sources.Add(new()
        {
            Url = "https://provider.test/work", Kind = "Metadata",
            Origin = "Synthetic", IsOpenAccess = true
        });
        await SeedAsync(secondId, second);

        await Task.WhenAll(SyncInScopeAsync(firstId), SyncInScopeAsync(secondId));

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        CanonicalWork canonical = await db.CanonicalWorks
            .Include(work => work.Observations).ThenInclude(observation => observation.AcademicWork!)
                .ThenInclude(work => work.Sources)
            .Include(work => work.Researchers)
            .SingleAsync(work => work.NormalizedDoi == "10.1234/shared");
        Assert.Equal(2, canonical.Researchers.Count);
        Assert.Equal(2, canonical.Observations.Count);
        CanonicalWorkObservation observation = canonical.Observations.Single(value =>
            value.Provider == AcademicWorkProvider.OpenAlex);
        Assert.Equal("openalex-work", observation.ProviderWorkId);
        Assert.Contains(observation.AcademicWork!.Sources,
            source => source.Url == "https://provider.test/work" && source.IsOpenAccess == true);
    }

    [Fact]
    public async Task SyncAsync_ConflictingAndMissingDois_NeverTitleMerges()
    {
        string firstId = Id("identity-a");
        string secondId = Id("identity-b");
        await SeedAsync(firstId,
            Work(firstId, AcademicWorkProvider.Orcid, "10.2000/a", "doi-a", "Same title"),
            Work(firstId, AcademicWorkProvider.Orcid, "10.2000/b", "doi-b", "Same title"),
            Work(firstId, AcademicWorkProvider.GoogleScholar, null, "source-only", "Same title"));
        await SeedAsync(secondId,
            Work(secondId, AcademicWorkProvider.GoogleScholar, null, "source-only", "Same title"));

        await SyncInScopeAsync(firstId);
        await SyncInScopeAsync(secondId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Equal(2, await db.CanonicalWorks.CountAsync(work =>
            work.NormalizedDoi == "10.2000/a" || work.NormalizedDoi == "10.2000/b"));
        Assert.Equal(2, await db.CanonicalWorks.CountAsync(work => work.SourceScopedKey != null &&
            work.Observations.Any(observation => observation.ProviderWorkId == "source-only")));
    }

    [Fact]
    public async Task SyncAsync_NormalizedMetadataAndCompatibleAuthors_GroupsWithinResearcherAndProjectsSummary()
    {
        string personelId = Id("metadata");
        string otherPersonelId = Id("metadata-other");
        AcademicWork first = Work(personelId, AcademicWorkProvider.OpenAlex, null, "openalex",
            "A Study: of C++");
        first.Authors = "Ada Lovelace, Alan M Turing";
        AcademicWork second = Work(personelId, AcademicWorkProvider.Scopus, null, "scopus",
            "  A study--of C++  ");
        second.Authors = "Turing A. M.; Ada Lovelace";
        AcademicWork other = Work(otherPersonelId, AcademicWorkProvider.GoogleScholar, null, "scholar",
            "A study of C++");
        other.Authors = "Ada Lovelace, Alan M Turing";
        await SeedAsync(personelId, first, second);
        await SeedAsync(otherPersonelId, other);

        await SyncInScopeAsync(personelId);
        await SyncInScopeAsync(otherPersonelId);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await new PublicationSummarySynchronizer(db).SyncAsync(personelId);

        Assert.Single(await db.CanonicalResearcherWorks.Where(value => value.PersonelId == personelId).ToListAsync());
        Assert.Single(await db.PublicationSummaries.Where(value => value.PersonelId == personelId).ToListAsync());
        Assert.NotEqual(
            await db.CanonicalResearcherWorks.Where(value => value.PersonelId == personelId)
                .Select(value => value.CanonicalWorkId).SingleAsync(),
            await db.CanonicalResearcherWorks.Where(value => value.PersonelId == otherPersonelId)
                .Select(value => value.CanonicalWorkId).SingleAsync());
    }

    [Fact]
    public async Task SyncAsync_MissingOrDifferentMetadata_DoesNotGroup()
    {
        string personelId = Id("metadata-separate");
        AcademicWork missingYear = Work(personelId, AcademicWorkProvider.Orcid, null, "missing-year");
        missingYear.PublicationYear = null;
        AcademicWork missingAuthors = Work(personelId, AcademicWorkProvider.OpenAlex, null, "missing-authors");
        missingAuthors.Authors = "et al.";
        AcademicWork differentYear = Work(personelId, AcademicWorkProvider.Scopus, null, "different-year");
        differentYear.PublicationYear = 2024;
        AcademicWork differentAuthor = Work(personelId, AcademicWorkProvider.GoogleScholar, null, "different-author");
        differentAuthor.Authors = "Different Author";
        await SeedAsync(personelId, missingYear, missingAuthors, differentYear, differentAuthor);

        await SyncInScopeAsync(personelId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Equal(4, await db.CanonicalResearcherWorks.CountAsync(value => value.PersonelId == personelId));
    }

    [Fact]
    public async Task SyncAsync_DoiAssociationRequiresOneCompleteCompatibleComponent()
    {
        string associatedId = Id("doi-associate");
        AcademicWork doi = Work(associatedId, AcademicWorkProvider.Crossref, "10.6000/one", "doi");
        doi.Authors = "Ada Lovelace";
        AcademicWork noDoi = Work(associatedId, AcademicWorkProvider.OpenAlex, null, "no-doi");
        noDoi.Authors = "Lovelace A";
        await SeedAsync(associatedId, doi, noDoi);

        string ambiguousId = Id("doi-ambiguous");
        AcademicWork doiA = Work(ambiguousId, AcademicWorkProvider.Crossref, "10.6000/a", "doi-a");
        doiA.Authors = "Ada Lovelace";
        AcademicWork doiB = Work(ambiguousId, AcademicWorkProvider.Orcid, "10.6000/b", "doi-b");
        doiB.Authors = "A Lovelace";
        AcademicWork bridge = Work(ambiguousId, AcademicWorkProvider.OpenAlex, null, "bridge");
        bridge.Authors = "Lovelace A";
        await SeedAsync(ambiguousId, doiA, doiB, bridge);

        await SyncInScopeAsync(associatedId);
        await SyncInScopeAsync(ambiguousId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Single(await db.CanonicalResearcherWorks.Where(value => value.PersonelId == associatedId).ToListAsync());
        Assert.Equal(2, await db.CanonicalWorkObservations.CountAsync(value => value.PersonelId == associatedId &&
            value.CanonicalWork!.NormalizedDoi == "10.6000/one"));
        Assert.Equal(3, await db.CanonicalResearcherWorks.CountAsync(value => value.PersonelId == ambiguousId));
        Assert.True(await db.CanonicalWorkObservations.AnyAsync(value => value.PersonelId == ambiguousId &&
            value.ProviderWorkId == "bridge" && value.CanonicalWork!.SourceScopedKey != null));
    }

    [Fact]
    public async Task SyncAsync_MetadataCorrection_SplitsAndRepeatedSyncIsStable()
    {
        string personelId = Id("metadata-correction");
        await SeedAsync(personelId,
            Work(personelId, AcademicWorkProvider.OpenAlex, null, "first"),
            Work(personelId, AcademicWorkProvider.Scopus, null, "second"));
        await SyncInScopeAsync(personelId);

        int groupedId;
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            groupedId = await db.CanonicalResearcherWorks.Where(value => value.PersonelId == personelId)
                .Select(value => value.CanonicalWorkId).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>().SyncAsync(personelId);
            Assert.Equal(groupedId, await db.CanonicalResearcherWorks.Where(value => value.PersonelId == personelId)
                .Select(value => value.CanonicalWorkId).SingleAsync());
            AcademicWork corrected = await db.AcademicWorks.SingleAsync(value =>
                value.PersonelId == personelId && value.ProviderWorkId == "second");
            corrected.Authors = "Other Researcher";
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>().SyncAsync(personelId);
            Assert.Equal(2, await db.CanonicalResearcherWorks.CountAsync(value => value.PersonelId == personelId));
        }
    }

    [Fact]
    public async Task SyncAsync_AuthorTokenOrderWithInitialAmbiguity_IsDeterministic()
    {
        string personelId = Id("author-order");
        AcademicWork first = Work(personelId, AcademicWorkProvider.OpenAlex, null, "first");
        first.Authors = "A Alice";
        AcademicWork second = Work(personelId, AcademicWorkProvider.Scopus, null, "second");
        second.Authors = "Alice Adam";
        await SeedAsync(personelId, first, second);

        await SyncInScopeAsync(personelId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Single(await db.CanonicalResearcherWorks.Where(value => value.PersonelId == personelId).ToListAsync());
    }

    [Fact]
    public async Task SyncAsync_AuthorInitialsWithoutSharedFullToken_RemainSeparate()
    {
        string personelId = Id("author-anchor");
        AcademicWork first = Work(personelId, AcademicWorkProvider.OpenAlex, null, "first");
        first.Authors = "Alice S";
        AcademicWork second = Work(personelId, AcademicWorkProvider.Scopus, null, "second");
        second.Authors = "A Smith";
        await SeedAsync(personelId, first, second);

        await SyncInScopeAsync(personelId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Equal(2, await db.CanonicalResearcherWorks.CountAsync(value => value.PersonelId == personelId));
    }

    [Fact]
    public async Task SyncAsync_ReplacementAndDeletion_ConvergesWithoutChangingSharedCanonicalId()
    {
        string firstId = Id("replace-a");
        string secondId = Id("replace-b");
        AcademicWork first = Work(firstId, AcademicWorkProvider.Orcid, "10.3000/stable", "first");
        first.IsRetracted = true;
        await SeedAsync(firstId, first);
        await SeedAsync(secondId,
            Work(secondId, AcademicWorkProvider.OpenAlex, "10.3000/stable", "second"));
        await SyncInScopeAsync(firstId);
        await SyncInScopeAsync(secondId);
        int canonicalId;
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            CanonicalWork canonical = await db.CanonicalWorks.SingleAsync(work =>
                work.NormalizedDoi == "10.3000/stable");
            canonicalId = canonical.Id;
            Assert.True(canonical.HasRetractionObservation);
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            db.AcademicWorks.RemoveRange(db.AcademicWorks.Where(work => work.PersonelId == firstId));
            db.AcademicWorks.Add(Work(firstId, AcademicWorkProvider.Crossref,
                "HTTPS://DOI.ORG/10.3000/STABLE", "replacement"));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>().SyncAsync(firstId);
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            CanonicalWork canonical = await db.CanonicalWorks.Include(work => work.Researchers)
                .SingleAsync(work => work.Id == canonicalId);
            Assert.Equal(2, canonical.Researchers.Count);
            Assert.False(canonical.HasRetractionObservation);
            db.AcademicWorks.RemoveRange(db.AcademicWorks.Where(work => work.PersonelId == firstId));
            await db.SaveChangesAsync();
        }
        await SyncInScopeAsync(firstId);

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Assert.Equal(canonicalId, (await db.CanonicalWorks.SingleAsync(work =>
                work.NormalizedDoi == "10.3000/stable")).Id);
            Assert.Single(await db.CanonicalResearcherWorks.Where(association =>
                association.CanonicalWorkId == canonicalId).ToListAsync());
            Assert.False(await db.CanonicalWorkObservations.AnyAsync(observation =>
                observation.PersonelId == firstId));
        }
    }

    [Fact]
    public async Task SyncAsync_ConcurrentSameResearcherAndProviderDeletion_CompletesAndConverges()
    {
        string personelId = Id("same-researcher");
        await SeedAsync(personelId,
            Work(personelId, AcademicWorkProvider.Orcid, "10.4000/concurrent", "same"));
        await Task.WhenAll(SyncInScopeAsync(personelId), SyncInScopeAsync(personelId));

        TaskCompletionSource deletionSaved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task deleteTask = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
            AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            CanonicalWorkSynchronizer sync = scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>();
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
            await sync.AcquireWriteGateAsync();
            await sync.AcquireResearcherLockAsync(personelId);
            db.AcademicWorks.RemoveRange(db.AcademicWorks.Where(work => work.PersonelId == personelId));
            await db.SaveChangesAsync();
            deletionSaved.SetResult();
            await Task.Delay(100);
            await sync.SyncAsync(personelId);
            await transaction.CommitAsync();
        });
        await deletionSaved.Task;
        Task rebuildTask = SyncInScopeAsync(personelId);
        Task combined = Task.WhenAll(deleteTask, rebuildTask);
        Assert.Same(combined, await Task.WhenAny(combined, Task.Delay(TimeSpan.FromSeconds(10))));
        await combined;

        await using AsyncServiceScope assertScope = fixture.Services.CreateAsyncScope();
        AcademicDbContext assertDb = assertScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.False(await assertDb.CanonicalResearcherWorks.AnyAsync(association =>
            association.PersonelId == personelId));
        Assert.False(await assertDb.CanonicalWorkObservations.AnyAsync(observation =>
            observation.PersonelId == personelId));
    }

    [Fact]
    public async Task SyncAsync_CrossResearcherSharedDoiDeleteAndRebuild_DoesNotDeadlockOrLoseMembership()
    {
        string firstId = Id("gate-delete");
        string secondId = Id("gate-rebuild");
        const string doi = "10.4500/shared-gate";
        await SeedAsync(firstId, Work(firstId, AcademicWorkProvider.Orcid, doi, "delete-work"));
        await SeedAsync(secondId, Work(secondId, AcademicWorkProvider.OpenAlex, doi, "retain-work"));
        await SyncInScopeAsync(firstId);
        await SyncInScopeAsync(secondId);

        TaskCompletionSource deletionSaved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task deleteTask = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
            AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            CanonicalWorkSynchronizer sync = scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>();
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
            await sync.AcquireWriteGateAsync();
            await sync.AcquireResearcherLockAsync(firstId);
            db.AcademicWorks.RemoveRange(db.AcademicWorks.Where(work => work.PersonelId == firstId));
            await db.SaveChangesAsync();
            deletionSaved.SetResult();
            await Task.Delay(100);
            await sync.SyncAsync(firstId);
            await transaction.CommitAsync();
        });
        await deletionSaved.Task;
        Task rebuildTask = SyncInScopeAsync(secondId);
        Task combined = Task.WhenAll(deleteTask, rebuildTask);
        Assert.Same(combined, await Task.WhenAny(combined, Task.Delay(TimeSpan.FromSeconds(10))));
        await combined;

        await using AsyncServiceScope assertScope = fixture.Services.CreateAsyncScope();
        AcademicDbContext assertDb = assertScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        CanonicalWork canonical = await assertDb.CanonicalWorks.Include(work => work.Researchers)
            .Include(work => work.Observations)
            .SingleAsync(work => work.NormalizedDoi == doi);
        Assert.DoesNotContain(canonical.Researchers, association => association.PersonelId == firstId);
        Assert.Contains(canonical.Researchers, association => association.PersonelId == secondId);
        Assert.DoesNotContain(canonical.Observations, observation => observation.PersonelId == firstId);
        Assert.Contains(canonical.Observations, observation => observation.PersonelId == secondId);
    }

    [Fact]
    public async Task SyncAsync_Rerun_DoesNotChangePublicationSummaryApproval()
    {
        string personelId = Id("approval");
        await SeedAsync(personelId,
            Work(personelId, AcademicWorkProvider.Orcid, "10.5000/approval", "approval"));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await new CanonicalWorkSynchronizer(db).SyncAsync(personelId);
        await new PublicationSummarySynchronizer(db).SyncAsync(personelId);
        PublicationSummary summary = await db.PublicationSummaries.SingleAsync(work => work.PersonelId == personelId);
        db.PublicationDisplayApprovals.Add(new()
        {
            PersonelId = personelId,
            PublicationSummaryId = summary.Id,
            ApprovedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        CanonicalWorkSynchronizer synchronizer = scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>();
        await synchronizer.SyncAsync(personelId);
        await synchronizer.SyncAsync(personelId);

        Assert.Equal(summary.Id, (await db.PublicationSummaries.SingleAsync(work =>
            work.PersonelId == personelId)).Id);
        Assert.True(await db.PublicationDisplayApprovals.AnyAsync(approval =>
            approval.PublicationSummaryId == summary.Id));
        Assert.Single(await db.CanonicalResearcherWorks.Where(association =>
            association.PersonelId == personelId).ToListAsync());
    }

    private async Task SyncInScopeAsync(string personelId)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>()
            .SyncAsync(personelId);
    }

    private async Task SeedAsync(string personelId, params AcademicWork[] works)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        db.Researchers.Add(new Researcher { PersonelId = personelId });
        db.AcademicWorks.AddRange(works);
        await db.SaveChangesAsync();
    }

    private static AcademicWork Work(
        string personelId,
        AcademicWorkProvider provider,
        string? doi,
        string providerWorkId,
        string title = "Same title") => new()
        {
            PersonelId = personelId,
            Provider = provider,
            ProviderWorkId = providerWorkId,
            Title = title,
            Doi = doi,
            PublicationYear = 2025,
            PublicationDate = new DateTime(2025, 3, 4),
            Category = AcademicWorkCategory.Article,
            Authors = "Provider supplied author string",
            Publication = "Synthetic Journal",
            Link = "https://provider.test/landing",
            FullTextUrl = "https://provider.test/file.pdf",
            License = "cc-by",
            Version = "publishedVersion",
            SyncedAt = DateTime.UtcNow
        };

    private static string Id(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N");
}
