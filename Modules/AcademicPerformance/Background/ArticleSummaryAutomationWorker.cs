using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Background;

public sealed class ArticleSummaryAutomationWorker(
    IServiceScopeFactory scopes,
    IOptionsMonitor<ArticleSummaryAutomationOptions> options,
    ILogger<ArticleSummaryAutomationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ArticleSummaryAutomationOptions settings = options.CurrentValue;
                if (settings.Enabled && settings.WorkerEnabled)
                {
                    using IServiceScope scope = scopes.CreateScope();
                    bool processed = await scope.ServiceProvider
                        .GetRequiredService<ArticleSummaryAutomationProcessor>()
                        .ProcessNextAsync(stoppingToken);
                    if (processed)
                        continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Article summary worker failed ({ErrorType}); queue processing will retry.",
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
