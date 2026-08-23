namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherMetricsUpdater
{
    public void Update(Researcher researcher)
    {
        ResearcherMetrics? metrics = null;

        if (researcher.OrcidProfile is null)
        {
            return;
        }

        metrics = researcher.Metrics ?? new ResearcherMetrics();
        metrics.Researcher = researcher;
        metrics.ResearcherId = researcher.Id;
        metrics.WorksCount = researcher.OrcidProfile.Works?.Count
            ?? researcher.OrcidProfile.WorksCount;
        metrics.CitedByCount = null;
        metrics.HIndex = null;
        metrics.I10Index = null;
        metrics.Source = "ORCID";
        metrics.UpdatedAt = researcher.OrcidProfile.LastUpdatedAt;
        researcher.Metrics = metrics;
    }
}
