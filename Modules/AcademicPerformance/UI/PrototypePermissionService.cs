using Serenity.Abstractions;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.UI;

/// <summary>
/// Allows access in the standalone prototype. Replace this registration with
/// BYS authorization services when the module is moved into production.
/// </summary>
public sealed class PrototypePermissionService : IPermissionService
{
    public bool HasPermission(string permission)
    {
        return true;
    }
}
