using System.ComponentModel.DataAnnotations;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ResearcherAnalysisIdRequest
{
    [Range(1, int.MaxValue)]
    public int ResearcherId { get; set; }
}
