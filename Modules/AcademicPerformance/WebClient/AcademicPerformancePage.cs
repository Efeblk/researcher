using Microsoft.AspNetCore.Mvc;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient;

public sealed class AcademicPerformancePage : Controller
{
    [HttpGet("AcademicPerformance")]
    public IActionResult Index()
    {
        return View(
            "~/Modules/AcademicPerformance/WebClient/AcademicPerformanceIndex.cshtml");
    }
}
