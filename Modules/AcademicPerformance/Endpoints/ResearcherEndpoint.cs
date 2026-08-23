using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using Microsoft.Data.Sqlite;
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
        catch (SqliteException exception) when (exception.SqliteErrorCode == 10)
        {
            logger.LogError(exception, "SQLite veritabanında disk I/O hatası oluştu.");

            feedbackText = string.Join(Environment.NewLine,
                "[HATA] SQLite veritabanı dosyasına erişilemedi (disk I/O).",
                "[ÖNERİ] WSL kullanıyorsanız projeyi /mnt/c veya OneDrive yerine " +
                "Linux dosya sisteminde (ör. ~/researcher) çalıştırın.",
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

    [HttpPost]
    public async Task<ResearcherRandomResponse> Random(
        ServiceRequest request,
        [FromServices] AcademicDatabaseInitializer databaseInitializer,
        [FromServices] DatabaseMaintenance databaseMaintenance,
        [FromServices] ResearcherSummaryFactory summaryFactory)
    {
        ResearcherRandomResponse? response = null;
        Researcher? researcher = null;

        await databaseInitializer.EnsureReadyAsync();
        researcher = await databaseMaintenance.GetRandomResearcherAsync();
        response = new ResearcherRandomResponse();
        response.Researcher = summaryFactory.Create(researcher);

        return response;
    }
}
