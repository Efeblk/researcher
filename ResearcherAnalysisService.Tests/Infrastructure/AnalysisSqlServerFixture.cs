using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResearcherAnalysisService.Data;

namespace ResearcherAnalysisService.Tests.Infrastructure;

public sealed class AnalysisSqlServerFixture : IAsyncLifetime
{
    private readonly string _databaseName = "ResearcherAnalysisTests_" + Guid.NewGuid().ToString("N");
    private string _masterConnectionString = string.Empty;
    private bool _created;
    public ServiceProvider Services { get; private set; } = null!;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        SqlConnectionStringBuilder connection = new(
            Environment.GetEnvironmentVariable("ACADEMIC_TEST_SQLSERVER") ??
            @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Encrypt=true;TrustServerCertificate=true");
        connection.Encrypt = SqlConnectionEncryptOption.Mandatory;
        connection.InitialCatalog = "master";
        _masterConnectionString = connection.ConnectionString;
        await using SqlConnection master = new(_masterConnectionString);
        await master.OpenAsync();
        await using SqlCommand create = master.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{_databaseName}]";
        await create.ExecuteNonQueryAsync();
        _created = true;

        connection.InitialCatalog = _databaseName;
        ConnectionString = connection.ConnectionString;
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UsageDatabase"] = ConnectionString
            }).Build();
        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddAnalysisDatabaseMigrations(configuration);
        Services = services.BuildServiceProvider();
        Services.MigrateAnalysisDatabase();
    }

    public async Task DisposeAsync()
    {
        if (Services is not null)
            await Services.DisposeAsync();
        if (!_created)
            return;

        await using SqlConnection master = new(_masterConnectionString);
        await master.OpenAsync();
        await using SqlCommand drop = master.CreateCommand();
        drop.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{_databaseName}]";
        await drop.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("Analysis SQL Server")]
public sealed class AnalysisSqlServerCollection : ICollectionFixture<AnalysisSqlServerFixture>;
