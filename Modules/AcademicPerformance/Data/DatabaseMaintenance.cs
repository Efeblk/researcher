using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public sealed class DatabaseMaintenance
{
    private readonly IConfiguration _configuration;
    private readonly AcademicDbContext _dbContext;
    private readonly AcademicDatabaseInitializer _databaseInitializer;

    public DatabaseMaintenance(
        IConfiguration configuration,
        AcademicDbContext dbContext,
        AcademicDatabaseInitializer databaseInitializer)
    {
        _configuration = configuration;
        _dbContext = dbContext;
        _databaseInitializer = databaseInitializer;
    }

    public async Task ClearSqliteAsync()
    {
        string? provider = null;

        provider = _configuration["Database:Provider"]
            ?? DatabaseConfiguration.SqliteProvider;

        if (!provider.Equals(
            DatabaseConfiguration.SqliteProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "--clear-db komutu güvenlik nedeniyle yalnızca SQLite için kullanılabilir.");
        }

        await _dbContext.Database.EnsureDeletedAsync();
        await _databaseInitializer.EnsureReadyAsync();

        System.Console.WriteLine("SQLite veritabanı temizlendi ve boş tablolar yeniden oluşturuldu.");
    }

    public async Task PrintSummaryAsync()
    {
        string? provider = null;
        int researcherCount = 0;
        int openAlexProfileCount = 0;
        int openAlexWorkCount = 0;
        int googleScholarProfileCount = 0;
        int googleScholarWorkCount = 0;
        int googleScholarInterestCount = 0;
        int scopusProfileCount = 0;
        int webOfScienceProfileCount = 0;

        provider = _configuration["Database:Provider"]
            ?? DatabaseConfiguration.SqliteProvider;

        await _databaseInitializer.EnsureReadyAsync();

        researcherCount = await _dbContext.Researchers.CountAsync();
        openAlexProfileCount = await _dbContext.OpenAlexProfiles.CountAsync();
        openAlexWorkCount = await _dbContext.OpenAlexWorks.CountAsync();
        googleScholarProfileCount = await _dbContext.GoogleScholarProfiles.CountAsync();
        googleScholarWorkCount = await _dbContext.GoogleScholarWorks.CountAsync();
        googleScholarInterestCount = await _dbContext.GoogleScholarInterests.CountAsync();
        scopusProfileCount = await _dbContext.ScopusProfiles.CountAsync();
        webOfScienceProfileCount = await _dbContext.WebOfScienceProfiles.CountAsync();

        System.Console.WriteLine($"Veritabanı sağlayıcısı     : {provider}");
        System.Console.WriteLine($"Akademisyen sayısı         : {researcherCount}");
        System.Console.WriteLine($"OpenAlex profil sayısı     : {openAlexProfileCount}");
        System.Console.WriteLine($"OpenAlex yayın sayısı      : {openAlexWorkCount}");
        System.Console.WriteLine($"Google Scholar profil sayısı: {googleScholarProfileCount}");
        System.Console.WriteLine($"Google Scholar yayın sayısı : {googleScholarWorkCount}");
        System.Console.WriteLine($"Google Scholar ilgi sayısı  : {googleScholarInterestCount}");
        System.Console.WriteLine($"Scopus profil sayısı         : {scopusProfileCount}");
        System.Console.WriteLine($"Web of Science profil sayısı : {webOfScienceProfileCount}");
    }

    public async Task<Researcher?> GetRandomResearcherAsync()
    {
        Researcher? researcher = null;
        int researcherCount = 0;
        int randomIndex = 0;

        await _databaseInitializer.EnsureReadyAsync();

        researcherCount = await _dbContext.Researchers.CountAsync();

        if (researcherCount == 0)
        {
            return null;
        }

        randomIndex = Random.Shared.Next(researcherCount);
        researcher = await _dbContext.Researchers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.OpenAlex)
                .ThenInclude(openAlex => openAlex!.Works)
            .Include(item => item.GoogleScholar)
                .ThenInclude(googleScholar => googleScholar!.Works)
            .Include(item => item.GoogleScholar)
                .ThenInclude(googleScholar => googleScholar!.Interests)
            .Include(item => item.Scopus)
            .Include(item => item.WebOfScience)
            .OrderBy(item => item.Id)
            .Skip(randomIndex)
            .FirstOrDefaultAsync();

        return researcher;
    }
}
