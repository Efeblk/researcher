using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

public sealed class DatabaseMaintenance
{
    private readonly IConfiguration _configuration;

    public DatabaseMaintenance(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task ClearSqliteAsync()
    {
        string? provider = null;
        DbContextOptionsBuilder<AcademicDbContext>? optionsBuilder = null;
        AcademicDbContext? dbContext = null;

        try
        {
            optionsBuilder = new DbContextOptionsBuilder<AcademicDbContext>();
            provider = DatabaseConfiguration.Configure(optionsBuilder, _configuration);

            if (provider != DatabaseConfiguration.SqliteProvider)
            {
                throw new InvalidOperationException(
                    "--clear-db komutu güvenlik nedeniyle yalnızca SQLite için kullanılabilir.");
            }

            dbContext = new AcademicDbContext(optionsBuilder.Options);

            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();

            Console.WriteLine("SQLite veritabanı temizlendi ve boş tablolar yeniden oluşturuldu.");
        }
        finally
        {
            if (dbContext is not null)
            {
                await dbContext.DisposeAsync();
            }
        }
    }

    public async Task PrintSummaryAsync()
    {
        string? provider = null;
        DbContextOptionsBuilder<AcademicDbContext>? optionsBuilder = null;
        AcademicDbContext? dbContext = null;
        int researcherCount = 0;
        int openAlexProfileCount = 0;
        int openAlexWorkCount = 0;
        int googleScholarProfileCount = 0;
        int googleScholarWorkCount = 0;
        int googleScholarInterestCount = 0;

        try
        {
            optionsBuilder = new DbContextOptionsBuilder<AcademicDbContext>();
            provider = DatabaseConfiguration.Configure(optionsBuilder, _configuration);
            dbContext = new AcademicDbContext(optionsBuilder.Options);

            await dbContext.Database.EnsureCreatedAsync();

            researcherCount = await dbContext.Researchers.CountAsync();
            openAlexProfileCount = await dbContext.OpenAlexProfiles.CountAsync();
            openAlexWorkCount = await dbContext.OpenAlexWorks.CountAsync();
            googleScholarProfileCount = await dbContext.GoogleScholarProfiles.CountAsync();
            googleScholarWorkCount = await dbContext.GoogleScholarWorks.CountAsync();
            googleScholarInterestCount = await dbContext.GoogleScholarInterests.CountAsync();

            Console.WriteLine($"Veritabanı sağlayıcısı     : {provider}");
            Console.WriteLine($"Akademisyen sayısı         : {researcherCount}");
            Console.WriteLine($"OpenAlex profil sayısı     : {openAlexProfileCount}");
            Console.WriteLine($"OpenAlex yayın sayısı      : {openAlexWorkCount}");
            Console.WriteLine($"Google Scholar profil sayısı: {googleScholarProfileCount}");
            Console.WriteLine($"Google Scholar yayın sayısı : {googleScholarWorkCount}");
            Console.WriteLine($"Google Scholar ilgi sayısı  : {googleScholarInterestCount}");
        }
        finally
        {
            if (dbContext is not null)
            {
                await dbContext.DisposeAsync();
            }
        }
    }
}
