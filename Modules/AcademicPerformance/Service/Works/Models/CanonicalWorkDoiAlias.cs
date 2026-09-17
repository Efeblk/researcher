namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

public sealed class CanonicalWorkDoiAlias
{
    public int Id { get; set; }
    public string NormalizedDoi { get; set; } = string.Empty;
    public int CanonicalWorkId { get; set; }
    public CanonicalWork? CanonicalWork { get; set; } = null;
}
