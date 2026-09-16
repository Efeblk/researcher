using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Metrics;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/[action]")]
public sealed class AcademicPerformanceEndpoint : ServiceEndpoint
{
    [HttpPost]
    public async Task<AcademicDataResponse> Collect(
        AcademicDataCollectRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        try
        {
            return await applicationService.CollectAsync(request);
        }
        catch (ArgumentException exception)
        {
            throw new ValidationError("ValidationError", exception.Message);
        }
    }

    [HttpPost]
    public async Task<IActionResult> RecalculateMetrics(
        ResearcherMetricsRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService,
        CancellationToken cancellationToken)
    {
        if (HttpContext.Request.GetTypedHeaders().Accept?.Any(value =>
            string.Equals(value.MediaType.Value, "application/x-ndjson", StringComparison.OrdinalIgnoreCase) &&
            value.Quality.GetValueOrDefault(1) > 0) == true)
        {
            await StreamMetricsAsync(request, applicationService, cancellationToken);
            return new EmptyResult();
        }

        try
        {
            return new JsonResult(await applicationService.RecalculateMetricsAsync(
                request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            throw new ValidationError(exception.Message);
        }
    }

    private async Task StreamMetricsAsync(
        ResearcherMetricsRequest request,
        IAcademicPerformanceApplicationService applicationService,
        CancellationToken cancellationToken)
    {
        HttpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";
        HttpContext.Response.Headers.CacheControl = "no-cache, no-store";
        HttpContext.Response.Headers.Append("X-Accel-Buffering", "no");

        using CancellationTokenSource disconnect = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        CancellationToken streamToken = disconnect.Token;
        Channel<ResearcherMetricsProgress> channel = Channel.CreateUnbounded<ResearcherMetricsProgress>(
            new() { SingleReader = true, SingleWriter = true });
        Stopwatch elapsed = Stopwatch.StartNew();
        TimeSpan lastActualProgress = elapsed.Elapsed;
        string lastStage = "starting";
        InlineProgress progress = new(item =>
        {
            lastStage = item.Stage;
            channel.Writer.TryWrite(item);
        });
        Task<ResearcherMetricsResponse> calculation = applicationService.RecalculateMetricsAsync(
            request, progress, streamToken);

        using PeriodicTimer heartbeatTimer = new(TimeSpan.FromSeconds(5));
        Task<bool>? pendingRead = null;
        Task<bool>? pendingHeartbeat = null;
        try
        {
            while (!calculation.IsCompleted || channel.Reader.TryPeek(out _))
            {
                while (channel.Reader.TryRead(out ResearcherMetricsProgress? item))
                {
                    pendingRead = null;
                    lastActualProgress = elapsed.Elapsed;
                    lastStage = item.Stage;
                    await WriteMetricsEventAsync(item, elapsed, lastActualProgress, streamToken);
                }
                if (calculation.IsCompleted)
                    break;

                pendingRead ??= channel.Reader.WaitToReadAsync(streamToken).AsTask();
                pendingHeartbeat ??= heartbeatTimer.WaitForNextTickAsync(streamToken).AsTask();
                Task completed = await Task.WhenAny(calculation, pendingRead, pendingHeartbeat);
                if (completed == pendingHeartbeat)
                {
                    pendingHeartbeat = null;
                    await WriteMetricsEventAsync(new()
                    {
                        Type = "heartbeat",
                        Stage = lastStage,
                        Message = "Sunucu bağlantısı etkin; son işlem aşaması sürüyor."
                    }, elapsed, lastActualProgress, streamToken);
                }
            }

            ResearcherMetricsResponse result = await calculation;
            await WriteMetricsEventAsync(new()
            {
                Type = "result", Stage = "completed",
                Message = "Akademik metrikler güncellendi.", Result = result
            }, elapsed, lastActualProgress, streamToken);
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
        {
        }
        catch (ArgumentException exception)
        {
            await WriteMetricsEventAsync(new()
            {
                Type = "error", Stage = lastStage, Message = exception.Message
            }, elapsed, lastActualProgress, streamToken);
        }
        catch
        {
            if (!streamToken.IsCancellationRequested)
            {
                await WriteMetricsEventAsync(new()
                {
                    Type = "error", Stage = lastStage,
                    Message = "Metrik güncellemesi beklenmeyen bir hatayla sona erdi."
                }, elapsed, lastActualProgress, streamToken);
            }
        }
        finally
        {
            await disconnect.CancelAsync();
            try { await calculation; } catch (OperationCanceledException) { } catch { }
        }
    }

    private async Task WriteMetricsEventAsync(ResearcherMetricsProgress item, Stopwatch elapsed,
        TimeSpan lastActualProgress, CancellationToken cancellationToken)
    {
        item.ElapsedSeconds = Math.Round(elapsed.Elapsed.TotalSeconds, 1);
        item.LastProgressElapsedSeconds = Math.Round(
            (elapsed.Elapsed - lastActualProgress).TotalSeconds, 1);
        await HttpContext.Response.WriteAsync(JsonSerializer.Serialize(item) + "\n", cancellationToken);
        await HttpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private sealed class InlineProgress(Action<ResearcherMetricsProgress> report)
        : IProgress<ResearcherMetricsProgress>
    {
        public void Report(ResearcherMetricsProgress value) => report(value);
    }

    [HttpPost]
    public async Task<AcademicDataResponse> GetResearcher(
        AcademicResearcherRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        try
        {
            return await applicationService.GetResearcherAsync(request);
        }
        catch (ArgumentException exception)
        {
            throw new ValidationError(exception.Message);
        }
    }

    [HttpPost]
    public Task<AcademicPublicationListResponse> ListPublications(
        AcademicPublicationListRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        return applicationService.ListPublicationsAsync(request);
    }

    [HttpPost]
    public Task<AcademicPublicationSelectionResponse> SavePublicationSelections(
        AcademicPublicationSelectionRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        return applicationService.SavePublicationSelectionsAsync(request);
    }

    [HttpPost]
    public Task<CanonicalPublicationListResponse> ListCanonicalPublications(
        CanonicalPublicationListRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService,
        CancellationToken cancellationToken)
    {
        return applicationService.ListCanonicalPublicationsAsync(request, cancellationToken);
    }

}
