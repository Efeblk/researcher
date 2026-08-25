using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Background;

public sealed class AcademicCollectionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AcademicCollectionSchedulerOptions _options;
    private readonly ILogger<AcademicCollectionBackgroundService> _logger;

    public AcademicCollectionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AcademicCollectionSchedulerOptions> options,
        ILogger<AcademicCollectionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Akademik veri zamanlayıcısı devre dışı.");
            return;
        }

        TimeSpan initialDelay = TimeSpan.FromSeconds(
            Math.Max(_options.InitialDelaySeconds, 0));
        TimeSpan interval = TimeSpan.FromMinutes(
            Math.Max(_options.IntervalMinutes, 1));

        if (initialDelay > TimeSpan.Zero)
        {
            await Task.Delay(initialDelay, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshResearchersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Zamanlanmış akademik veri toplama turu tamamlanamadı.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RefreshResearchersAsync(CancellationToken stoppingToken)
    {
        int lastResearcherId = 0;
        int processedCount = 0;
        int batchSize = Math.Clamp(_options.BatchSize, 1, 1000);

        _logger.LogInformation("Zamanlanmış akademik veri toplama turu başladı.");

        while (!stoppingToken.IsCancellationRequested)
        {
            List<ScheduledResearcherTarget> targets = await GetTargetsAsync(
                lastResearcherId,
                batchSize,
                stoppingToken);

            if (targets.Count == 0)
            {
                break;
            }

            foreach (ScheduledResearcherTarget target in targets)
            {
                stoppingToken.ThrowIfCancellationRequested();
                await RefreshResearcherAsync(target);
                lastResearcherId = target.ResearcherId;
                processedCount++;
            }
        }

        _logger.LogInformation(
            "Zamanlanmış akademik veri toplama turu tamamlandı. İşlenen: {Count}",
            processedCount);
    }

    private async Task<List<ScheduledResearcherTarget>> GetTargetsAsync(
        int lastResearcherId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        AcademicDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<AcademicDbContext>();

        return await dbContext.Researchers
            .AsNoTracking()
            .Where(researcher =>
                researcher.Id > lastResearcherId &&
                (researcher.Orcid != null ||
                 researcher.WebOfScienceResearcherId != null))
            .OrderBy(researcher => researcher.Id)
            .Take(batchSize)
            .Select(researcher => new ScheduledResearcherTarget
            {
                ResearcherId = researcher.Id,
                Orcid = researcher.Orcid,
                WebOfScienceResearcherId = researcher.WebOfScienceResearcherId
            })
            .ToListAsync(cancellationToken);
    }

    private async Task RefreshResearcherAsync(ScheduledResearcherTarget target)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IAcademicPerformanceApplicationService applicationService =
                scope.ServiceProvider.GetRequiredService<
                    IAcademicPerformanceApplicationService>();
            AcademicDataResponse response = await applicationService.CollectAsync(
                new AcademicDataCollectRequest
                {
                    Orcid = target.Orcid,
                    WebOfScienceResearcherId = target.WebOfScienceResearcherId
                });

            if (!response.IsSaved)
            {
                _logger.LogWarning(
                    "Akademisyen {ResearcherId} zamanlanmış toplamada güncellenemedi.",
                    target.ResearcherId);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Akademisyen {ResearcherId} zamanlanmış toplamada hata verdi.",
                target.ResearcherId);
        }
    }

    private sealed class ScheduledResearcherTarget
    {
        public int ResearcherId { get; set; }
        public string? Orcid { get; set; } = null;
        public string? WebOfScienceResearcherId { get; set; } = null;
    }
}
