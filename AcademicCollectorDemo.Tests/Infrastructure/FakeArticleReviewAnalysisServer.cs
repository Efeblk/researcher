using AcademicCollector.Analysis.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Integrations.Gemini;

namespace AcademicCollectorDemo.Tests.Infrastructure;

public sealed class FakeArticleReviewAnalysisServer(WebApplication application) : IAsyncDisposable
{
    public string BaseUrl { get; } = application.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

    public static async Task<FakeArticleReviewAnalysisServer> StartAsync()
    {
        WebApplication application = ResearcherAnalysisService.Program.CreateApplication(
            ["--environment", "Development"], builder =>
            {
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Urls"] = "http://127.0.0.1:0"
                });
                builder.Logging.ClearProviders();
                builder.Services.RemoveAll<IArticleReviewGenerator>();
                builder.Services.RemoveAll<IArticleReviewVerifier>();
                builder.Services.AddScoped<IArticleReviewGenerator, Generator>();
                builder.Services.AddScoped<IArticleReviewVerifier, Verifier>();
            });
        await application.StartAsync();
        return new(application);
    }

    public async ValueTask DisposeAsync()
    {
        await application.StopAsync();
        await application.DisposeAsync();
    }

    private sealed class Generator(ArticleReviewDispatchContext context) : IArticleReviewGenerator
    {
        public Task<GeneratedArticleReviewPass> GenerateAsync(string role, string language, string sourceKind,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
        {
            context.Capture(Success());
            if (role != "method")
                return Task.FromResult(new GeneratedArticleReviewPass(role, [], "gemini-3.8-flash", ArticleReviewPrompt.Version));
            ArticleSourceSpan span = sourceSpans.First(value => !string.IsNullOrWhiteSpace(value.Text));
            return Task.FromResult(new GeneratedArticleReviewPass(role,
                [new("F1", role, "source_observation", "The source reports a study method.", null, [span.SourceId])],
                "gemini-3.8-flash", ArticleReviewPrompt.Version));
        }
    }

    private sealed class Verifier(ArticleReviewDispatchContext context) : IArticleReviewVerifier
    {
        public Task<GeneratedArticleReviewVerification> VerifyAsync(string role, string language,
            IReadOnlyList<GeneratedArticleReviewFinding> findings, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            context.Capture(Success());
            return Task.FromResult(new GeneratedArticleReviewVerification(
                findings.Select(finding => new GeneratedArticleReviewVerdict(
                    finding.FindingId, "supported", "Direct support.")).ToList(),
                "gemini-3.8-flash", ArticleReviewVerificationPrompt.Version));
        }
    }

    private static GeminiUsageCompletion Success() => new()
    {
        Outcome = "Success", ReturnedModel = "gemini-3.8-flash", EstimatedUsd = 0.001m,
        PricingVersion = "gemini-3.8-flash-standard-through-2026-12-31"
    };
}
