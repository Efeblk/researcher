using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Data;
using ResearcherAnalysisService.Products;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;

namespace ResearcherAnalysisService.Tests.Infrastructure;

public sealed class AnalysisProductSqlServerFixture : IAsyncLifetime
{
    private readonly string _databaseName = "AnalysisProductTests_" + Guid.NewGuid().ToString("N");
    private string _masterConnectionString = string.Empty;
    private bool _created;

    public string ConnectionString { get; private set; } = string.Empty;
    public ServiceProvider Services { get; private set; } = null!;

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
        await CreateSourceSchemaAsync();

        IConfiguration configuration = Configuration();
        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(configuration);
        services.AddAcademicProducts(configuration);
        services.AddAnalysisDatabaseMigrations(configuration);
        Services = services.BuildServiceProvider();
        Services.MigrateAnalysisDatabase();
    }

    public DbContext CreateSeedContext()
    {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseSqlServer(ConnectionString)
            .UseModel(SourceModel())
            .Options;
        return new DbContext(options);
    }

    public async Task<SyntheticCanonicalSource> SeedCanonicalSourceAsync(
        string personelId,
        string? title = null,
        string? doi = null)
    {
        doi ??= "10.9876/" + Guid.NewGuid().ToString("N");
        Researcher researcher = new() { PersonelId = personelId };
        CanonicalWork canonical = new()
        {
            NormalizedDoi = doi,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        AcademicWork academic = new()
        {
            PersonelId = personelId,
            Provider = AcademicWorkProvider.OpenAlex,
            ProviderWorkId = "https://openalex.org/W" + Guid.NewGuid().ToString("N"),
            Title = title ?? "Synthetic source article",
            Doi = doi,
            PublicationYear = 2025,
            Abstract = "A synthetic source abstract used only by the isolated test database.",
            SyncedAt = DateTime.UtcNow,
            CanonicalObservation = new CanonicalWorkObservation
            {
                CanonicalWork = canonical,
                PersonelId = personelId,
                Provider = AcademicWorkProvider.OpenAlex,
                DoiObserved = doi,
                TitleObserved = title ?? "Synthetic source article",
                ObservedAt = DateTime.UtcNow
            }
        };
        CanonicalResearcherWork association = new()
        {
            CanonicalWork = canonical,
            Researcher = researcher,
            PersonelId = personelId,
            LastObservedAt = DateTime.UtcNow
        };
        await using DbContext source = CreateSeedContext();
        source.AddRange(researcher, academic, association);
        await source.SaveChangesAsync();

        DbContextOptions<AnalysisDbContext> options =
            new DbContextOptionsBuilder<AnalysisDbContext>()
                .UseSqlServer(ConnectionString).Options;
        await using AnalysisDbContext database = new(options);
        string sourceIdentity = (await CanonicalSourceIdentity.LoadAsync(
            database, canonical.Id, default))!;
        return new(personelId, academic.Id, canonical.Id, sourceIdentity);
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

    private IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:UsageDatabase"] = ConnectionString,
            ["CollectionChanges:WorkerEnabled"] = "false",
            ["ArticleSummaryAutomation:Enabled"] = "false",
            ["ArticleSummaryAutomation:WorkerEnabled"] = "false",
            ["PublicationMetrics:WorkerEnabled"] = "false",
            ["ArticleEvaluation:WorkerEnabled"] = "false",
            ["FacultyAssistant:WorkerEnabled"] = "false"
        }).Build();

    private async Task CreateSourceSchemaAsync()
    {
        IModel model = SourceModel();
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseSqlServer(ConnectionString)
            .UseModel(model)
            .Options;
        await using (DbContext bootstrap = new(options))
        {
            IRelationalDatabaseCreator creator =
                bootstrap.GetService<IRelationalDatabaseCreator>();
            await creator.CreateTablesAsync();
        }

        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) +
                N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' +
                QUOTENAME(name) + N';'
            FROM sys.foreign_keys
            WHERE SCHEMA_NAME(schema_id) IN (N'analysis', N'hr', N'faculty')
               OR SCHEMA_NAME(OBJECTPROPERTY(parent_object_id, 'SchemaId')) IN
                    (N'analysis', N'hr', N'faculty');
            IF @sql <> N'' EXEC sys.sp_executesql @sql;

            SET @sql = N'';
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) +
                N'.' + QUOTENAME(name) + N';'
            FROM sys.tables
            WHERE SCHEMA_NAME(schema_id) IN (N'analysis', N'hr', N'faculty');
            IF @sql <> N'' EXEC sys.sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
        command.CommandText =
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'core') AND name=N'Researchers';";
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1)
            throw new InvalidOperationException(
                "The synthetic analysis source schema did not create core.Researchers.");
    }

    private IModel SourceModel()
    {
        DbContextOptions<AnalysisDbContext> options =
            new DbContextOptionsBuilder<AnalysisDbContext>()
                .UseSqlServer(ConnectionString).Options;
        using AnalysisDbContext context = new(options);
        return context.GetService<IDesignTimeModel>().Model;
    }
}

[CollectionDefinition("Analysis Product SQL Server")]
public sealed class AnalysisProductSqlServerCollection :
    ICollectionFixture<AnalysisProductSqlServerFixture>;

public sealed record SyntheticCanonicalSource(
    string PersonelId,
    int AcademicWorkId,
    int CanonicalWorkId,
    string SourceIdentityHash);

public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public static class AnalysisSourceSeedExtensions
{
    public static async Task<int> SaveChangesWithSourceSeedAsync(
        this AnalysisDbContext database,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var sourceEntries = database.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry => entry.Metadata.GetSchema() is not ("analysis" or "hr" or "faculty"))
            .Select(entry => new
            {
                entry.Entity,
                entry.State
            })
            .ToArray();
        if (sourceEntries.Length == 0)
            return await database.SaveChangesAsync(cancellationToken);

        foreach (var entry in sourceEntries)
            database.Entry(entry.Entity).State = EntityState.Detached;

        DbContextOptions options = new DbContextOptionsBuilder()
            .UseSqlServer(connectionString)
            .UseModel(database.Model)
            .Options;
        await using DbContext source = new(options);
        foreach (var entry in sourceEntries)
            source.Entry(entry.Entity).State = entry.State;
        int written = await source.SaveChangesAsync(cancellationToken);

        foreach (var entry in sourceEntries.Where(entry => entry.State != EntityState.Deleted))
            database.Entry(entry.Entity).State = EntityState.Unchanged;
        return written + await database.SaveChangesAsync(cancellationToken);
    }
}
