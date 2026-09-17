using Serenity.Abstractions;

namespace AcademicCollectorDemo.Host;

/// <summary>
/// Allows all requests in the standalone development host. Replace this
/// registration with trusted institutional authorization before production deployment.
/// </summary>
public sealed class DevelopmentPermissionService : IPermissionService
{
    public bool HasPermission(string permission)
    {
        return true;
    }
}
