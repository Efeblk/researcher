using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class AcademicWorkTypeSynchronizationTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task SyncAsync_TrDizinTurkishType_PersistsNormalizedCategoryAndRawType()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string personelId = "type-sync-" + Guid.NewGuid().ToString("N");
        Researcher researcher = new()
        {
            PersonelId = personelId,
            TrDizinProfile = new TrDizinProfile
            {
                Works =
                [
                    new TrDizinWork
                    {
                        PublicationId = "type-1",
                        Title = "Normalized type",
                        PublicationType = "KİTAP_BÖLÜMÜ"
                    }
                ]
            }
        };
        database.Researchers.Add(researcher);
        await database.SaveChangesAsync();

        await new AcademicWorkSynchronizer(database).SyncAsync(researcher);

        AcademicWork work = await database.AcademicWorks.SingleAsync(
            item => item.PersonelId == personelId);
        Assert.Equal(AcademicWorkCategory.BookChapter, work.Category);
        Assert.Equal(AcademicWorkCategorySource.TrDizin, work.CategorySource);
        Assert.Equal("KİTAP_BÖLÜMÜ", work.RawType);
    }
}
