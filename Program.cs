using AcademicCollectorDemo.Modules.AcademicPerformance;
using Microsoft.AspNetCore.Mvc;
using Serenity;
using Serenity.Extensions.DependencyInjection;

public static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder? builder = null;
        WebApplication? application = null;

        builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();
        builder.Configuration
            .AddJsonFile(
                "appsettings.bundles.json",
                optional: false,
                reloadOnChange: true)
            .AddJsonFile(
                "academicsettings.json",
                optional: false,
                reloadOnChange: true)
            .AddUserSecrets(
                typeof(Program).Assembly,
                optional: true,
                reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);

        builder.Services.AddAcademicPerformanceModule(builder.Configuration);
        builder.Services.AddApplicationPartsTypeSource();
        builder.Services.ConfigureSections(builder.Configuration);
        builder.Services.AddCaching();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<Serenity.Data.IRowFieldsProvider,
            Serenity.Data.DefaultRowFieldsProvider>();
        builder.Services.AddSingleton<Serenity.Abstractions.IPermissionService,
            AcademicCollectorDemo.Modules.AcademicPerformance.UI.PrototypePermissionService>();
        builder.Services.AddControllersWithViews();
        builder.Services.AddServiceEndpointConventions();
        builder.Services.AddDynamicScripts();
        builder.Services.AddCssBundling();
        builder.Services.AddScriptBundling();
        builder.Services.Configure<JsonOptions>(options =>
            JSON.Defaults.Populate(options.JsonSerializerOptions));

        application = builder.Build();
        Serenity.Data.RowFieldsProvider.SetDefaultFrom(application.Services);

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
