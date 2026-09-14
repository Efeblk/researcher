namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

public sealed record CanonicalWorkSyncResult(
    int CanonicalWorkCount,
    int ObservationCount,
    int AssociationCount);
