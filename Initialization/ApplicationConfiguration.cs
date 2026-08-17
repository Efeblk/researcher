using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Initialization;

public static class ApplicationConfiguration
{
    public static IConfigurationRoot Create()
    {
        IConfigurationRoot? configuration = null;

        configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<UserSecretsMarker>()
            .Build();

        return configuration;
    }
}

internal sealed class UserSecretsMarker
{
}
