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
    public async Task AcquireWriteGateAsync_CommitAndRollback_ReleaseGateForNextResearcher()
    {
        await HoldAndReleaseGateAsync(commit: true);
        await SyncInScopeAsync(Id("after-commit"));

        await HoldAndReleaseGateAsync(commit: false);
        await SyncInScopeAsync(Id("after-rollback"));
    }

    [Fact]
    public async Task SyncAsync_GlobalGateTimeout_IsRetryableAndSucceedsAfterHolderRollsBack()
    {
        string personelId = Id("gate-timeout");
        await using AsyncServiceScope holderScope = fixture.Services.CreateAsyncScope();
        AcademicDbContext holderDb = holderScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await using IDbContextTransaction holderTransaction =
            await holderDb.Database.BeginTransactionAsync();
        await holderScope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>()
            .AcquireWriteGateAsync();

        CanonicalWorkLockException exception = await Assert.ThrowsAsync<CanonicalWorkLockException>(
            () => SyncInScopeAsync(personelId));

        Assert.Equal(-1, exception.SqlResult);
        Assert.Equal("ortak yayın kayıt kilidi", exception.LockScope);
        Assert.True(exception.IsRetryable);
        await holderTransaction.RollbackAsync();
        await SyncInScopeAsync(personelId);
    }

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
    public async Task SyncAsync_TitleYearFallback_NormalizesPresentationAndIgnoresIncompleteAuthors()
    {
        string personelId = Id("title-presentation");
        AcademicWork first = Work(personelId, AcademicWorkProvider.OpenAlex, null, "openalex",
            "İSTATİSTİK\u200B&amp; BİLİMİ: CO\u00ADOP\u200DERATION α");
        AcademicWork second = Work(personelId, AcademicWorkProvider.Scopus, null, "scopus",
            "istatistik & bilimi: cooperation &#945;");
        second.Authors = "et al.";
        AcademicWork third = Work(personelId, AcademicWorkProvider.Crossref, null, "crossref",
            "&lt;strong&gt;ISTATISTIK&lt;/strong&gt; &amp; BILIMI:&lt;br&gt;COOPERATION α");
        third.Authors = "Different Author";
        await SeedAsync(personelId, first, second, third);

        await SyncInScopeAsync(personelId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await new PublicationSummarySynchronizer(db).SyncAsync(personelId);
        Assert.Single(await db.CanonicalResearcherWorks.Where(value =>
            value.PersonelId == personelId).ToListAsync());
        Assert.Equal(3, await db.CanonicalWorkObservations.CountAsync(value =>
            value.PersonelId == personelId));
        Assert.Single(await db.PublicationSummaries.Where(value =>
            value.PersonelId == personelId).ToListAsync());
    }

    [Fact]
    public async Task SyncAsync_MeaningfulTitleTextAndMathSymbols_RemainSeparate()
    {
        string personelId = Id("title-meaning");
        await SeedAsync(personelId,
            Work(personelId, AcademicWorkProvider.OpenAlex, null, "math-x", "Response of <x> + y"),
            Work(personelId, AcademicWorkProvider.Scopus, null, "math-y", "Response of <y> + y"),
            Work(personelId, AcademicWorkProvider.Orcid, null, "plus", "A + B"),
            Work(personelId, AcademicWorkProvider.GoogleScholar, null, "minus", "A − B"));

        await SyncInScopeAsync(personelId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Equal(4, await db.CanonicalResearcherWorks.CountAsync(value =>
            value.PersonelId == personelId));
    }

    [Fact]
    public async Task SyncAsync_TitleYearFallback_RequiresExactTitleAndKnownEqualYear()
    {
        string personelId = Id("metadata-separate");
        AcademicWork baseline = Work(personelId, AcademicWorkProvider.Crossref, null, "baseline");
        AcademicWork missingYear = Work(personelId, AcademicWorkProvider.Orcid, null, "missing-year");
        missingYear.PublicationYear = null;
        AcademicWork missingAuthors = Work(personelId, AcademicWorkProvider.OpenAlex, null, "missing-authors");
        missingAuthors.Authors = "et al.";
        AcademicWork differentYear = Work(personelId, AcademicWorkProvider.Scopus, null, "different-year");
        differentYear.PublicationYear = 2024;
        AcademicWork differentAuthor = Work(personelId, AcademicWorkProvider.GoogleScholar, null, "different-author");
        differentAuthor.Authors = "Different Author";
        differentAuthor.Title = "Different title";
        await SeedAsync(personelId, baseline, missingYear, missingAuthors, differentYear, differentAuthor);

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
        noDoi.Authors = "Truncated";
        await SeedAsync(associatedId, doi, noDoi);

        string ambiguousId = Id("doi-ambiguous");
        AcademicWork doiA = Work(ambiguousId, AcademicWorkProvider.Crossref, "10.6000/a", "doi-a");
        doiA.Authors = "Ada Lovelace";
        AcademicWork doiB = Work(ambiguousId, AcademicWorkProvider.Orcid, "10.6000/b", "doi-b");
        doiB.Authors = "Grace Hopper";
        AcademicWork matched = Work(ambiguousId, AcademicWorkProvider.Scopus, null, "matched");
        matched.Authors = "Lovelace A";
        AcademicWork bridge = Work(ambiguousId, AcademicWorkProvider.OpenAlex, null, "bridge");
        bridge.Authors = null;
        await SeedAsync(ambiguousId, doiA, doiB, matched, bridge);

        await SyncInScopeAsync(associatedId);
        await SyncInScopeAsync(ambiguousId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Single(await db.CanonicalResearcherWorks.Where(value => value.PersonelId == associatedId).ToListAsync());
        Assert.Equal(2, await db.CanonicalWorkObservations.CountAsync(value => value.PersonelId == associatedId &&
            value.CanonicalWork!.NormalizedDoi == "10.6000/one"));
        Assert.Equal(3, await db.CanonicalResearcherWorks.CountAsync(value => value.PersonelId == ambiguousId));
        Assert.True(await db.CanonicalWorkObservations.AnyAsync(value => value.PersonelId == ambiguousId &&
            value.ProviderWorkId == "matched" && value.CanonicalWork!.NormalizedDoi == "10.6000/a"));
        Assert.True(await db.CanonicalWorkObservations.AnyAsync(value => value.PersonelId == ambiguousId &&
            value.ProviderWorkId == "bridge" && value.CanonicalWork!.SourceScopedKey != null));
    }

    [Fact]
    public async Task SyncAsync_TitleYearFallback_AuthorCorrectionPreservesCanonicalId()
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
            Assert.Equal(groupedId, await db.CanonicalResearcherWorks.Where(value =>
                value.PersonelId == personelId).Select(value => value.CanonicalWorkId).SingleAsync());
        }
    }

    [Fact]
    public async Task SyncAsync_EquivalentAuthorTokenAndListReordering_PreservesCanonicalId()
    {
        string personelId = Id("author-order");
        AcademicWork first = Work(personelId, AcademicWorkProvider.OpenAlex, null, "first");
        first.Authors = "Ada Lovelace, Alan Turing";
        AcademicWork second = Work(personelId, AcademicWorkProvider.Scopus, null, "second");
        second.Authors = "Turing Alan; Lovelace Ada";
        await SeedAsync(personelId, first, second);

        await SyncInScopeAsync(personelId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        int canonicalId = await db.CanonicalResearcherWorks.Where(value => value.PersonelId == personelId)
            .Select(value => value.CanonicalWorkId).SingleAsync();
        List<AcademicWork> works = await db.AcademicWorks.Where(value => value.PersonelId == personelId)
            .OrderBy(value => value.ProviderWorkId).ToListAsync();
        works[0].Authors = "Turing Alan; Lovelace Ada";
        works[1].Authors = "Ada Lovelace, Alan Turing";
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>().SyncAsync(personelId);

        Assert.Equal(canonicalId, await db.CanonicalResearcherWorks.Where(value => value.PersonelId == personelId)
            .Select(value => value.CanonicalWorkId).SingleAsync());
    }

    [Fact]
    public async Task SyncAsync_TitleYearFallback_MergesDifferentCompleteAuthors()
    {
        string personelId = Id("author-bridge");
        AcademicWork first = Work(personelId, AcademicWorkProvider.Crossref, null, "first");
        first.Authors = "Ada Lovelace";
        AcademicWork bridge = Work(personelId, AcademicWorkProvider.OpenAlex, null, "bridge");
        bridge.Authors = "A Lovelace";
        AcademicWork third = Work(personelId, AcademicWorkProvider.Scopus, null, "third");
        third.Authors = "Alice Lovelace";
        await SeedAsync(personelId, first, bridge, third);

        await SyncInScopeAsync(personelId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Single(await db.CanonicalResearcherWorks.Where(value => value.PersonelId == personelId)
            .ToListAsync());
        Assert.Single(await db.CanonicalWorkObservations.Where(value => value.PersonelId == personelId)
            .Select(value => value.CanonicalWorkId).Distinct().ToListAsync());
    }

    [Fact]
    public async Task SyncAsync_TitleYearFallback_DoesNotRequireSharedAuthorTokens()
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
        Assert.Single(await db.CanonicalResearcherWorks.Where(value => value.PersonelId == personelId)
            .ToListAsync());
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

    [Fact]
    public async Task SyncAsync_ExplicitCrossrefVersionParentAbsent_GroupsObservationsUnderParentDoi()
    {
        string personelId = Id("crossref-versions");
        AcademicWork first = Work(personelId, AcademicWorkProvider.Crossref,
            "10.7000/version-1", "version-1");
        first.ProviderPayload = CrossrefVersionPayload(first.Doi!, "10.7000/concept");
        AcademicWork second = Work(personelId, AcademicWorkProvider.Crossref,
            "10.7000/version-2", "version-2");
        second.ProviderPayload = CrossrefVersionPayload(second.Doi!, "10.7000/concept");
        await SeedAsync(personelId, first, second);

        await SyncInScopeAsync(personelId);
        await SyncInScopeAsync(personelId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        CanonicalWork canonical = await db.CanonicalWorks.Include(work => work.Observations)
            .SingleAsync(work => work.NormalizedDoi == "10.7000/concept");
        Assert.Equal(2, canonical.Observations.Count);
        Assert.Contains(canonical.Observations, value => value.DoiObserved == "10.7000/version-1");
        Assert.Contains(canonical.Observations, value => value.DoiObserved == "10.7000/version-2");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncAsync_CrossResearcherVersionEvidence_UsesOneStableCanonical(bool relationFirst)
    {
        string standaloneId = Id((relationFirst ? "true-" : "false-") + "global-version-standalone");
        string relationId = Id((relationFirst ? "true-" : "false-") + "global-version-relation");
        string versionDoi = relationFirst ? "10.7050/version-first" : "10.7050/version-last";
        string conceptDoi = relationFirst ? "10.7050/concept-first" : "10.7050/concept-last";
        AcademicWork standalone = Work(standaloneId, AcademicWorkProvider.OpenAlex,
            versionDoi, "standalone");
        AcademicWork related = Work(relationId, AcademicWorkProvider.Crossref,
            versionDoi, "related");
        related.ProviderPayload = CrossrefVersionPayload(related.Doi!, conceptDoi);
        await SeedAsync(standaloneId, standalone);
        await SeedAsync(relationId, related);

        if (relationFirst)
        {
            await SyncInScopeAsync(relationId);
            await SyncInScopeAsync(standaloneId);
        }
        else
        {
            await SyncInScopeAsync(standaloneId);
            await SyncInScopeAsync(relationId);
        }
        await SyncInScopeAsync(standaloneId);
        await SyncInScopeAsync(relationId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        int[] canonicalIds = await db.CanonicalResearcherWorks
            .Where(value => value.PersonelId == standaloneId || value.PersonelId == relationId)
            .Select(value => value.CanonicalWorkId).Distinct().ToArrayAsync();
        Assert.Single(canonicalIds);
        Assert.Equal(2, await db.CanonicalWorkObservations.CountAsync(value =>
            value.CanonicalWorkId == canonicalIds[0] && value.DoiObserved == versionDoi));
        Assert.Equal(canonicalIds[0], await db.CanonicalWorkDoiAliases
            .Where(value => value.NormalizedDoi == versionDoi)
            .Select(value => value.CanonicalWorkId).SingleAsync());
        Assert.Equal(canonicalIds[0], await db.CanonicalWorkDoiAliases
            .Where(value => value.NormalizedDoi == conceptDoi)
            .Select(value => value.CanonicalWorkId).SingleAsync());
    }

    [Fact]
    public async Task SyncAsync_CrossResearcherContradictionAndCycle_DoNotClaimNewAliases()
    {
        string firstId = Id("relation-history-first");
        string contradictionId = Id("relation-history-conflict");
        string cycleId = Id("relation-history-cycle");
        AcademicWork first = Work(firstId, AcademicWorkProvider.Crossref,
            "10.7060/version", "first");
        first.ProviderPayload = CrossrefVersionPayload(first.Doi!, "10.7060/concept-a");
        AcademicWork contradiction = Work(contradictionId, AcademicWorkProvider.Crossref,
            "10.7060/version", "conflict");
        contradiction.ProviderPayload = CrossrefVersionPayload(contradiction.Doi!, "10.7060/concept-b");
        AcademicWork withoutDoi = Work(contradictionId, AcademicWorkProvider.OpenAlex,
            null, "conflict-without-doi");
        withoutDoi.Authors = null;
        AcademicWork cycle = Work(cycleId, AcademicWorkProvider.Crossref,
            "10.7060/concept-a", "cycle");
        cycle.ProviderPayload = CrossrefVersionPayload(cycle.Doi!, "10.7060/version");
        await SeedAsync(firstId, first);
        await SeedAsync(contradictionId, contradiction, withoutDoi);
        await SeedAsync(cycleId, cycle);

        await SyncInScopeAsync(firstId);
        await SyncInScopeAsync(contradictionId);
        await SyncInScopeAsync(cycleId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.False(await db.CanonicalWorkDoiAliases.AnyAsync(value =>
            value.NormalizedDoi == "10.7060/concept-b"));
        Assert.False(await db.CanonicalWorkDoiRelations.AnyAsync(value =>
            value.SourceDoi == "10.7060/concept-a" && value.TargetDoi == "10.7060/version"));
        Assert.Single(await db.CanonicalWorkDoiRelations.Where(value =>
            value.SourceDoi == "10.7060/version").ToListAsync());
        int versionCanonicalId = await db.CanonicalWorkDoiAliases
            .Where(value => value.NormalizedDoi == "10.7060/version")
            .Select(value => value.CanonicalWorkId).SingleAsync();
        Assert.Equal(2, await db.CanonicalWorkObservations.CountAsync(value =>
            value.PersonelId == contradictionId && value.CanonicalWorkId == versionCanonicalId));
    }

    [Fact]
    public async Task SyncAsync_LongVersionDois_PersistAliasAndRelationKeys()
    {
        string personelId = Id("long-version-dois");
        string versionDoi = "10.7070/" + new string('v', 480);
        string conceptDoi = "10.7070/" + new string('c', 480);
        AcademicWork work = Work(personelId, AcademicWorkProvider.Crossref, versionDoi, "long-doi");
        work.ProviderPayload = CrossrefVersionPayload(versionDoi, conceptDoi);
        await SeedAsync(personelId, work);

        await SyncInScopeAsync(personelId);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Equal(2, await db.CanonicalWorkDoiAliases.CountAsync(value =>
            value.NormalizedDoi == versionDoi || value.NormalizedDoi == conceptDoi));
        Assert.Single(await db.CanonicalWorkDoiRelations.Where(value =>
            value.SourceDoi == versionDoi && value.TargetDoi == conceptDoi).ToListAsync());
    }

    [Fact]
    public async Task SyncAsync_OrcidVersionEquivalence_RetainsObservedDoisAndOneSummary()
    {
        string personelId = Id("orcid-versions");
        AcademicWork first = Work(personelId, AcademicWorkProvider.Orcid,
            "10.7100/version-1", "version-1");
        first.ProviderPayload = OrcidVersionPayload(first.Doi!, "10.7100/version-2");
        AcademicWork second = Work(personelId, AcademicWorkProvider.Orcid,
            "10.7100/version-2", "version-2");
        second.ProviderPayload = OrcidVersionPayload(second.Doi!, "10.7100/version-1");
        await SeedAsync(personelId, first, second);

        await SyncInScopeAsync(personelId);
        await SyncInScopeAsync(personelId);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await new PublicationSummarySynchronizer(db).SyncAsync(personelId);

        CanonicalResearcherWork association = await db.CanonicalResearcherWorks
            .SingleAsync(value => value.PersonelId == personelId);
        string?[] observedDois = await db.CanonicalWorkObservations
            .Where(value => value.CanonicalWorkId == association.CanonicalWorkId)
            .OrderBy(value => value.DoiObserved).Select(value => value.DoiObserved).ToArrayAsync();
        Assert.Equal(new string?[] { "10.7100/version-1", "10.7100/version-2" }, observedDois);
        Assert.Single(await db.PublicationSummaries.Where(value => value.PersonelId == personelId)
            .ToListAsync());
    }

    private async Task SyncInScopeAsync(string personelId)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>()
            .SyncAsync(personelId);
    }

    private async Task HoldAndReleaseGateAsync(bool commit)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>()
            .AcquireWriteGateAsync();
        if (commit)
            await transaction.CommitAsync();
        else
            await transaction.RollbackAsync();
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

    private static string CrossrefVersionPayload(string doi, string parentDoi) =>
        $"{{\"message\":{{\"DOI\":\"{doi}\",\"relation\":{{\"is-version-of\":[" +
        $"{{\"id-type\":\"doi\",\"id\":\"{parentDoi}\"}}]}}}}}}";

    private static string OrcidVersionPayload(string doi, string relatedDoi) =>
        $"{{\"external-ids\":{{\"external-id\":[" +
        $"{{\"external-id-type\":\"doi\",\"external-id-value\":\"{doi}\",\"external-id-relationship\":\"self\"}}," +
        $"{{\"external-id-type\":\"doi\",\"external-id-value\":\"{relatedDoi}\",\"external-id-relationship\":\"version-of\"}}]}}}}";
}
