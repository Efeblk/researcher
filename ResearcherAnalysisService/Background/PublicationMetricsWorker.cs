using ResearcherAnalysisService.Products.Metrics;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Background;

public sealed class PublicationMetricsWorker(
    IServiceScopeFactory scopes,
    IOptionsMonitor<PublicationMetricsOptions> options,
    ILogger<PublicationMetricsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.CurrentValue.WorkerEnabled)
                {
                    using IServiceScope scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<PublicationMetricsProcessor>()
                        .ProcessBatchAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Publication metrics worker failed ({ErrorType}); pending refreshes will retry.",
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
