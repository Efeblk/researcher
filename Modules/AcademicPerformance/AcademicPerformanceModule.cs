using AcademicCollectorDemo.Modules.AcademicPerformance.Console;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
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
        services.AddSingleton(configuration);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(CreateHttpClient());

        services.AddSingleton<ResearcherIdentifierParser>();
        services.AddSingleton<ResearcherConsolePresenter>();

        services.AddTransient<OpenAlexClient>();
        services.AddTransient<GoogleScholarClient>();
        services.AddTransient<ScopusClient>();
        services.AddTransient<WebOfScienceClient>();

        services.AddScoped(CreateDbContext);
        services.AddScoped<AcademicDatabaseInitializer>();
        services.AddScoped<ResearcherRepository>();
        services.AddScoped<ResearcherCollectionService>();
        services.AddScoped<ResearcherEndpoint>();
        services.AddScoped<DatabaseMaintenance>();
        services.AddScoped<AcademicPerformanceConsoleHost>();

        return services;
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient? httpClient = null;

        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AcademicCollectorDemo/0.1");

        return httpClient;
    }

    private static AcademicDbContext CreateDbContext(IServiceProvider serviceProvider)
    {
        IConfiguration? configuration = null;
        DbContextOptionsBuilder<AcademicDbContext>? optionsBuilder = null;

        configuration = serviceProvider.GetRequiredService<IConfiguration>();
        optionsBuilder = new DbContextOptionsBuilder<AcademicDbContext>();
        DatabaseConfiguration.Configure(optionsBuilder, configuration);

        return new AcademicDbContext(optionsBuilder.Options);
    }
}
