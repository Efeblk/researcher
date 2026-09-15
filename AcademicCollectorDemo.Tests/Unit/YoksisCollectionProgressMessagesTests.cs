using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class YoksisCollectionProgressMessagesTests
{
    [Fact]
    public void Completion_PartialCollectionAndSaveFailure_ReportsSaveFailure()
    {
        YoksisCollectResponse response = new()
        {
            SuccessfulCategoryCount = 2,
            FailedCategoryCount = 1,
            IsSaved = false
        };

        string message = YoksisCollectionProgressMessages.Completion(response);

        Assert.Contains("veritabanına kaydedilemedi", message);
    }

    [Fact]
    public void Completion_SavedPartialCollection_ReportsPartialCollection()
    {
        YoksisCollectResponse response = new()
        {
            SuccessfulCategoryCount = 2,
            FailedCategoryCount = 1,
            IsSaved = true
        };

        string message = YoksisCollectionProgressMessages.Completion(response);

        Assert.Contains("kısmen tamamlandı", message);
    }
}
