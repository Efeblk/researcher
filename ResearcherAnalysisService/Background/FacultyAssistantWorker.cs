using ResearcherAnalysisService.Products.FacultyAssistant;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Background;

public sealed class FacultyAssistantWorker(IServiceScopeFactory scopes,
    IOptionsMonitor<FacultyAssistantOptions> options, ILogger<FacultyAssistantWorker> logger) : BackgroundService
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
                    if (await scope.ServiceProvider.GetRequiredService<FacultyAssistantProcessor>().ProcessNextAsync(stoppingToken))
                        continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError("Faculty assistant worker failed ({ErrorType}).", exception.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.CurrentValue.PollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
