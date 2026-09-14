using System.Security.Cryptography;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AcademicCollectorDemo.Tests.Infrastructure;

public sealed class FakeArticleEvaluationServer(WebApplication application) : IAsyncDisposable
{
    private int _executeCalls;
    public int ExecuteCalls => Volatile.Read(ref _executeCalls);
    public string BaseUrl { get; private set; } = string.Empty;

    public static async Task<FakeArticleEvaluationServer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        WebApplication app = builder.Build();
        FakeArticleEvaluationServer server = new(app);
        app.MapGet("/api/v1/evaluations/profiles", () => new ArticleEvaluationProfilesResponse([Profile()]));
        app.MapPost("/api/v1/evaluations/execute", (ArticleEvaluationRequest request) => server.Execute(request));
        await app.StartAsync();
        server.BaseUrl = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await application.StopAsync();
        await application.DisposeAsync();
    }

    private AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse Execute(ArticleEvaluationRequest request)
    {
        Interlocked.Increment(ref _executeCalls);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string sourceIdentity = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            request.Source, new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant();
        ArticleEvaluationAttemptTelemetry attempt = new(1, "calibration", null, "fake", "configured-alias",
            "actual-synthetic-model-revision", "completed", now, now.AddMilliseconds(2), 2,
            20, 5, null, null, null, 0.000001m, "test-price-v1", null);
        return new(request.ProfileId, request.TaskKind, "fake", "configured-alias",
            ["actual-synthetic-model-revision"], Fingerprint, "fake-settings-v1", sourceIdentity,
            ArticleEvaluationOutcomes.Completed, null, new(2, 1, [attempt]))
        {
            Verdicts = request.CalibrationClaims?.Select(value =>
                new ArticleEvaluationVerdict(value.ClaimId, "uncertain", "Synthetic fake verdict.")).ToList(),
            ExecutionSettings = Settings
        };
    }

    private static ArticleEvaluationProfile Profile() => new("fake-profile", "Fake profile", "fake",
        "configured-alias", "test-revision", Fingerprint, "fake-settings-v1",
        [ArticleEvaluationTaskKinds.Calibration, ArticleEvaluationTaskKinds.Review,
            ArticleEvaluationTaskKinds.CrossCheck], "configured", null, true)
    {
        ExecutionSettings = Settings
    };

    private static string Fingerprint => new('a', 64);
    private static IReadOnlyDictionary<string, string> Settings =>
        new Dictionary<string, string>
        {
            ["promptVersion"] = "fake-v1",
            ["maximumInputBytes"] = "100000"
        };
}
