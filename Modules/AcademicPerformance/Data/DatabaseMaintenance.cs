using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public sealed class DatabaseMaintenance
{
    private readonly AcademicDbContext _dbContext;
    private readonly AcademicDatabaseInitializer _databaseInitializer;

    public DatabaseMaintenance(
        AcademicDbContext dbContext,
        AcademicDatabaseInitializer databaseInitializer)
    {
        _dbContext = dbContext;
        _databaseInitializer = databaseInitializer;
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
