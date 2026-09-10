using System.Security.Claims;
using AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Identity;
using Microsoft.AspNetCore.Http;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class HttpContextCurrentPersonnelResolverTests
{
    [Fact]
    public void GetPersonelId_AuthenticatedClaim_ReturnsTrimmedValue()
    {
        HttpContextCurrentPersonnelResolver resolver = CreateResolver(
            Identity(true, (HttpContextCurrentPersonnelResolver.PersonelIdClaimType, " 00123-A ")));

        Assert.Equal("00123-A", resolver.GetPersonelId());
    }

    [Fact]
    public void GetPersonelId_UnauthenticatedClaim_RejectsRequest()
    {
        HttpContextCurrentPersonnelResolver resolver = CreateResolver(
            Identity(false, (HttpContextCurrentPersonnelResolver.PersonelIdClaimType, "spoofed")));

        Assert.Throws<UnauthorizedAccessException>(() => resolver.GetPersonelId());
    }

    [Fact]
    public void GetPersonelId_UnauthenticatedSecondaryIdentity_IgnoresItsClaim()
    {
        HttpContextCurrentPersonnelResolver resolver = CreateResolver(
            Identity(true, (HttpContextCurrentPersonnelResolver.PersonelIdClaimType, "current")),
            Identity(false, (HttpContextCurrentPersonnelResolver.PersonelIdClaimType, "spoofed")));

        Assert.Equal("current", resolver.GetPersonelId());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetPersonelId_MissingOrEmptyClaim_RejectsRequest(string? value)
    {
        ClaimsIdentity identity = value is null
            ? Identity(true)
            : Identity(true, (HttpContextCurrentPersonnelResolver.PersonelIdClaimType, value));

        Assert.Throws<UnauthorizedAccessException>(() =>
            CreateResolver(identity).GetPersonelId());
    }

    [Fact]
    public void GetPersonelId_ConflictingAuthenticatedClaims_RejectsRequest()
    {
        HttpContextCurrentPersonnelResolver resolver = CreateResolver(
            Identity(true, (HttpContextCurrentPersonnelResolver.PersonelIdClaimType, "one")),
            Identity(true, (HttpContextCurrentPersonnelResolver.PersonelIdClaimType, "two")));

        Assert.Throws<UnauthorizedAccessException>(() => resolver.GetPersonelId());
    }

    [Fact]
    public void GetPersonelId_OverlongClaim_RejectsRequest()
    {
        HttpContextCurrentPersonnelResolver resolver = CreateResolver(
            Identity(true, (HttpContextCurrentPersonnelResolver.PersonelIdClaimType, new string('x', 201))));

        Assert.Throws<UnauthorizedAccessException>(() => resolver.GetPersonelId());
    }

    private static ClaimsIdentity Identity(bool authenticated, params (string Type, string Value)[] claims)
    {
        return new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)),
            authenticated ? "Test" : null);
    }

    private static HttpContextCurrentPersonnelResolver CreateResolver(params ClaimsIdentity[] identities)
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(identities);
        return new HttpContextCurrentPersonnelResolver(new HttpContextAccessor { HttpContext = context });
    }
}
