using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.FacultyAssistant;

public sealed class FacultyAssistantContextService(AcademicDbContext database)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FacultyAssistantContextResponse?> SaveAsync(AcademicProductAccessGrant grant,
        SaveFacultyAssistantContextRequest request, CancellationToken cancellationToken)
    {
        if (!await database.Researchers.AsNoTracking().AnyAsync(value =>
            value.PersonelId == grant.SubjectPersonelId, cancellationToken)) return null;
        int current = await database.FacultyAssistantContextVersions.Where(value =>
            value.PersonelId == grant.SubjectPersonelId).MaxAsync(value => (int?)value.Version, cancellationToken) ?? 0;
        if (current != request.ExpectedVersion) throw new FacultyAssistantConflictException();
        string json = JsonSerializer.Serialize(request.Context, JsonOptions);
        if (json.Length > FacultyAssistantAnalysisLimits.MaximumPrivateContextCharacters)
            throw new FacultyAssistantInputException(
                "The private context exceeds the shared assistant input limit.");
        FacultyAssistantContextVersion value = new()
        {
            PersonelId = grant.SubjectPersonelId, Version = current + 1, ContextJson = json,
            ContextFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(),
            CreatedByActorId = grant.ActorAuditId, CreatedAt = DateTimeOffset.UtcNow
        };
        database.FacultyAssistantContextVersions.Add(value);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { throw new FacultyAssistantConflictException(); }
        return Map(value);
    }

    public async Task<FacultyAssistantContextResponse?> GetAsync(AcademicProductAccessGrant grant,
        int? version, CancellationToken cancellationToken)
    {
        IQueryable<FacultyAssistantContextVersion> query = database.FacultyAssistantContextVersions.AsNoTracking()
            .Where(value => value.PersonelId == grant.SubjectPersonelId);
        FacultyAssistantContextVersion? value = version.HasValue
            ? await query.SingleOrDefaultAsync(item => item.Version == version, cancellationToken)
            : await query.OrderByDescending(item => item.Version).FirstOrDefaultAsync(cancellationToken);
        return value is null ? null : Map(value);
    }

    private static FacultyAssistantContextResponse Map(FacultyAssistantContextVersion value) => new(
        value.PersonelId, value.Version, value.ContextFingerprint, value.CreatedAt,
        JsonSerializer.Deserialize<FacultyPrivateContext>(value.ContextJson, JsonOptions)!);
}

public sealed class FacultyAssistantConflictException : Exception;
