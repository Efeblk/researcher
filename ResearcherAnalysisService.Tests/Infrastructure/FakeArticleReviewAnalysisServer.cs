using AcademicCollector.Analysis.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ResearcherAnalysisService.Tests.Infrastructure;

public sealed class FakeArticleReviewAnalysisServer(WebApplication application) : IAsyncDisposable
{
    private const string GenerationPromptVersion = "article-specialist-review-v4";
    private const string Model = "gemini-3.8-flash";
    private const string PricingVersion = "gemini-3.8-flash-standard-through-2026-12-31";
    private const string SettingsFingerprint = "synthetic-analysis-settings-v1";
    private const string VerificationPromptVersion = "article-specialist-review-verification-v4";

    public string BaseUrl { get; } = application.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

    public static async Task<FakeArticleReviewAnalysisServer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Urls"] = "http://127.0.0.1:0"
        });
        builder.Logging.ClearProviders();
        WebApplication application = builder.Build();

        application.MapGet("/api/v1/articles/review/configuration", () => Results.Ok(new
            ArticleReviewRuntimeConfiguration(SettingsFingerprint, "Gemini", Model, Model,
                GenerationPromptVersion, VerificationPromptVersion, 8192, 8192, "high", "high",
                PricingVersion)));
        application.MapPost("/api/v1/articles/review/stages/quote",
            (ArticleReviewStageQuoteRequest request) => Results.Ok(new ArticleReviewStageQuote(
                SettingsFingerprint, Fingerprint(request.Stage, request.Role), 0.001m)));
        application.MapPost("/api/v1/articles/review/stages/generate",
            (ArticleReviewStageDispatchRequest request) => Results.Ok(Generate(request)));
        application.MapPost("/api/v1/articles/review/stages/verify",
            (ArticleReviewStageDispatchRequest request) => Results.Ok(Verify(request)));

        await application.StartAsync();
        return new(application);
    }

    public async ValueTask DisposeAsync()
    {
        await application.StopAsync();
        await application.DisposeAsync();
    }

    private static ArticleReviewGenerationStageResult Generate(ArticleReviewStageDispatchRequest request)
    {
        List<ArticleReviewCandidateFinding> findings = [];
        if (request.Role == "method")
        {
            ArticleSourceSpan span = request.Source.SourceSpans!
                .First(value => !string.IsNullOrWhiteSpace(value.Text));
            findings.Add(new("F1", request.Role, "source_observation",
                "The source reports a study method.", null, [span.SourceId]));
        }

        return new(request.Role, findings, Model, GenerationPromptVersion,
            Attempt(request.AttemptId));
    }

    private static ArticleReviewVerificationStageResult Verify(ArticleReviewStageDispatchRequest request) => new(
        request.Findings!.Select(finding => new ArticleReviewVerificationVerdict(
            finding.FindingId, "supported", "Direct support.")).ToList(),
        Model,
        VerificationPromptVersion,
        Attempt(request.AttemptId));

    private static ArticleReviewProviderAttempt Attempt(Guid attemptId) => new(
        attemptId, "Success", Model, 0.001m, PricingVersion);

    private static string Fingerprint(string stage, string role) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{stage}:{role}")));
}
