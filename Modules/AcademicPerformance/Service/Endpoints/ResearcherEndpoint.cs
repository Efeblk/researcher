using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Endpoints;

[Route("Services/AcademicPerformance/Researcher/[action]")]
public sealed class ResearcherEndpoint : ServiceEndpoint
{
    [HttpPost]
    public Task<ResearcherCollectResponse> Collect(
        ResearcherCollectRequest request,
        [FromServices] ResearcherCollectionHandler handler)
    {
        return handler.CollectAsync(request);
    }

    [HttpPost]
    public async Task<ContentResult> CollectText(
        ResearcherCollectRequest request,
        [FromServices] ResearcherCollectionHandler handler,
        [FromServices] ILogger<ResearcherEndpoint> logger)
    {
        ResearcherCollectResponse? response = null;
        string? feedbackText = null;

        try
        {
            response = await handler.CollectAsync(request);
            feedbackText = string.Join(Environment.NewLine, response.Messages);

            return Content(feedbackText, "text/plain; charset=utf-8");
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "SQL Server veritabanı hatası oluştu.");

            feedbackText = string.Join(Environment.NewLine,
                "[HATA] SQL Server veritabanı işlemi tamamlanamadı.",
                "[ÖNERİ] Bağlantı cümlesini, sunucunun erişilebilirliğini ve " +
                "veritabanı yetkilerini kontrol edin.",
                "[BİLGİ] Teknik ayrıntılar sunucu terminaline yazıldı.");

            return new ContentResult
            {
                Content = feedbackText,
                ContentType = "text/plain; charset=utf-8",
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Akademik veri toplama işlemi tamamlanamadı.");

            feedbackText = string.Join(Environment.NewLine,
                "[HATA] Toplama işlemi beklenmeyen bir nedenle tamamlanamadı.",
                $"[DETAY] {exception.Message}",
                "[BİLGİ] Teknik ayrıntılar sunucu terminaline yazıldı.");

            return new ContentResult
            {
                Content = feedbackText,
                ContentType = "text/plain; charset=utf-8",
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
