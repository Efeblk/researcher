using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/Yoksis/[action]")]
public sealed class YoksisEndpoint : ServiceEndpoint
{
    [HttpPost]
    public Task<YoksisCollectResponse> Collect(
        YoksisCollectRequest request,
        [FromServices] YoksisCollectionHandler collectionHandler)
    {
        return collectionHandler.CollectAsync(
            request, cancellationToken: HttpContext.RequestAborted);
    }

    [HttpPost]
    public async Task CollectStream(
        YoksisCollectRequest request,
        [FromServices] YoksisCollectionHandler collectionHandler)
    {
        HttpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";
        HttpContext.Response.Headers.CacheControl = "no-cache, no-store";
        HttpContext.Response.Headers.Append("X-Accel-Buffering", "no");

        using CancellationTokenSource disconnect = CancellationTokenSource.CreateLinkedTokenSource(
            HttpContext.RequestAborted);
        CancellationToken cancellationToken = disconnect.Token;
        Channel<YoksisCollectionProgress> channel = Channel.CreateUnbounded<YoksisCollectionProgress>(
            new() { SingleReader = true, SingleWriter = true });
        Stopwatch elapsed = Stopwatch.StartNew();
        TimeSpan lastActualProgress = elapsed.Elapsed;
        InlineProgress progress = new(item => channel.Writer.TryWrite(item));
        Task<YoksisCollectResponse> collection = collectionHandler.CollectAsync(
            request, progress, cancellationToken);
        Task<bool>? pendingRead = null;

        try
        {
            while (!collection.IsCompleted || channel.Reader.TryPeek(out _))
            {
                while (channel.Reader.TryRead(out YoksisCollectionProgress? item))
                {
                    pendingRead = null;
                    lastActualProgress = elapsed.Elapsed;
                    await WriteEventAsync(item, elapsed, lastActualProgress, cancellationToken);
                }

                if (collection.IsCompleted)
                    break;

                pendingRead ??= channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
                Task heartbeat = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                Task completed = await Task.WhenAny(pendingRead, heartbeat, collection);
                if (completed == heartbeat)
                {
                    await WriteEventAsync(new()
                    {
                        Type = "heartbeat",
                        Stage = "waiting",
                        Message = "Sunucuyla bağlantı sürüyor. Bu ileti bir YÖKSİS yanıtı değildir."
                    }, elapsed, lastActualProgress, cancellationToken);
                }
            }

            YoksisCollectResponse result = await collection;
            await WriteEventAsync(new()
            {
                Type = "result",
                Stage = "completed",
                Message = result.SuccessfulCategoryCount == 0 && result.StopReason is not null
                    ? result.StopReason
                    : result.IsSaved
                        ? "YÖKSİS toplaması ve kayıt işlemi tamamlandı."
                    : "YÖKSİS toplaması tamamlandı fakat kayıt işlemi başarısız oldu.",
                Result = result
            }, elapsed, lastActualProgress, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (YoksisProviderException exception)
        {
            await WriteEventAsync(new()
            {
                Type = "error",
                Stage = "configuration",
                Message = exception.Message
            }, elapsed, lastActualProgress, cancellationToken);
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                await WriteEventAsync(new()
                {
                    Type = "error",
                    Stage = "failed",
                    Message = "YÖKSİS toplaması beklenmeyen bir hatayla sona erdi."
                }, elapsed, lastActualProgress, cancellationToken);
            }
        }
        finally
        {
            await disconnect.CancelAsync();
            try { await collection; } catch (OperationCanceledException) { } catch { }
        }
    }

    private async Task WriteEventAsync(YoksisCollectionProgress item, Stopwatch elapsed,
        TimeSpan lastActualProgress, CancellationToken cancellationToken)
    {
        item.ElapsedSeconds = Math.Round(elapsed.Elapsed.TotalSeconds, 1);
        item.LastProgressElapsedSeconds = Math.Round(
            (elapsed.Elapsed - lastActualProgress).TotalSeconds, 1);
        await HttpContext.Response.WriteAsync(JsonSerializer.Serialize(item) + "\n", cancellationToken);
        await HttpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private sealed class InlineProgress(Action<YoksisCollectionProgress> report)
        : IProgress<YoksisCollectionProgress>
    {
        public void Report(YoksisCollectionProgress value) => report(value);
    }
}
