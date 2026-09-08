namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class LocalProviderBudgetDto
{
    public string Status { get; set; } = "Unknown";
    public int MinimumIntervalMilliseconds { get; set; }
    public int? DailyRequestLimit { get; set; } = null;
    public int? RequestsToday { get; set; } = null;
    public int? RemainingToday { get; set; } = null;
    public DateTime? NextAllowedAt { get; set; } = null;
    public DateTime ResetsAt { get; set; }
}

