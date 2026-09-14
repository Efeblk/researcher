using System.Text.Json;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.HrDossiers;
using ResearcherAnalysisService.Products.ProductAccess;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Products.ArticleReviews;
using ResearcherAnalysisService.Products.FacultyAssistant;
using ResearcherAnalysisService.Products.Metrics;
using Microsoft.EntityFrameworkCore;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class HrEvidenceDossierTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task CreateAndActions_ContentChangeChangesFingerprintAndWhitespaceRetryIsIdempotent()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        string personelId = "hr-dossier-" + Guid.NewGuid().ToString("N");
        const string privateSentinel = "PRIVATE_FACULTY_CONTEXT_SENTINEL";
        const string providerSentinel = "RAW_PROVIDER_INPUT_SENTINEL";
        const string tcSentinel = "99999999999";
        Researcher researcher = new() { PersonelId = personelId, FirstName = "Synthetic",
            TcKimlikNo = tcSentinel };
        AcademicWork academic = new()
        {
            PersonelId = personelId, Provider = AcademicWorkProvider.Orcid, Title = "Synthetic work",
            Category = AcademicWorkCategory.Article, CategorySource = AcademicWorkCategorySource.Orcid,
            ProviderPayload = providerSentinel, SyncedAt = DateTime.UtcNow
        };
        CanonicalWork canonical = new() { NormalizedDoi = "10.8080/" + Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        database.AddRange(researcher, academic, canonical); await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        CanonicalWorkObservation observation = new()
        {
            CanonicalWorkId = canonical.Id, AcademicWorkId = academic.Id, PersonelId = personelId,
            Provider = AcademicWorkProvider.Orcid, PublicationYearObserved = 2025,
            CategoryObserved = AcademicWorkCategory.Article, ObservedAt = DateTime.UtcNow
        };
        database.AddRange(new CanonicalResearcherWork { CanonicalWorkId = canonical.Id,
            PersonelId = personelId, LastObservedAt = DateTime.UtcNow }, observation);
        database.FacultyAssistantContextVersions.Add(new FacultyAssistantContextVersion
        {
            PersonelId = personelId, Version = 1,
            ContextJson = JsonSerializer.Serialize(new { preferences = privateSentinel }),
            ContextFingerprint = new string('f', 64), CreatedByActorId = "faculty",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        PublicationMetricSnapshot metric = new()
        {
            PersonelId = personelId, CatalogVersion = "old-catalog", SourceRevision = 1,
            ComputationYear = DateTime.UtcNow.Year - 1, ComputedAt = DateTime.UtcNow,
            ResultJson = JsonSerializer.Serialize(new ResearcherPublicationMetricsResponse
            {
                PersonelId = personelId, CatalogVersion = "old-catalog",
                ValidYearUpperBound = DateTime.UtcNow.Year + 1
            })
        };
        database.PublicationMetricSnapshots.Add(metric);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        database.PublicationMetricsRefreshStates.Add(new PublicationMetricsRefreshState
        {
            PersonelId = personelId, RequestedRevision = 2, ComputedRevision = 1,
            RequestedCatalogVersion = PublicationMetricCatalog.Version,
            RequestedComputationYear = DateTime.UtcNow.Year,
            LastSuccessfulSnapshotId = metric.Id, UpdatedAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow
        });
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        HrEvidenceDossierService service = new(database, Options.Create(new ArticleReviewOptions()),
            Options.Create(new PublicationMetricsOptions()), TimeProvider.System);
        AcademicProductAccessGrant grant = new("grant", "reviewer", personelId,
            AcademicProductOperation.HrDossierCreate);
        CreateHrEvidenceDossierRequest request = new() { PersonelId = personelId,
            PublicationMetricSnapshotId = metric.Id, CanonicalWorkIds = [canonical.Id], Language = "en" };

        HrEvidenceDossierResponse first = (await service.CreateAsync(grant, request, default))!;
        HrEvidenceDossier persisted = await database.HrEvidenceDossiers.AsNoTracking()
            .SingleAsync(value => value.Id == first.DossierId);
        string exported = JsonSerializer.Serialize(first) + persisted.InputManifestJson + persisted.DossierJson;
        Assert.DoesNotContain(privateSentinel, exported);
        Assert.DoesNotContain(providerSentinel, exported);
        Assert.DoesNotContain(tcSentinel, exported);
        Assert.True(first.Dossier.PublicationMetrics!.IsStale);
        Assert.Contains(first.Dossier.PublicationMetrics.StaleReasons,
            value => value.Contains("waiting to be computed", StringComparison.Ordinal));
        Assert.Contains(first.Dossier.PublicationMetrics.StaleReasons,
            value => value.Contains("catalog version", StringComparison.Ordinal));
        Assert.Contains(first.Dossier.PublicationMetrics.StaleReasons,
            value => value.Contains("computation year", StringComparison.Ordinal));
        observation.PublicationYearObserved = 2024; await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        HrEvidenceDossierResponse second = (await service.CreateAsync(grant, request, default))!;
        Assert.NotEqual(first.InputFingerprint, second.InputFingerprint);

        Guid clientRequestId = Guid.NewGuid();
        AppendHrDossierReviewActionRequest action = new() { DossierId = first.DossierId,
            PersonelId = personelId, ClientRequestId = clientRequestId, ActionType = "NoteAdded",
            EvidenceReference = "  work:1  ", Note = "  Verify source  " };
        HrDossierReviewActionResponse created = (await service.AppendActionAsync(grant, action, default))!;
        action.EvidenceReference = "work:1"; action.Note = "Verify source";
        HrDossierReviewActionResponse retried = (await service.AppendActionAsync(grant, action, default))!;
        Assert.False(created.Reused);
        Assert.True(retried.Reused);
        Assert.Equal(created.Action.Id, retried.Action.Id);
    }
}
