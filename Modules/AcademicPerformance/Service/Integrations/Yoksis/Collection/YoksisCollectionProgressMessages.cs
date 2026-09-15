namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;

internal static class YoksisCollectionProgressMessages
{
    public static string Completion(YoksisCollectResponse response)
    {
        if (response.SuccessfulCategoryCount == 0)
            return response.StopReason ?? "YÖKSİS kategorilerinden veri alınamadı.";

        if (response.FailedCategoryCount > 0)
        {
            return response.StopReason is null
                ? "YÖKSİS toplaması kısmen tamamlandı; bazı kategoriler alınamadı."
                : $"YÖKSİS toplaması kısmen tamamlandı. {response.StopReason}";
        }

        return response.IsSaved
            ? "YÖKSİS toplaması ve kayıt işlemi tamamlandı."
            : "YÖKSİS toplaması tamamlandı fakat kayıt işlemi başarısız oldu.";
    }
}
