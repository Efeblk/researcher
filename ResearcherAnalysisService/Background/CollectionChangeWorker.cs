using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Products.Data;

namespace ResearcherAnalysisService.Background;

public sealed class CollectionChangeWorker(
    IServiceScopeFactory scopes,
    IOptionsMonitor<CollectionChangeOptions> options,
    ILogger<CollectionChangeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CollectionChangeProcessor>()
                    .ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Collection change processing failed ({ErrorType}); events remain pending.",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.CurrentValue.PollSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
