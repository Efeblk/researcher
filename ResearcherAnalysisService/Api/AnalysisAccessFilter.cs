using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ResearcherAnalysisService.Api;

public sealed class AnalysisAccessFilter(IConfiguration configuration, IHostEnvironment environment)
    : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        string? key = configuration["Service:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            IPAddress? address = context.HttpContext.Connection.RemoteIpAddress;
            if (environment.IsDevelopment() && address is not null && IPAddress.IsLoopback(address))
                return;

            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = 503,
                Title = "Configure Service:ApiKey before accepting remote requests."
            }) { StatusCode = 503 };
            return;
        }

        string supplied = context.HttpContext.Request.Headers["X-Analysis-Key"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            context.Result = new UnauthorizedResult();
    }
}
