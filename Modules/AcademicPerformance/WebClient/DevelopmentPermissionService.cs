using Serenity.Abstractions;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient;

/// <summary>
/// Allows all requests in the standalone development host. Replace this
/// registration with BYS authorization before production deployment.
/// </summary>
public sealed class DevelopmentPermissionService : IPermissionService
{
    public bool HasPermission(string permission)
    {
        return true;
    }
}
