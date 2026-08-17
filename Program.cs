using AcademicCollectorDemo.Initialization;
using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Console;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

public static class Program
{
    public static async Task Main(string[] args)
    {
        IConfigurationRoot? configuration = null;
        ServiceProvider? serviceProvider = null;
        IServiceScope? serviceScope = null;
        AcademicPerformanceConsoleHost? consoleHost = null;

        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            configuration = ApplicationConfiguration.Create();
            serviceProvider = new ServiceCollection()
                .AddAcademicPerformanceModule(configuration)
                .BuildServiceProvider();
            serviceScope = serviceProvider.CreateScope();
            consoleHost = serviceScope.ServiceProvider
                .GetRequiredService<AcademicPerformanceConsoleHost>();

            await consoleHost.RunAsync(args);
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"Geçersiz argüman: {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Beklenmeyen hata: {exception.Message}");
        }
        finally
        {
            serviceScope?.Dispose();
            serviceProvider?.Dispose();
        }
    }
}
