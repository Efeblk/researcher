using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Identity;

internal static class CurrentPersonnelHttpResult
{
    public static bool TryResolve(
        ICurrentPersonnelResolver currentPersonnel,
        out string personelId,
        out ActionResult? error)
    {
        try
        {
            personelId = currentPersonnel.GetPersonelId();
            error = null;
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            personelId = string.Empty;
            error = new UnauthorizedObjectResult(new ServiceResponse
            {
                Error = new ServiceError
                {
                    Code = "Authentication",
                    Message = exception.Message
                }
            });
            return false;
        }
    }
}
