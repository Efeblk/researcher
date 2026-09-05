using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Infrastructure;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly string _databaseName = "AcademicCollectorTests_" + Guid.NewGuid().ToString("N");
    private string _masterConnectionString = string.Empty;
    private bool _created;
    public ServiceProvider Services { get; private set; } = null!;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        // Never load the application's connection string or user secrets.
        SqlConnectionStringBuilder connection = new(
            Environment.GetEnvironmentVariable("ACADEMIC_TEST_SQLSERVER") ??
            @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true");
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
                ["ConnectionStrings:AcademicDatabase"] = connection.ConnectionString
            }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAcademicPerformanceModule(configuration);
        services.AddSingleton(new HttpClient(new StubHttpHandler(_ =>
            throw new InvalidOperationException("A test attempted an unconfigured provider request."))));
        Services = services.BuildServiceProvider();
        Services.MigrateAcademicDatabase();
    }

    public async Task DisposeAsync()
    {
        if (Services is not null)
            await Services.DisposeAsync();

        if (!_created)
            return;

        // Only the randomly named database created by this fixture can be removed.
        await using SqlConnection master = new(_masterConnectionString);
        await master.OpenAsync();
        await using SqlCommand drop = master.CreateCommand();
        drop.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]";
        await drop.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("SQL Server")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
