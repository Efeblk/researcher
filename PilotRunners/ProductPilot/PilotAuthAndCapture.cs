using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ProductPilot;

internal sealed class PilotAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Product-Pilot-Auth", out var value) || value != "synthetic-faculty")
            return Task.FromResult(AuthenticateResult.NoResult());
        ClaimsIdentity identity = new([new Claim(ClaimTypes.NameIdentifier, PilotAccessService.ActorId)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

internal sealed class PilotAccessService : IAcademicProductAccessService
{
    public const string ActorId = "synthetic-product-pilot-actor";
    public const string SubjectId = "synthetic-product-pilot-faculty";
    private const string GrantId = "synthetic-product-pilot-grant";

    public Task<AcademicProductAccessGrant> AuthorizeAsync(ClaimsPrincipal principal,
        AcademicProductAccessRequest request, CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            principal.FindFirstValue(ClaimTypes.NameIdentifier) != ActorId)
            throw new AcademicProductUnauthenticatedException();
        if (request.SubjectPersonelId != SubjectId)
            throw new AcademicProductAccessDeniedException();
        return Task.FromResult(new AcademicProductAccessGrant(GrantId, ActorId, SubjectId, request.Operation));
    }

    public Task<AcademicProductAccessGrant> ReauthorizeAsync(AcademicProductAccessGrant persistedGrant,
        CancellationToken cancellationToken)
    {
        if (persistedGrant.AuthorizationGrantId != GrantId || persistedGrant.ActorAuditId != ActorId ||
            persistedGrant.SubjectPersonelId != SubjectId)
            throw new AcademicProductAccessDeniedException();
        return Task.FromResult(persistedGrant);
    }
}

internal sealed class FacultyProviderCapture
{
    private readonly ConcurrentQueue<object> entries = new();
    public void Add(object value) => entries.Enqueue(value);
    public IReadOnlyList<object> Snapshot() => entries.ToArray();
}

internal sealed class CapturingFacultyGenerator(
    GeminiFacultyAssistantGenerator inner,
    FacultyProviderCapture capture) : IFacultyAssistantGenerator
{
    public async Task<GeneratedFacultyAssistantAnswer> GenerateAsync(
        FacultyAssistantAnalysisRequest request, CancellationToken cancellationToken)
    {
        GeneratedFacultyAssistantAnswer generated = await inner.GenerateAsync(request, cancellationToken);
        capture.Add(new { stage = "generation", request.Mode, request.Query, generated.Model,
            generated.PromptVersion, generated.Items });
        return generated;
    }
}

internal sealed class CapturingFacultyVerifier(
    GeminiArticleReviewVerifier inner,
    FacultyProviderCapture capture) : IArticleReviewVerifier
{
    public async Task<GeneratedArticleReviewVerification> VerifyAsync(string role, string language,
        IReadOnlyList<GeneratedArticleReviewFinding> findings, IReadOnlyList<ArticleSourceSpan> sourceSpans,
        CancellationToken cancellationToken)
    {
        GeneratedArticleReviewVerification verification = await inner.VerifyAsync(
            role, language, findings, sourceSpans, cancellationToken);
        capture.Add(new { stage = "verification", role, language, findings, sourceSpans,
            verification.Model, verification.PromptVersion, verification.Verdicts });
        return verification;
    }
}
