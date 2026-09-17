namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;

internal static class YoksisCollectionProgressMessages
{
    public static string Completion(YoksisCollectResponse response)
    {
        if (response.IsCached)
            return "YÖKSİS verileri önbellekten yüklendi.";

        if (response.SuccessfulCategoryCount == 0)
            return response.StopReason ?? "YÖKSİS kategorilerinden veri alınamadı.";

        if (!response.IsSaved)
            return "YÖKSİS verileri toplandı fakat veritabanına kaydedilemedi.";

        if (response.FailedCategoryCount > 0)
        {
            return response.StopReason is null
                ? "YÖKSİS toplaması kısmen tamamlandı; bazı kategoriler alınamadı."
                : $"YÖKSİS toplaması kısmen tamamlandı. {response.StopReason}";
        }

        return "YÖKSİS toplaması ve kayıt işlemi tamamlandı.";
    }
}
