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
        builder.Configuration
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
        builder.Services.AddControllers();
        builder.Services.AddServiceEndpointConventions();
        builder.Services.Configure<JsonOptions>(options =>
            JSON.Defaults.Populate(options.JsonSerializerOptions));

        application = builder.Build();
        application.MapGet("/", () => Results.Ok(new
        {
            Application = "AcademicCollectorDemo",
            Status = "Running"
        }));
        application.MapControllers();

        await application.RunAsync();
    }
}
