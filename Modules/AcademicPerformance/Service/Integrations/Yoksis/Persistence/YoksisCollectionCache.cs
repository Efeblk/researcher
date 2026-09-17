using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;

public sealed class YoksisCollectionCache
{
    private const int DefaultMaxAgeHours = 24;

    private readonly AcademicDbContext _dbContext;
    private readonly TimeSpan _maxAge;

    public YoksisCollectionCache(
        AcademicDbContext dbContext,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        int maxAgeHours = 0;

        if (!int.TryParse(
                configuration["ProviderCache:MaxAgeHours"],
                out maxAgeHours) ||
            maxAgeHours <= 0)
        {
            maxAgeHours = DefaultMaxAgeHours;
        }

        _maxAge = TimeSpan.FromHours(maxAgeHours);
    }

    public async Task<YoksisCollectResponse?> TryGetFreshAsync(
        string personelId,
        string tcKimlikNo,
        CancellationToken cancellationToken = default)
    {
        DateTime earliestCompletion = DateTime.UtcNow.Subtract(_maxAge);
        string tcKimlikNoHash = CreateTcKimlikNoHash(tcKimlikNo);
        YoksisCollectionSnapshot? snapshot = await _dbContext.YoksisCollectionSnapshots
            .AsNoTracking()
            .Where(item =>
                item.PersonelId == personelId &&
                item.TcKimlikNoHash == tcKimlikNoHash &&
                item.CompletedAtUtc >= earliestCompletion &&
                _dbContext.Researchers.Any(researcher =>
                    researcher.PersonelId == personelId &&
                    researcher.TcKimlikNo == tcKimlikNo))
            .SingleOrDefaultAsync(cancellationToken);

        if (snapshot is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<YoksisCollectResponse>(
                snapshot.ResponseJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task ReplaceAsync(
        string personelId,
        string tcKimlikNo,
        DateTime completedAtUtc,
        YoksisCollectResponse response,
        CancellationToken cancellationToken = default)
    {
        YoksisCollectionSnapshot? snapshot = await _dbContext.YoksisCollectionSnapshots
            .SingleOrDefaultAsync(item => item.PersonelId == personelId, cancellationToken);
        string responseJson = JsonSerializer.Serialize(response);

        if (snapshot is null)
        {
            snapshot = new()
            {
                PersonelId = personelId
            };
            _dbContext.YoksisCollectionSnapshots.Add(snapshot);
        }

        snapshot.TcKimlikNoHash = CreateTcKimlikNoHash(tcKimlikNo);
        snapshot.CompletedAtUtc = completedAtUtc;
        snapshot.ResponseJson = responseJson;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task InvalidateAsync(
        string personelId,
        CancellationToken cancellationToken = default)
    {
        YoksisCollectionSnapshot? snapshot = await _dbContext.YoksisCollectionSnapshots
            .SingleOrDefaultAsync(item => item.PersonelId == personelId, cancellationToken);

        if (snapshot is null)
            return;

        _dbContext.YoksisCollectionSnapshots.Remove(snapshot);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    internal static string CreateTcKimlikNoHash(string tcKimlikNo)
    {
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(tcKimlikNo))).ToLowerInvariant();
    }
}
