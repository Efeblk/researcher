namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

public sealed class CanonicalWorkDoiRelation
{
    public int Id { get; set; }
    public string RelationKey { get; set; } = string.Empty;
    public string SourceDoi { get; set; } = string.Empty;
    public string TargetDoi { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}
