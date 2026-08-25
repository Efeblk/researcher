using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Background;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using FluentMigrator.Runner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Modules.AcademicPerformance;

public static class AcademicPerformanceModule
{
    public static IServiceCollection AddAcademicPerformanceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = DatabaseConfiguration.GetConnectionString(
            configuration);

        services.AddSingleton(CreateHttpClient());
        services.AddFluentMigratorCore()
            .ConfigureRunner(runner =>
            {
                runner
                    .AddSqlServer()
                    .WithGlobalConnectionString(connectionString)
                    .ScanIn(typeof(AcademicPerformanceModule).Assembly)
                    .For.Migrations();
            });

        services.AddSingleton<ResearcherIdentifierParser>();
        services.AddSingleton<AcademicWorkCategorizer>();
        services.AddSingleton<ResearcherCollectionFeedback>();
        services.Configure<AcademicCollectionSchedulerOptions>(
            configuration.GetSection("AcademicPerformance:ScheduledCollection"));
        services.AddTransient<OrcidClient>();
        services.AddTransient<WebOfScienceClient>();
        services.AddTransient<YoksisClient>();
        services.AddTransient<YoksisCollectionService>();
        services.AddScoped<YoksisRecordSynchronizer>();
        services.AddScoped<YoksisAcademicWorkSynchronizer>();
        services.AddScoped<YoksisCollectionHandler>();

        services.AddDbContext<AcademicDbContext>(optionsBuilder =>
            DatabaseConfiguration.Configure(optionsBuilder, configuration));
        services.AddScoped<AcademicDatabaseMigrator>();
        services.AddScoped<ResearcherRepository>();
        services.AddScoped<AcademicWorkSynchronizer>();
        services.AddScoped<PublicationSummarySynchronizer>();
        services.AddScoped<ResearcherCollectionService>();
        services.AddScoped<ResearcherCollectionHandler>();
        services.AddScoped<IAcademicPerformanceApplicationService,
            AcademicPerformanceApplicationService>();
        services.AddHostedService<AcademicCollectionBackgroundService>();
        return services;
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient httpClient = new();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AcademicCollectorDemo/0.1");

        return httpClient;
    }
}
