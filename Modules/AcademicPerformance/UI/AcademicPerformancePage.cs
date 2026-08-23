using Microsoft.AspNetCore.Mvc;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.UI;

public sealed class AcademicPerformancePage : Controller
{
    [HttpGet("AcademicPerformance")]
    public IActionResult Index()
    {
        return View("~/Modules/AcademicPerformance/UI/AcademicPerformanceIndex.cshtml");
    }
}
