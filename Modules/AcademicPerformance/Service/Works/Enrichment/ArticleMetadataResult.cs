using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Enrichment;

public sealed record ArticleMetadataResult(
    string? Abstract,
    IReadOnlyList<AcademicWorkSource> Sources,
    string Status);
