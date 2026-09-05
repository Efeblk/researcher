using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Background;

public sealed class BulkCollectionWorker(
    IServiceScopeFactory scopes, IOptions<BulkCollectionOptions> options,
    ILogger<BulkCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                bool processed = await scope.ServiceProvider.GetRequiredService<BulkJobProcessor>()
                    .ProcessNextAsync(stoppingToken);
                if (processed)
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Do not log provider URLs, query text, credentials, or researcher input.
                logger.LogError("Bulk worker failed ({ErrorType}); queue processing will retry.", exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken);
        }
    }
}
