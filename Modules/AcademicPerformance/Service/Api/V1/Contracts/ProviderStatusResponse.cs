using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderStatusResponse : ServiceResponse
{
    public DateTime CheckedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public List<ProviderStatusDto> Providers { get; set; } = [];
}
