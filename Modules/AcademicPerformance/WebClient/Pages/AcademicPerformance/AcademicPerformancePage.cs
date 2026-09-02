using Microsoft.AspNetCore.Mvc;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Pages.AcademicPerformance;

public sealed class AcademicPerformancePage : Controller
{
    [HttpGet("AcademicPerformance")]
    public IActionResult Index()
    {
        return View(
            "~/Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/Index.cshtml");
    }
}
