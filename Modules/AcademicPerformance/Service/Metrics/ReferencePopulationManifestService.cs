using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;

public sealed class ReferencePopulationManifestService(AcademicDbContext database)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReferencePopulationManifestResponse> ImportAsync(
        string actorAuditId,
        ReferencePopulationImportRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        string fingerprint = Fingerprint(request);
        ReferencePopulationManifest? existing = await database.ReferencePopulationManifests
            .AsNoTracking().SingleOrDefaultAsync(value =>
                value.ManifestVersion == request.ManifestVersion.Trim(), cancellationToken);
        if (existing is not null)
        {
            if (!existing.Fingerprint.Equals(fingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("The manifest version already exists with different content.");
            return Map(existing);
        }

        ReferencePopulationReviewDto? review = request.Review;
        ReferencePopulationManifest manifest = new()
        {
            ManifestVersion = request.ManifestVersion.Trim(),
            Fingerprint = fingerprint,
            CohortDefinition = request.CohortDefinition.Trim(),
            EligibilityPolicyVersion = request.EligibilityPolicyVersion.Trim(),
            Provenance = request.Provenance.Trim(),
            SamplingAndCoverage = request.SamplingAndCoverage.Trim(),
            MemberCount = request.Members.Count,
            ImportedAt = DateTimeOffset.UtcNow,
            ImportedByActorAuditId = actorAuditId,
            Reviewer = NullIfBlank(review?.Reviewer),
            ReviewedAt = review?.ReviewedAt == default ? null : review?.ReviewedAt,
            ReviewMethod = NullIfBlank(review?.Method),
            ApprovedForInternalNormalization = review?.ApprovedForInternalNormalization == true,
            Members = request.Members.OrderBy(value => value.StableMemberId.Trim(), StringComparer.Ordinal)
                .Select(value => new ReferencePopulationMember
                {
                    StableMemberId = value.StableMemberId.Trim(),
                    ClassificationId = value.ClassificationId.Trim(),
                    PublicationYear = value.PublicationYear,
                    WorkType = value.WorkType.Trim().ToLowerInvariant(),
                    Category = value.Category.Trim(),
                    CitationCount = value.CitationCount
                }).ToList()
        };
        database.ReferencePopulationManifests.Add(manifest);
        await database.SaveChangesAsync(cancellationToken);
        return Map(manifest);
    }

    public async Task<ReferencePopulationManifestResponse?> GetAsync(
        string manifestVersion, CancellationToken cancellationToken)
    {
        ReferencePopulationManifest? manifest = await database.ReferencePopulationManifests
            .AsNoTracking().SingleOrDefaultAsync(value =>
                value.ManifestVersion == manifestVersion.Trim(), cancellationToken);
        return manifest is null ? null : Map(manifest);
    }

    internal static void Validate(ReferencePopulationImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ManifestVersion) ||
            string.IsNullOrWhiteSpace(request.CohortDefinition) ||
            string.IsNullOrWhiteSpace(request.EligibilityPolicyVersion) ||
            string.IsNullOrWhiteSpace(request.Provenance) ||
            string.IsNullOrWhiteSpace(request.SamplingAndCoverage) || request.Members is null ||
            request.Members.Count == 0)
            throw new ArgumentException("The reference-population manifest is incomplete.", nameof(request));
        if (request.Members.Count > 100000)
            throw new ArgumentException("The reference population exceeds 100000 members.", nameof(request));
        if (request.ManifestVersion.Trim().Length > 100 ||
            request.CohortDefinition.Trim().Length > 4000 ||
            request.EligibilityPolicyVersion.Trim().Length > 100 ||
            request.Provenance.Trim().Length > 4000 ||
            request.SamplingAndCoverage.Trim().Length > 4000)
            throw new ArgumentException("A reference-population manifest field exceeds its bound.", nameof(request));
        if (request.Members.Any(value => value is null || string.IsNullOrWhiteSpace(value.StableMemberId) ||
            string.IsNullOrWhiteSpace(value.ClassificationId) ||
            string.IsNullOrWhiteSpace(value.WorkType) ||
            value.StableMemberId.Trim().Length > 200 || value.ClassificationId.Trim().Length > 200 ||
            value.WorkType.Trim().Length > 100 || (value.Category?.Trim().Length ?? 0) > 100 ||
            !Enum.GetNames<AcademicWorkCategory>().Contains(value.Category, StringComparer.Ordinal) ||
            value.PublicationYear is < 1 or > 9999 || value.CitationCount < 0))
            throw new ArgumentException("A reference-population member is invalid.", nameof(request));
        if (request.Members.Select(value => value!.StableMemberId.Trim())
            .Distinct(StringComparer.Ordinal).Count() != request.Members.Count)
            throw new ArgumentException("Reference-population member IDs must be unique.", nameof(request));
        if (request.Review is { } review &&
            (string.IsNullOrWhiteSpace(review.Reviewer) || review.ReviewedAt == default ||
             string.IsNullOrWhiteSpace(review.Method) || review.Reviewer.Trim().Length > 200 ||
             review.Method.Trim().Length > 4000))
            throw new ArgumentException("A review attestation requires bounded reviewer, review time, and method fields.", nameof(request));
    }

    internal static string Fingerprint(ReferencePopulationImportRequest request)
    {
        var canonical = new
        {
            manifestVersion = request.ManifestVersion.Trim(),
            cohortDefinition = request.CohortDefinition.Trim(),
            eligibilityPolicyVersion = request.EligibilityPolicyVersion.Trim(),
            provenance = request.Provenance.Trim(),
            samplingAndCoverage = request.SamplingAndCoverage.Trim(),
            review = request.Review is null ? null : new
            {
                reviewer = request.Review.Reviewer.Trim(),
                request.Review.ReviewedAt,
                method = request.Review.Method.Trim(),
                request.Review.ApprovedForInternalNormalization
            },
            members = request.Members.OrderBy(value => value.StableMemberId.Trim(), StringComparer.Ordinal)
                .Select(value => new
                {
                    stableMemberId = value.StableMemberId.Trim(),
                    classificationId = value.ClassificationId.Trim(),
                    value.PublicationYear,
                    workType = value.WorkType.Trim().ToLowerInvariant(),
                    category = value.Category.Trim(),
                    value.CitationCount
                })
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(canonical, JsonOptions)))).ToLowerInvariant();
    }

    private static ReferencePopulationManifestResponse Map(ReferencePopulationManifest value)
    {
        bool reviewed = value.ApprovedForInternalNormalization && value.Reviewer is not null &&
            value.ReviewedAt.HasValue && value.ReviewMethod is not null;
        return new()
        {
            Id = value.Id,
            ManifestVersion = value.ManifestVersion,
            Fingerprint = value.Fingerprint,
            MemberCount = value.MemberCount,
            MechanicalValidationStatus = "Passed",
            ReviewStatus = reviewed ? "ReviewedApproved" : "UnreviewedOrNotApproved",
            ReadyForInternalNormalization = false,
            Reason = reviewed
                ? "The manifest passed software checks and contains an approval attestation, but no compatible normalization execution policy and formula is registered. Software does not establish scientific validity."
                : "Internal normalization remains unavailable until the population has an explicit review approval."
        };
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
