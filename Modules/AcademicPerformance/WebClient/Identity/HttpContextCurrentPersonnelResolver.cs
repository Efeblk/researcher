using System.Security.Claims;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Identity;

public sealed class HttpContextCurrentPersonnelResolver : ICurrentPersonnelResolver
{
    public const string PersonelIdClaimType = "PersonelID";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentPersonnelResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetPersonelId()
    {
        ClaimsPrincipal? user = _httpContextAccessor.HttpContext?.User;
        ClaimsIdentity[] authenticatedIdentities = user?.Identities
            .Where(identity => identity.IsAuthenticated)
            .ToArray() ?? [];
        if (authenticatedIdentities.Length == 0)
            throw new UnauthorizedAccessException("Oturum açmış bir kullanıcı gereklidir.");

        string[] claimValues = authenticatedIdentities
            .SelectMany(identity => identity.FindAll(PersonelIdClaimType))
            .Select(claim => claim.Value.Trim())
            .ToArray();
        if (claimValues.Length == 0 || claimValues.Any(value => value.Length == 0))
            throw new UnauthorizedAccessException("Oturum kullanıcısında geçerli PersonelID bilgisi bulunamadı.");

        string[] values = claimValues
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length > 1)
            throw new UnauthorizedAccessException("Oturum kullanıcısında birden fazla PersonelID bilgisi bulundu.");
        if (values[0].Length > 200)
            throw new UnauthorizedAccessException("Oturum kullanıcısındaki PersonelID bilgisi geçersiz.");

        return values[0];
    }
}
