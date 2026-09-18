using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

public sealed class CanonicalWorkSynchronizer
{
    private const int ApplicationLockTimeoutMilliseconds = 15000;

    private readonly AcademicDbContext _dbContext;
    private readonly AcademicWorkResearchContextSynchronizer? _researchContextSynchronizer;

    public CanonicalWorkSynchronizer(
        AcademicDbContext dbContext, AcademicWorkResearchContextSynchronizer? researchContextSynchronizer = null)
    {
        _dbContext = dbContext;
        _researchContextSynchronizer = researchContextSynchronizer;
    }

    public async Task<CanonicalWorkSyncResult> SyncAsync(
        string personelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personelId))
            throw new ArgumentException("PersonelID is required.", nameof(personelId));

        IDbContextTransaction? ownedTransaction = null;
        if (_dbContext.Database.CurrentTransaction is null)
        {
            ownedTransaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
        }

        try
        {
            await AcquireWriteGateAsync(cancellationToken);
            await AcquireResearcherLockAsync(personelId, cancellationToken);
            if (_researchContextSynchronizer is not null)
                await _researchContextSynchronizer.SynchronizeAsync(personelId, cancellationToken);

            List<AcademicWork> works = await _dbContext.AcademicWorks
                .Include(work => work.Sources)
                .Where(work => work.PersonelId == personelId)
                .OrderBy(work => work.Id)
                .ToListAsync(cancellationToken);
            List<WorkIdentity> identities = CanonicalWorkIdentityResolver.Resolve(works);
            await RejectContradictoryPersistedRelationsAsync(works, identities, cancellationToken);
            Dictionary<string, CanonicalWork> canonicalByAlias = await LoadDoiAliasesAsync(
                identities.SelectMany(identity => identity.DoiAliases), cancellationToken);
            RejectConflictingRelationComponents(works, identities, canonicalByAlias);
            CanonicalWorkIdentityResolver.ApplyTitleYearFallback(works, identities);
            List<CanonicalWorkObservation> existingObservations =
                await _dbContext.CanonicalWorkObservations
                    .Include(observation => observation.CanonicalWork)
                    .Where(observation => observation.PersonelId == personelId)
                    .ToListAsync(cancellationToken);
            List<CanonicalResearcherWork> existingAssociations =
                await _dbContext.CanonicalResearcherWorks
                    .Include(association => association.CanonicalWork)
                    .Where(association => association.PersonelId == personelId)
                    .ToListAsync(cancellationToken);

            IEnumerable<string> oldLockKeys = existingAssociations.Select(association =>
                CreateLockKey(association.CanonicalWork!));
            foreach (string lockKey in identities.Select(identity => identity.LockKey).Concat(oldLockKeys)
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
            {
                await AcquireLockAsync("identity:" + lockKey, cancellationToken);
            }

            Dictionary<string, CanonicalWork> canonicalByIdentity =
                await LoadCanonicalWorksAsync(identities, canonicalByAlias, cancellationToken);
            DateTime synchronizedAt = DateTime.UtcNow;

            foreach (WorkIdentity identity in identities)
            {
                if (canonicalByIdentity.ContainsKey(identity.DictionaryKey))
                    continue;

                CanonicalWork canonical = new()
                {
                    NormalizedDoi = identity.NormalizedDoi,
                    SourceScopedKey = identity.SourceScopedKey,
                    CreatedAt = synchronizedAt,
                    UpdatedAt = synchronizedAt
                };
                canonicalByIdentity.Add(identity.DictionaryKey, canonical);
                _dbContext.CanonicalWorks.Add(canonical);
            }

            HashSet<string> persistedAliases = canonicalByAlias.Keys.ToHashSet(StringComparer.Ordinal);
            foreach (WorkIdentity identity in identities)
            {
                CanonicalWork canonical = canonicalByIdentity[identity.DictionaryKey];
                foreach (string alias in identity.DoiAliases.Where(persistedAliases.Add))
                {
                    _dbContext.CanonicalWorkDoiAliases.Add(new()
                    {
                        NormalizedDoi = alias,
                        CanonicalWork = canonical
                    });
                    canonicalByAlias[alias] = canonical;
                }
            }
            HashSet<ProviderWorkRelation> storedRelations = (await LoadRelationsAsync(
                identities.SelectMany(identity => identity.DoiAliases), cancellationToken)).ToHashSet();
            foreach (ProviderWorkRelation relation in identities.SelectMany(identity => identity.Relations)
                .Where(storedRelations.Add))
                _dbContext.CanonicalWorkDoiRelations.Add(new()
                {
                    RelationKey = Hash($"{relation.SourceDoi.Length}:{relation.SourceDoi}" +
                        $"{relation.TargetDoi.Length}:{relation.TargetDoi}{relation.Kind}"),
                    SourceDoi = relation.SourceDoi,
                    TargetDoi = relation.TargetDoi,
                    Kind = relation.Kind.ToString()
                });

            Dictionary<int, CanonicalWorkObservation> observationsByWorkId =
                existingObservations.ToDictionary(observation => observation.AcademicWorkId);
            HashSet<int> currentWorkIds = works.Select(work => work.Id).ToHashSet();
            List<CanonicalWorkObservation> currentObservations = [];

            for (int index = 0; index < works.Count; index++)
            {
                AcademicWork work = works[index];
                CanonicalWork canonical = canonicalByIdentity[identities[index].DictionaryKey];
                if (!observationsByWorkId.TryGetValue(work.Id, out CanonicalWorkObservation? observation))
                {
                    observation = new CanonicalWorkObservation { AcademicWorkId = work.Id };
                    _dbContext.CanonicalWorkObservations.Add(observation);
                }

                CopyObservation(work, canonical, observation);
                currentObservations.Add(observation);
                canonical.UpdatedAt = synchronizedAt;
            }

            _dbContext.CanonicalWorkObservations.RemoveRange(
                existingObservations.Where(observation => !currentWorkIds.Contains(observation.AcademicWorkId)));

            Dictionary<int, CanonicalResearcherWork> persistedAssociations = existingAssociations
                .Where(association => association.CanonicalWorkId != 0)
                .ToDictionary(association => association.CanonicalWorkId);
            HashSet<CanonicalWork> desiredCanonicalWorks = identities
                .Select(identity => canonicalByIdentity[identity.DictionaryKey])
                .ToHashSet();
            HashSet<int> desiredPersistedIds = desiredCanonicalWorks
                .Where(work => work.Id != 0).Select(work => work.Id).ToHashSet();
            Dictionary<CanonicalWork, DateTime> lastObservedByCanonical = works
                .Select((work, index) => new
                {
                    Work = work,
                    Canonical = canonicalByIdentity[identities[index].DictionaryKey]
                })
                .GroupBy(item => item.Canonical)
                .ToDictionary(group => group.Key, group => group.Max(item => item.Work.SyncedAt));

            _dbContext.CanonicalResearcherWorks.RemoveRange(existingAssociations.Where(
                association => !desiredPersistedIds.Contains(association.CanonicalWorkId)));
            foreach (CanonicalWork canonical in desiredCanonicalWorks)
            {
                if (canonical.Id != 0 && persistedAssociations.TryGetValue(
                    canonical.Id, out CanonicalResearcherWork? existingAssociation))
                {
                    existingAssociation.LastObservedAt = lastObservedByCanonical[canonical];
                    continue;
                }

                _dbContext.CanonicalResearcherWorks.Add(new()
                {
                    CanonicalWork = canonical,
                    PersonelId = personelId,
                    LastObservedAt = lastObservedByCanonical[canonical]
                });
            }

            HashSet<CanonicalWork> affectedCanonicalWorks = desiredCanonicalWorks
                .Concat(existingAssociations.Select(association => association.CanonicalWork!))
                .ToHashSet();
            int[] affectedIds = affectedCanonicalWorks.Where(work => work.Id != 0)
                .Select(work => work.Id).Distinct().ToArray();
            HashSet<int> otherRetractionIds = affectedIds.Length == 0
                ? []
                : (await _dbContext.CanonicalWorkObservations.AsNoTracking()
                    .Where(observation => affectedIds.Contains(observation.CanonicalWorkId) &&
                        observation.PersonelId != personelId && observation.IsRetracted == true)
                    .Select(observation => observation.CanonicalWorkId)
                    .Distinct().ToListAsync(cancellationToken)).ToHashSet();
            HashSet<CanonicalWork> requestedRetractionWorks = currentObservations
                .Where(observation => observation.IsRetracted == true)
                .Select(observation => observation.CanonicalWork!).ToHashSet();
            foreach (CanonicalWork canonical in affectedCanonicalWorks)
            {
                canonical.HasRetractionObservation = requestedRetractionWorks.Contains(canonical) ||
                    canonical.Id != 0 && otherRetractionIds.Contains(canonical.Id);
                canonical.UpdatedAt = synchronizedAt;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.CollectionChanges.Add(new()
            {
                EventId = Guid.NewGuid(),
                ChangeKind = "ResearcherCollected",
                PersonelId = personelId,
                OccurredAtUtc = synchronizedAt
            });
            foreach (CanonicalWork canonical in affectedCanonicalWorks.Where(work => work.Id != 0))
            {
                int? academicWorkId = currentObservations
                    .Where(observation => observation.CanonicalWorkId == canonical.Id ||
                        ReferenceEquals(observation.CanonicalWork, canonical))
                    .Select(observation => (int?)observation.AcademicWorkId)
                    .Concat(existingObservations.Where(observation => observation.CanonicalWorkId == canonical.Id)
                        .Select(observation => (int?)observation.AcademicWorkId))
                    .FirstOrDefault();
                _dbContext.CollectionChanges.Add(new()
                {
                    EventId = Guid.NewGuid(),
                    ChangeKind = "CanonicalWorkChanged",
                    PersonelId = personelId,
                    CanonicalWorkId = canonical.Id,
                    AcademicWorkId = academicWorkId,
                    OccurredAtUtc = synchronizedAt
                });
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);

            return new(
                desiredCanonicalWorks.Count,
                works.Count,
                desiredCanonicalWorks.Count);
        }
        catch
        {
            if (ownedTransaction is not null)
                await ownedTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (ownedTransaction is not null)
                await ownedTransaction.DisposeAsync();
        }
    }

    public Task AcquireResearcherLockAsync(
        string personelId,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required before acquiring a canonical researcher lock.");
        return AcquireLockAsync("researcher:" + Hash(personelId.Trim()), cancellationToken);
    }

    public Task AcquireWriteGateAsync(CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required before acquiring the canonical write gate.");
        return AcquireLockAsync("write-gate", cancellationToken);
    }

    private async Task<Dictionary<string, CanonicalWork>> LoadCanonicalWorksAsync(
        List<WorkIdentity> identities,
        Dictionary<string, CanonicalWork> canonicalByAlias,
        CancellationToken cancellationToken)
    {
        Dictionary<string, CanonicalWork> result = new(StringComparer.Ordinal);
        foreach (WorkIdentity identity in identities)
        {
            CanonicalWork? aliased = identity.DoiAliases.Select(alias =>
                canonicalByAlias.GetValueOrDefault(alias)).FirstOrDefault(value => value is not null);
            if (aliased is not null)
                result[identity.DictionaryKey] = aliased;
        }
        foreach (string[] dois in identities.Where(identity => identity.NormalizedDoi is not null)
            .Select(identity => identity.NormalizedDoi!).Distinct(StringComparer.Ordinal).Chunk(500))
        {
            foreach (CanonicalWork canonical in await _dbContext.CanonicalWorks
                .Where(work => work.NormalizedDoi != null && dois.Contains(work.NormalizedDoi))
                .ToListAsync(cancellationToken))
            {
                result.TryAdd("doi:" + canonical.NormalizedDoi, canonical);
            }
        }

        foreach (string[] keys in identities.Where(identity => identity.SourceScopedKey is not null)
            .Select(identity => identity.SourceScopedKey!).Distinct(StringComparer.Ordinal).Chunk(500))
        {
            foreach (CanonicalWork canonical in await _dbContext.CanonicalWorks
                .Where(work => work.SourceScopedKey != null && keys.Contains(work.SourceScopedKey))
                .ToListAsync(cancellationToken))
            {
                result.TryAdd("source:" + canonical.SourceScopedKey, canonical);
            }
        }

        return result;
    }

    private async Task<Dictionary<string, CanonicalWork>> LoadDoiAliasesAsync(
        IEnumerable<string> requestedAliases, CancellationToken cancellationToken)
    {
        Dictionary<string, CanonicalWork> result = new(StringComparer.Ordinal);
        foreach (string[] aliases in requestedAliases.Distinct(StringComparer.Ordinal).Chunk(500))
        {
            foreach (CanonicalWorkDoiAlias alias in await _dbContext.CanonicalWorkDoiAliases
                .Include(value => value.CanonicalWork)
                .Where(value => aliases.Contains(value.NormalizedDoi)).ToListAsync(cancellationToken))
                result.Add(alias.NormalizedDoi, alias.CanonicalWork!);
            foreach (CanonicalWork canonical in await _dbContext.CanonicalWorks
                .Where(value => value.NormalizedDoi != null && aliases.Contains(value.NormalizedDoi))
                .ToListAsync(cancellationToken))
                result.TryAdd(canonical.NormalizedDoi!, canonical);
        }
        return result;
    }

    private async Task RejectContradictoryPersistedRelationsAsync(List<AcademicWork> works,
        List<WorkIdentity> identities, CancellationToken cancellationToken)
    {
        foreach (IGrouping<string, int> group in identities.Select((identity, index) => (identity, index))
            .Where(value => value.identity.Relations.Count != 0)
            .GroupBy(value => value.identity.DictionaryKey, value => value.index))
        {
            List<ProviderWorkRelation> current = group.SelectMany(index => identities[index].Relations)
                .Distinct().ToList();
            List<ProviderWorkRelation> combined = (await LoadRelationsAsync(
                current.SelectMany(value => new[] { value.SourceDoi, value.TargetDoi }), cancellationToken))
                .Concat(current).Distinct().ToList();
            if (CanonicalWorkIdentityResolver.RelationsAreUnambiguous(combined))
                continue;
            foreach (int index in group)
            {
                string? doi = AcademicDoiNormalizer.NormalizeValid(works[index].Doi);
                identities[index] = doi is not null
                    ? new("doi:" + doi, doi, null, Hash("doi:" + doi), [doi], [])
                    : CanonicalWorkIdentityResolver.CreateSourceIdentity(works[index]);
            }
        }
    }

    private async Task<List<ProviderWorkRelation>> LoadRelationsAsync(IEnumerable<string> seedDois,
        CancellationToken cancellationToken)
    {
        HashSet<string> discovered = seedDois.ToHashSet(StringComparer.Ordinal);
        HashSet<string> pending = discovered.ToHashSet(StringComparer.Ordinal);
        List<CanonicalWorkDoiRelation> rows = [];
        while (pending.Count != 0)
        {
            string[] batch = pending.Take(500).ToArray();
            pending.ExceptWith(batch);
            List<CanonicalWorkDoiRelation> found = await _dbContext.CanonicalWorkDoiRelations.AsNoTracking()
                .Where(value => batch.Contains(value.SourceDoi) || batch.Contains(value.TargetDoi))
                .ToListAsync(cancellationToken);
            foreach (CanonicalWorkDoiRelation row in found)
            {
                if (!rows.Any(value => value.SourceDoi == row.SourceDoi && value.TargetDoi == row.TargetDoi &&
                    value.Kind == row.Kind)) rows.Add(row);
                if (discovered.Add(row.SourceDoi)) pending.Add(row.SourceDoi);
                if (discovered.Add(row.TargetDoi)) pending.Add(row.TargetDoi);
            }
        }
        return rows.Select(row => new ProviderWorkRelation(row.SourceDoi, row.TargetDoi,
            Enum.Parse<ProviderWorkRelationKind>(row.Kind))).ToList();
    }

    private static void RejectConflictingRelationComponents(List<AcademicWork> works,
        List<WorkIdentity> identities, Dictionary<string, CanonicalWork> canonicalByAlias)
    {
        foreach (IGrouping<string, int> group in identities.Select((identity, index) => (identity, index))
            .GroupBy(value => value.identity.DictionaryKey, value => value.index))
        {
            int[] owners = group.SelectMany(index => identities[index].DoiAliases)
                .Select(alias => canonicalByAlias.GetValueOrDefault(alias)?.Id ?? 0)
                .Where(id => id != 0).Distinct().ToArray();
            if (owners.Length <= 1)
                continue;
            foreach (int index in group)
            {
                string? doi = AcademicDoiNormalizer.NormalizeValid(works[index].Doi);
                identities[index] = doi is not null
                    ? new("doi:" + doi, doi, null, Hash("doi:" + doi), [doi], [])
                    : CanonicalWorkIdentityResolver.CreateSourceIdentity(works[index]);
            }
        }
    }

    private async Task AcquireLockAsync(string resource, CancellationToken cancellationToken)
    {
        DbConnection connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @timeout;
            SELECT @result;
            """;
        DbParameter resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.Value = "canonical-work:" + resource;
        command.Parameters.Add(resourceParameter);
        DbParameter timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "@timeout";
        timeoutParameter.Value = ApplicationLockTimeoutMilliseconds;
        command.Parameters.Add(timeoutParameter);
        object? scalar = await command.ExecuteScalarAsync(cancellationToken);
        int result = Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0)
            throw new CanonicalWorkLockException(
                resource, result, TimeSpan.FromMilliseconds(ApplicationLockTimeoutMilliseconds));
    }

    private static void CopyObservation(
        AcademicWork work,
        CanonicalWork canonical,
        CanonicalWorkObservation observation)
    {
        observation.CanonicalWork = canonical;
        observation.PersonelId = work.PersonelId;
        observation.Provider = work.Provider;
        observation.ProviderWorkId = work.ProviderWorkId;
        observation.TitleObserved = work.Title;
        observation.DoiObserved = string.IsNullOrWhiteSpace(work.Doi) ? null : work.Doi.Trim();
        observation.PublicationYearObserved = work.PublicationYear;
        observation.PublicationDateObserved = work.PublicationDate;
        observation.CategoryObserved = work.Category;
        observation.AuthorsObserved = work.Authors;
        observation.PublicationObserved = work.Publication;
        observation.SourceId = work.SourceId;
        observation.SourceName = work.SourceName;
        observation.SourceType = work.SourceType;
        observation.Link = work.Link;
        observation.FullTextUrl = work.FullTextUrl;
        observation.License = work.License;
        observation.Version = work.Version;
        observation.IsRetracted = work.IsRetracted;
        observation.ObservedAt = work.SyncedAt;
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string CreateLockKey(CanonicalWork work) => Hash(
        work.NormalizedDoi is null
            ? "source:" + work.SourceScopedKey
            : "doi:" + work.NormalizedDoi);

}
