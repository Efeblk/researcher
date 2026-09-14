using System.Data;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.ProductAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Products.FacultyAssistant;

public sealed class FacultyAssistantProcessor(AnalysisDbContext database,
    FacultyAssistantServiceClient client, IAcademicProductAccessService access,
    ILogger<FacultyAssistantProcessor> logger,
    IOptions<ArticleSummaryAutomationOptions>? analysisOptions = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using SqlApplicationLock? workerLock = await SqlApplicationLock.TryAcquireAsync(
            database.Database.GetConnectionString()!, "AcademicCollector.FacultyAssistantWorker", 0, cancellationToken);
        if (workerLock is null) return false;
        await MarkInterruptedAsync(cancellationToken);
        FacultyAssistantRun? run = await ClaimAsync(cancellationToken);
        if (run is null) return false;
        Guid token = run.AttemptToken!.Value;
        try
        {
            AcademicProductAccessGrant persisted = new(run.AuthorizationGrantId, run.ActorAuditId,
                run.PersonelId, AcademicProductOperation.FacultyAssistantStart);
            AcademicProductAccessGrant current = await access.ReauthorizeAsync(persisted, cancellationToken);
            if (current.SubjectPersonelId != run.PersonelId || current.ActorAuditId != run.ActorAuditId ||
                !await FacultyAssistantReadService.HasAssociationsAsync(database, run, cancellationToken))
            {
                await FailAsync(run.Id, token, "AccessRevoked",
                    "Authorization or current publication association is no longer available.", cancellationToken);
                return true;
            }
            AcademicEvidenceSearchResponse pinned = JsonSerializer.Deserialize<AcademicEvidenceSearchResponse>(
                run.RetrievalManifestJson, JsonOptions) ?? throw new JsonException();
            long[] pinnedRunIds = pinned.Hits.Select(value => value.AnalysisRunId)
                .Where(value => value > 0).Distinct().ToArray();
            IReadOnlyDictionary<long, AnalysisFreshnessResult> freshness =
                await AnalysisFreshnessEvaluator.EvaluateAsync(database, pinnedRunIds,
                    analysisOptions?.Value.PolicyVersion?.Trim() ?? new ArticleSummaryAutomationOptions().PolicyVersion,
                    cancellationToken);
            if (freshness.Values.Any(value => value.Status == AnalysisFreshnessStatus.Stale))
            {
                await FailAsync(run.Id, token, "EvidenceChanged",
                    "The pinned article evidence became stale before provider dispatch.", cancellationToken);
                return true;
            }
            FacultyAssistantAnalysisRequest input = JsonSerializer.Deserialize<FacultyAssistantAnalysisRequest>(
                run.AuthorizedInputJson!, JsonOptions) ?? throw new JsonException();
            FacultyAssistantAnalysisReport report = await client.AnswerAsync(input, cancellationToken);
            await CompleteAsync(run.Id, token, report, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await InterruptAsync(run.Id, token);
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Faculty assistant run was interrupted by an upstream timeout.");
            await InterruptAsync(run.Id, token);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Faculty assistant run failed ({ErrorType}).", exception.GetType().Name);
            string code = exception switch
            {
                AcademicProductAccessUnavailableException => "AuthorizationUnavailable",
                AcademicProductUnauthenticatedException or AcademicProductAccessDeniedException => "AccessRevoked",
                HttpRequestException => "ProviderFailure",
                JsonException => "InvalidResponse",
                _ => "ProcessingFailure"
            };
            await FailAsync(run.Id, token, code, "The faculty assistant run failed safely; it was not retried.", CancellationToken.None);
        }
        return true;
    }

    private async Task<FacultyAssistantRun?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        FacultyAssistantRun? run = await database.FacultyAssistantRuns.OrderBy(value => value.Id)
            .FirstOrDefaultAsync(value => value.Status == "Pending", cancellationToken);
        if (run is null) return null;
        run.Status = "Running"; run.AttemptCount++; run.AttemptToken = Guid.NewGuid();
        run.AttemptStartedAt = DateTimeOffset.UtcNow; run.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return run;
    }

    private async Task MarkInterruptedAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await database.FacultyAssistantRuns.Where(value => value.Status == "Running")
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, "Interrupted")
                .SetProperty(value => value.ErrorCode, "Interrupted")
                .SetProperty(value => value.ErrorMessage, "An earlier execution ended while running; remote cost may be unknown and no automatic retry was made.")
                .SetProperty(value => value.UpdatedAt, now), cancellationToken);
    }

    private async Task CompleteAsync(long id, Guid token, FacultyAssistantAnalysisReport report, CancellationToken ct)
    {
        int changed = await database.FacultyAssistantRuns.Where(value => value.Id == id && value.Status == "Running" && value.AttemptToken == token)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, "Completed")
                .SetProperty(value => value.ReportJson, JsonSerializer.Serialize(report, JsonOptions))
                .SetProperty(value => value.UpdatedAt, DateTimeOffset.UtcNow), ct);
        if (changed != 1) throw new InvalidOperationException("The faculty assistant execution fence was lost.");
    }

    private Task FailAsync(long id, Guid token, string code, string message, CancellationToken ct) =>
        database.FacultyAssistantRuns.Where(value => value.Id == id && value.Status == "Running" && value.AttemptToken == token)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, "Failed")
                .SetProperty(value => value.ErrorCode, code).SetProperty(value => value.ErrorMessage, message)
                .SetProperty(value => value.UpdatedAt, DateTimeOffset.UtcNow), ct);

    private Task InterruptAsync(long id, Guid token) =>
        database.FacultyAssistantRuns.Where(value => value.Id == id && value.Status == "Running" && value.AttemptToken == token)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, "Interrupted")
                .SetProperty(value => value.ErrorCode, "Interrupted")
                .SetProperty(value => value.ErrorMessage,
                    "Execution was interrupted; remote cost may be unknown and no automatic retry was made.")
                .SetProperty(value => value.UpdatedAt, DateTimeOffset.UtcNow), CancellationToken.None);
}
