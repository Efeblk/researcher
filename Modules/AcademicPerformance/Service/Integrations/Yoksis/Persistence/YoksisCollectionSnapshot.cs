using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;

public sealed class YoksisCollectionSnapshot
{
    public string PersonelId { get; set; } = string.Empty;
    public string TcKimlikNoHash { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
    public string ResponseJson { get; set; } = string.Empty;

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;
}
