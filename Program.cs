using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Host;
using Microsoft.AspNetCore.Mvc;
using Serenity;
using Serenity.Extensions.DependencyInjection;

public static class Program
{
    private const string CleanDatabaseArgument = "--clean-database";

    public static async Task Main(string[] args)
    {
        bool cleanDatabase = args.Any(argument => string.Equals(
            argument,
            CleanDatabaseArgument,
            StringComparison.OrdinalIgnoreCase));
        string[] hostArguments = args
            .Where(argument => !string.Equals(
                argument,
                CleanDatabaseArgument,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            hostArguments);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        if (cleanDatabase)
        {
            builder.Logging.AddFilter("FluentMigrator", LogLevel.Warning);
        }

        builder.Configuration
            .AddJsonFile(
                "appsettings.bundles.json",
                optional: false,
                reloadOnChange: true)
            .AddJsonFile(
                "academicsettings.json",
                optional: false,
                reloadOnChange: true);

        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddUserSecrets(
                typeof(Program).Assembly,
                optional: true,
                reloadOnChange: true);
        }

        builder.Configuration
            .AddEnvironmentVariables()
            .AddCommandLine(hostArguments);

        builder.Services.AddAcademicPerformanceModule(builder.Configuration);
        builder.Services.AddApplicationPartsTypeSource();
        builder.Services.ConfigureSections(builder.Configuration);
        builder.Services.AddCaching();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<Serenity.Data.IRowFieldsProvider,
            Serenity.Data.DefaultRowFieldsProvider>();
        builder.Services.AddSingleton<Serenity.Abstractions.IPermissionService,
            DevelopmentPermissionService>();
        builder.Services.AddControllersWithViews();
        builder.Services.AddServiceEndpointConventions();
        builder.Services.AddDynamicScripts();
        builder.Services.AddCssBundling();
        builder.Services.AddScriptBundling();
        builder.Services.Configure<JsonOptions>(options =>
            JSON.Defaults.Populate(options.JsonSerializerOptions));

        WebApplication application = builder.Build();
        Serenity.Data.RowFieldsProvider.SetDefaultFrom(application.Services);

        if (cleanDatabase)
        {
            if (!application.Environment.IsDevelopment())
            {
                Console.Error.WriteLine(
                    "[HATA] Veritabanı temizleme yalnız Development " +
                    "ortamında kullanılabilir.");
                Environment.ExitCode = 1;
                return;
            }

            application.Services.CleanAcademicDatabase();
            Console.WriteLine(
                "Uygulama tabloları ve verileri veritabanından kaldırıldı.");
            return;
        }

        application.Services.MigrateAcademicDatabase();

        application.UseStaticFiles();
        application.UseRouting();
        application.UseDynamicScripts();

        application.MapGet("/", () => Results.Ok(new
        {
            Application = "AcademicCollectorDemo",
            Status = "Running"
        }));
        application.MapControllers();

        await application.RunAsync();
    }
}
