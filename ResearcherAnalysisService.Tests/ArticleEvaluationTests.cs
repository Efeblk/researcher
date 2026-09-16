using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.DeepSeek;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class ArticleEvaluationTests
{
    [Fact]
    public void AttemptRecorder_OpenAttemptReservesBudgetAndUniqueOrdinal()
    {
        ArticleEvaluationAttemptRecorder recorder = new(1);
        using IDisposable context = recorder.Enter("calibration_verify", null);
        using ArticleEvaluationAttemptRecorder.Attempt first = recorder.Begin("Synthetic", "model");

        Assert.Throws<InvalidOperationException>(() => recorder.Begin("Synthetic", "model"));
        first.Complete("completed", null);
        Assert.Equal(1, recorder.Snapshot().Single().AttemptNumber);
    }

    [Fact]
    public void AttemptRecorder_LatestAlreadyFailed_DoesNotRewriteEarlierCompletedAttempt()
    {
        ArticleEvaluationAttemptRecorder recorder = new(2);
        using IDisposable context = recorder.Enter("article_review", "method");
        using ArticleEvaluationAttemptRecorder.Attempt generation = recorder.Begin("Synthetic", "model");
        generation.Complete("completed", null);
        using ArticleEvaluationAttemptRecorder.Attempt verification = recorder.Begin("Synthetic", "model");
        verification.Complete("failed", "invalid_provider_response");

        recorder.MarkLatestCompletedAttemptFailed("invalid_provider_response");

        IReadOnlyList<ArticleEvaluationAttemptTelemetry> attempts = recorder.Snapshot();
        Assert.Equal("completed", attempts[0].Status);
        Assert.Null(attempts[0].ErrorCode);
        Assert.Equal("failed", attempts[1].Status);
        Assert.Equal("invalid_provider_response", attempts[1].ErrorCode);
    }

    [Fact]
    public void AttemptRecorder_LatestCompleted_ChangesOnlyLatestToFailed()
    {
        ArticleEvaluationAttemptRecorder recorder = new(2);
        using IDisposable context = recorder.Enter("article_review", "method");
        using ArticleEvaluationAttemptRecorder.Attempt first = recorder.Begin("Synthetic", "model");
        first.Complete("completed", null);
        using ArticleEvaluationAttemptRecorder.Attempt latest = recorder.Begin("Synthetic", "model");
        latest.Complete("completed", null);

        recorder.MarkLatestCompletedAttemptFailed("invalid_provider_response");

        IReadOnlyList<ArticleEvaluationAttemptTelemetry> attempts = recorder.Snapshot();
        Assert.Equal("completed", attempts[0].Status);
        Assert.Equal("failed", attempts[1].Status);
        Assert.Equal("invalid_provider_response", attempts[1].ErrorCode);
    }

    [Fact]
    public void AttemptRecorder_LatestInFlight_DoesNotRewriteEarlierCompletedAttempt()
    {
        ArticleEvaluationAttemptRecorder recorder = new(2);
        using IDisposable context = recorder.Enter("article_review", "method");
        using ArticleEvaluationAttemptRecorder.Attempt first = recorder.Begin("Synthetic", "model");
        first.Complete("completed", null);
        using ArticleEvaluationAttemptRecorder.Attempt latest = recorder.Begin("Synthetic", "model");

        recorder.MarkLatestCompletedAttemptFailed("invalid_provider_response");

        ArticleEvaluationAttemptTelemetry attempt = Assert.Single(recorder.Snapshot());
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal("completed", attempt.Status);
    }

    [Fact]
    public void AttemptRecorder_OutOfOrderCompletion_ChangesHighestAttemptNumber()
    {
        ArticleEvaluationAttemptRecorder recorder = new(2);
        using IDisposable context = recorder.Enter("article_review", "method");
        using ArticleEvaluationAttemptRecorder.Attempt first = recorder.Begin("Synthetic", "model");
        using ArticleEvaluationAttemptRecorder.Attempt latest = recorder.Begin("Synthetic", "model");
        latest.Complete("completed", null);
        first.Complete("completed", null);

        recorder.MarkLatestCompletedAttemptFailed("invalid_provider_response");

        IReadOnlyList<ArticleEvaluationAttemptTelemetry> attempts = recorder.Snapshot();
        Assert.Equal("completed", attempts[0].Status);
        Assert.Equal("failed", attempts[1].Status);
        Assert.Equal("invalid_provider_response", attempts[1].ErrorCode);
    }

    [Fact]
    public void AttemptRecorder_LatestCancelled_ChangesOnlyLatestToTimedOut()
    {
        ArticleEvaluationAttemptRecorder recorder = new(2);
        using IDisposable context = recorder.Enter("article_review", "method");
        using ArticleEvaluationAttemptRecorder.Attempt first = recorder.Begin("Synthetic", "model");
        first.Complete("completed", null);
        using ArticleEvaluationAttemptRecorder.Attempt latest = recorder.Begin("Synthetic", "model");
        latest.Complete("cancelled", "cancelled");

        recorder.MarkLatestCancelledAttemptTimedOut();

        IReadOnlyList<ArticleEvaluationAttemptTelemetry> attempts = recorder.Snapshot();
        Assert.Equal("completed", attempts[0].Status);
        Assert.Equal("timed_out", attempts[1].Status);
        Assert.Equal("timeout", attempts[1].ErrorCode);
    }

    [Fact]
    public async Task Profiles_ReturnFixedSafeMetadataAndNoSecretOrUrl()
    {
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "synthetic-secret"
        });

        ArticleEvaluationProfilesResponse profiles = (await host.Client.GetFromJsonAsync<ArticleEvaluationProfilesResponse>(
            "api/v1/evaluations/profiles"))!;

        Assert.Equal(["gemini-baseline", "ollama-qwen-baseline", "deepseek-candidate"],
            profiles.Profiles.Select(value => value.ProfileId));
        ArticleEvaluationProfile qwen = profiles.Profiles.Single(value => value.ProfileId == "ollama-qwen-baseline");
        Assert.Equal("qwen3.8:27b-q4_K_M", qwen.RequestedModel);
        Assert.Equal(64, qwen.ModelRevision!.Length);
        Assert.Equal("32768", qwen.ExecutionSettings!["contextTokens"]);
        Assert.Equal("4096", qwen.ExecutionSettings["verifierMaxOutputTokens"]);
        string serialized = JsonSerializer.Serialize(profiles);
        Assert.DoesNotContain("synthetic-secret", serialized);
        Assert.DoesNotContain("11434", serialized);
        Assert.Equal("not_configured", profiles.Profiles.Single(value =>
            value.ProfileId == "deepseek-candidate").Availability);
    }

    [Theory]
    [InlineData("unknown-profile", "calibration", "unknown_profile", HttpStatusCode.BadRequest)]
    [InlineData("gemini-baseline", "unknown-task", "unknown_task", HttpStatusCode.BadRequest)]
    public async Task Execute_UnknownProfileOrTask_RejectsBeforeProviderCall(string profileId,
        string taskKind, string expectedErrorCode, HttpStatusCode expectedStatus)
    {
        int calls = 0;
        StubHandler handler = new((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler,
            settings: new Dictionary<string, string?> { ["Gemini:ApiKey"] = "synthetic-key" });
        ArticleEvaluationRequest request = new(profileId, taskKind, "irrelevant", Source());

        ArticleEvaluationRequestException exception = await Assert.ThrowsAsync<ArticleEvaluationRequestException>(
            () => ExecuteAsync(host, request));
        Assert.Equal((int)expectedStatus, exception.StatusCode);
        Assert.Equal(expectedErrorCode, exception.ErrorCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Execute_StaleFingerprint_ReturnsConflictBeforeProviderCall()
    {
        int calls = 0;
        StubHandler handler = new((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler,
            settings: new Dictionary<string, string?> { ["Gemini:ApiKey"] = "synthetic-key" });
        ArticleEvaluationRequest request = CalibrationRequest("gemini-baseline", "stale");

        ArticleEvaluationRequestException exception = await Assert.ThrowsAsync<ArticleEvaluationRequestException>(
            () => ExecuteAsync(host, request));
        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Execute_GeminiCalibration_ReturnsAllVerdictsAndNormalizedTelemetry()
    {
        StubHandler handler = new((request, _) =>
        {
            Assert.Equal("generativelanguage.googleapis.com", request.RequestUri!.Host);
            return Task.FromResult(GeminiResponse("""
                {"verdicts":[{"claimId":"claim-1","verdict":"supported","reason":"Direct support."}]}
                """, prompt: 100, candidate: 20, cached: 10, thinking: 5));
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler,
            settings: new Dictionary<string, string?> { ["Gemini:ApiKey"] = "synthetic-key" });
        ArticleEvaluationProfile profile = await Profile(host, "gemini-baseline");

        ArticleEvaluationResponse result = await ExecuteAsync(host,
            CalibrationRequest(profile.ProfileId, profile.SettingsFingerprint));
        Assert.Equal("completed", result.Outcome);
        Assert.Equal("supported", result.Verdicts!.Single().Verdict);
        ArticleEvaluationAttemptTelemetry attempt = Assert.Single(result.Telemetry.Attempts);
        Assert.Equal(100, attempt.InputTokens);
        Assert.Equal(25, attempt.OutputTokens);
        Assert.Equal(10, attempt.CacheReadTokens);
        Assert.Equal(5, attempt.ThinkingTokens);
        Assert.NotNull(attempt.EstimatedCostUsd);
        Assert.True(attempt.CompletedAtUtc >= attempt.StartedAtUtc);
    }

    [Fact]
    public async Task Execute_GeminiInconsistentCacheUsage_FailsClosedWithUnattributedTelemetry()
    {
        StubHandler handler = new((_, _) => Task.FromResult(GeminiResponse("""
            {"verdicts":[{"claimId":"claim-1","verdict":"supported","reason":"Direct support."}]}
            """, prompt: 100, candidate: 20, cached: 101, thinking: 5)));
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler,
            settings: new Dictionary<string, string?> { ["Gemini:ApiKey"] = "synthetic-key" });
        ArticleEvaluationProfile profile = await Profile(host, "gemini-baseline");

        ArticleEvaluationResponse result = await ExecuteAsync(host,
            CalibrationRequest(profile.ProfileId, profile.SettingsFingerprint));

        Assert.Equal("failed", result.Outcome);
        Assert.Null(result.Verdicts);
        ArticleEvaluationAttemptTelemetry attempt = Assert.Single(result.Telemetry.Attempts);
        Assert.Equal("gemini-3.8-flash", attempt.ReturnedModel);
        Assert.Null(attempt.InputTokens);
        Assert.Null(attempt.OutputTokens);
        Assert.Null(attempt.CacheReadTokens);
        Assert.Null(attempt.ThinkingTokens);
        Assert.Null(attempt.EstimatedCostUsd);
        Assert.Null(attempt.PricingVersion);
    }

    [Fact]
    public async Task Execute_OllamaCrossCheck_GroupsRolesAndDoesNotSendProfileIdentity()
    {
        int modelCalls = 0;
        StubHandler handler = new(async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
                return OllamaTags(ArticleEvaluationProfileCatalog.OllamaModelDigest);
            if (request.RequestUri!.AbsolutePath == "/api/version")
                return JsonResponse(new { version = "0.34.0" });
            modelCalls++;
            string body = await request.Content!.ReadAsStringAsync();
            using JsonDocument outer = JsonDocument.Parse(body);
            string input = outer.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            Assert.DoesNotContain("profileId", input, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("requestedModel", input, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("generator", input, StringComparison.OrdinalIgnoreCase);
            using JsonDocument inner = JsonDocument.Parse(input);
            string[] ids = inner.RootElement.GetProperty("items").EnumerateArray()
                .Select(value => value.GetProperty("finding").GetProperty("findingId").GetString()!).ToArray();
            string content = JsonSerializer.Serialize(new
            {
                verdicts = ids.Select(id => new { findingId = id, verdict = "supported", reason = "Supported." })
            });
            return OllamaResponse(content);
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler);
        ArticleEvaluationProfile profile = await Profile(host, "ollama-qwen-baseline");
        ReviewArticleRequest source = Source();
        ArticleReviewEvidence evidence = Evidence(source);
        ArticleEvaluationRequest request = new(profile.ProfileId, ArticleEvaluationTaskKinds.CrossCheck,
            profile.SettingsFingerprint, source)
        {
            Findings =
            [
                new("finding-1", "method", "source_observation", "Basis one.", null, [evidence]),
                new("finding-2", "quantitative", "source_observation", "Basis two.", null, [evidence])
            ]
        };

        ArticleEvaluationResponse result = await ExecuteAsync(host, request);

        Assert.Equal("completed", result.Outcome);
        Assert.Equal(2, result.Verdicts!.Count);
        Assert.Equal(2, modelCalls);
        Assert.Equal(2, result.Telemetry.AttemptCount);
        Assert.All(result.Telemetry.Attempts, value => Assert.Null(value.EstimatedCostUsd));
        Assert.Equal(["method", "quantitative"], result.Telemetry.Attempts.Select(value => value.Role));
    }

    [Fact]
    public async Task Execute_OllamaRevisionDrift_ReturnsConflictBeforeGeneration()
    {
        int generationCalls = 0;
        StubHandler handler = new((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
                return Task.FromResult(OllamaTags(new string('0', 64)));
            generationCalls++;
            return Task.FromResult(OllamaResponse("{}"));
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler);
        ArticleEvaluationProfile profile = await Profile(host, "ollama-qwen-baseline");

        ArticleEvaluationRequestException exception = await Assert.ThrowsAsync<ArticleEvaluationRequestException>(
            () => ExecuteAsync(host, CalibrationRequest(profile.ProfileId, profile.SettingsFingerprint)));
        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("model_revision_mismatch", exception.ErrorCode);
        Assert.Equal(0, generationCalls);
    }

    [Fact]
    public async Task Execute_GeminiReview_UsesFourRolesAndAtMostEightCalls()
    {
        ReviewArticleRequest source = Source();
        string sourceId = source.SourceSpans!.Single().SourceId;
        int calls = 0;
        StubHandler handler = new(async (request, _) =>
        {
            calls++;
            string body = await request.Content!.ReadAsStringAsync();
            using JsonDocument outer = JsonDocument.Parse(body);
            JsonElement schema = outer.RootElement.GetProperty("generationConfig").GetProperty("responseJsonSchema");
            string input = outer.RootElement.GetProperty("contents")[0].GetProperty("parts")[0]
                .GetProperty("text").GetString()!;
            using JsonDocument inner = JsonDocument.Parse(input);
            string role = inner.RootElement.GetProperty("role").GetString()!;
            bool verification = schema.GetProperty("properties").TryGetProperty("verdicts", out JsonElement _);
            string answer = verification
                ? "{\"verdicts\":[{\"findingId\":\"F1\",\"verdict\":\"supported\",\"reason\":\"Supported.\"}]}"
                : JsonSerializer.Serialize(new
                {
                    role,
                    findings = new[]
                    {
                        new
                        {
                            findingId = "F1", role,
                            kind = role == "teaching" ? "teaching_adaptation" : "source_observation",
                            basis = "Grounded basis.",
                            suggestion = role == "teaching" ? "Use a classroom prompt." : null,
                            sourceIds = new[] { sourceId }
                        }
                    }
                });
            return GeminiResponse(answer);
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler,
            settings: new Dictionary<string, string?> { ["Gemini:ApiKey"] = "synthetic-key" });
        ArticleEvaluationProfile profile = await Profile(host, "gemini-baseline");
        ArticleEvaluationRequest request = new(profile.ProfileId, ArticleEvaluationTaskKinds.Review,
            profile.SettingsFingerprint, source);

        ArticleEvaluationResponse result = await ExecuteAsync(host, request);

        Assert.Equal("completed", result.Outcome);
        Assert.Equal(8, calls);
        Assert.Equal(8, result.Telemetry.AttemptCount);
        Assert.Equal(4, result.ReviewReport!.Reviews.Count);
    }

    [Fact]
    public async Task Execute_MalformedVerdict_FailsAndMarksAttemptFailed()
    {
        StubHandler handler = new((_, _) => Task.FromResult(GeminiResponse(
            "{\"verdicts\":[{\"claimId\":\"wrong\",\"verdict\":\"supported\",\"reason\":\"No.\"}]}")));
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler,
            settings: new Dictionary<string, string?> { ["Gemini:ApiKey"] = "synthetic-key" });
        ArticleEvaluationProfile profile = await Profile(host, "gemini-baseline");

        ArticleEvaluationResponse result = await ExecuteAsync(host,
            CalibrationRequest(profile.ProfileId, profile.SettingsFingerprint));

        Assert.Equal("failed", result.Outcome);
        Assert.Equal("invalid_provider_response", result.ErrorCode);
        Assert.Equal("invalid_evidence", result.Failure!.Reason);
        Assert.Equal("calibration_verify", result.Failure.Stage);
        Assert.Null(result.Failure.Role);
        ArticleEvaluationAttemptTelemetry attempt = Assert.Single(result.Telemetry.Attempts);
        Assert.Equal("failed", attempt.Status);
        Assert.Equal("invalid_evidence", attempt.ErrorCode);
    }

    [Fact]
    public async Task Execute_GeminiReviewOutputLimit_PreservesStageRoleAndSpecificAttemptReason()
    {
        StubHandler handler = new((_, _) => Task.FromResult(GeminiResponse("{}", finishReason: "MAX_TOKENS")));
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler,
            settings: new Dictionary<string, string?> { ["Gemini:ApiKey"] = "synthetic-key" });
        ArticleEvaluationProfile profile = await Profile(host, "gemini-baseline");
        ArticleEvaluationRequest request = new(profile.ProfileId, ArticleEvaluationTaskKinds.Review,
            profile.SettingsFingerprint, Source());

        ArticleEvaluationResponse result = await ExecuteAsync(host, request);

        Assert.Equal("failed", result.Outcome);
        Assert.Equal("invalid_provider_response", result.ErrorCode);
        Assert.Equal("output_limit", result.Failure!.Reason);
        Assert.Equal("generation", result.Failure.Stage);
        Assert.Equal("method", result.Failure.Role);
        ArticleEvaluationAttemptTelemetry attempt = Assert.Single(result.Telemetry.Attempts);
        Assert.Equal("output_limit", attempt.ErrorCode);
        Assert.Equal("review_generate", attempt.Stage);
        Assert.Equal("method", attempt.Role);
    }

    [Fact]
    public async Task Execute_ProviderFailure_PreservesUnknownCostAttempt()
    {
        StubHandler handler = new((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler,
            settings: new Dictionary<string, string?> { ["Gemini:ApiKey"] = "synthetic-key" });
        ArticleEvaluationProfile profile = await Profile(host, "gemini-baseline");

        ArticleEvaluationResponse result = await ExecuteAsync(host,
            CalibrationRequest(profile.ProfileId, profile.SettingsFingerprint));

        Assert.Equal("failed", result.Outcome);
        ArticleEvaluationAttemptTelemetry attempt = Assert.Single(result.Telemetry.Attempts);
        Assert.Equal("failed", attempt.Status);
        Assert.Null(attempt.EstimatedCostUsd);
        Assert.Null(attempt.InputTokens);
        Assert.Null(attempt.OutputTokens);
    }

    [Fact]
    public async Task DeepSeekClient_UsesFixedEndpointHeadersJsonModeAndParsesUsageWithoutGuessingCost()
    {
        StubHandler handler = new(async (request, _) =>
        {
            Assert.Equal(new Uri("https://api.deepseek.com/chat/completions"), request.RequestUri);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "synthetic-key"), request.Headers.Authorization);
            string body = await request.Content!.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(body);
            Assert.Equal("deepseek-flash", document.RootElement.GetProperty("model").GetString());
            Assert.Equal("json_object", document.RootElement.GetProperty("response_format").GetProperty("type").GetString());
            Assert.Equal("enabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
            Assert.Equal("high", document.RootElement.GetProperty("reasoning_effort").GetString());
            Assert.False(document.RootElement.GetProperty("thinking").TryGetProperty(
                "reasoning_effort", out JsonElement _));
            return JsonResponse(new
            {
                choices = new[] { new { finish_reason = "stop", message = new { content = "{}" } } },
                model = "deepseek-v4.1-flash-20260910",
                system_fingerprint = "synthetic-revision",
                usage = new
                {
                    prompt_tokens = 100,
                    completion_tokens = 25,
                    prompt_cache_hit_tokens = 40,
                    prompt_cache_miss_tokens = 60,
                    total_tokens = 125,
                    completion_tokens_details = new { reasoning_tokens = 5 }
                }
            });
        });
        ArticleEvaluationAttemptRecorder recorder = new(1);
        DeepSeekArticleClient client = new(new HttpClient(handler), Options.Create(new ArticleEvaluationOptions
        {
            DeepSeek = new() { ApiKey = "synthetic-key" }
        }), recorder);

        DeepSeekArticleResult result;
        using (recorder.Enter("calibration_verify", null))
            result = await client.GenerateAsync("Return JSON.", "{}", 512, default);

        Assert.Equal("deepseek-v4.1-flash-20260910", result.Model);
        ArticleEvaluationAttemptTelemetry attempt = Assert.Single(recorder.Snapshot());
        Assert.Equal(100, attempt.InputTokens);
        Assert.Equal(25, attempt.OutputTokens);
        Assert.Equal(40, attempt.CacheReadTokens);
        Assert.Equal(5, attempt.ThinkingTokens);
        Assert.Null(attempt.EstimatedCostUsd);
        Assert.Null(attempt.PricingVersion);
    }

    [Fact]
    public async Task DeepSeekClient_LengthFinishFailsWithCapturedUsage()
    {
        StubHandler handler = new((_, _) => Task.FromResult(JsonResponse(new
        {
            choices = new[] { new { finish_reason = "length", message = new { content = "{}" } } },
            model = "deepseek-flash",
            usage = new
            {
                prompt_tokens = 10, completion_tokens = 5, prompt_cache_hit_tokens = 0,
                prompt_cache_miss_tokens = 10, total_tokens = 15
            }
        })));
        ArticleEvaluationAttemptRecorder recorder = new(1);
        DeepSeekArticleClient client = new(new HttpClient(handler), Options.Create(new ArticleEvaluationOptions
        {
            DeepSeek = new() { ApiKey = "synthetic-key" }
        }), recorder);

        using (recorder.Enter("calibration_verify", null))
            await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
                client.GenerateAsync("Return JSON.", "{}", 512, default));

        ArticleEvaluationAttemptTelemetry attempt = Assert.Single(recorder.Snapshot());
        Assert.Equal("failed", attempt.Status);
        Assert.Equal(10, attempt.InputTokens);
        Assert.Equal(5, attempt.OutputTokens);
    }

    [Fact]
    public async Task DeepSeekClient_CancellationRecordsAttemptWithoutUsageOrCost()
    {
        StubHandler handler = new(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return JsonResponse(new { });
        });
        ArticleEvaluationAttemptRecorder recorder = new(1);
        DeepSeekArticleClient client = new(new HttpClient(handler), Options.Create(new ArticleEvaluationOptions
        {
            DeepSeek = new() { ApiKey = "synthetic-key" }
        }), recorder);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(25));

        using (recorder.Enter("calibration_verify", null))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.GenerateAsync("Return JSON.", "{}", 512, cancellation.Token));

        ArticleEvaluationAttemptTelemetry attempt = Assert.Single(recorder.Snapshot());
        Assert.Equal("cancelled", attempt.Status);
        Assert.Equal("cancelled", attempt.ErrorCode);
        Assert.Null(attempt.InputTokens);
        Assert.Null(attempt.EstimatedCostUsd);
    }

    [Fact]
    public async Task Execute_InvalidEvidenceRejectsBeforeProviderCall()
    {
        int calls = 0;
        StubHandler handler = new((_, _) =>
        {
            calls++;
            return Task.FromResult(GeminiResponse("{}"));
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler,
            settings: new Dictionary<string, string?> { ["Gemini:ApiKey"] = "synthetic-key" });
        ArticleEvaluationProfile profile = await Profile(host, "gemini-baseline");
        ReviewArticleRequest source = Source();
        ArticleEvaluationRequest request = new(profile.ProfileId, ArticleEvaluationTaskKinds.CrossCheck,
            profile.SettingsFingerprint, source)
        {
            Findings = [new("finding-1", "method", "source_observation", "Basis.", null,
                [Evidence(source) with { Quote = "fabricated quote" }])]
        };

        await Assert.ThrowsAsync<ArticleEvaluationRequestException>(() => ExecuteAsync(host, request));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Execute_SourceHashMismatchRejectsBeforeProviderCall()
    {
        int calls = 0;
        StubHandler handler = new((_, _) =>
        {
            calls++;
            return Task.FromResult(GeminiResponse("{}"));
        });
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(evaluationHandler: handler,
            settings: new Dictionary<string, string?> { ["Gemini:ApiKey"] = "synthetic-key" });
        ArticleEvaluationProfile profile = await Profile(host, "gemini-baseline");
        ArticleEvaluationRequest valid = CalibrationRequest(profile.ProfileId, profile.SettingsFingerprint);
        ArticleEvaluationRequest request = valid with { Source = valid.Source with { SourceHash = new string('0', 64) } };

        ArticleEvaluationRequestException exception = await Assert.ThrowsAsync<ArticleEvaluationRequestException>(
            () => ExecuteAsync(host, request));
        Assert.Equal("source_hash_mismatch", exception.ErrorCode);
        Assert.Equal(0, calls);
    }

    private static ArticleEvaluationRequest CalibrationRequest(string profileId, string fingerprint)
    {
        ReviewArticleRequest source = Source();
        return new(profileId, ArticleEvaluationTaskKinds.Calibration, fingerprint, source)
        {
            CalibrationClaims = [new("claim-1", "The sample includes 40 participants.", "data",
                [source.SourceSpans!.Single().SourceId])]
        };
    }

    private static Task<ArticleEvaluationResponse> ExecuteAsync(AnalysisTestHost host,
        ArticleEvaluationRequest request) => host.InvokeAsync<ArticleEvaluationService, ArticleEvaluationResponse>(
        service => service.ExecuteAsync(request, default));

    private static ReviewArticleRequest Source()
    {
        IReadOnlyList<ArticlePage> pages = [new(1, "The study enrolled 40 participants and reports a mean of 4.2 units.")];
        string sourceHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(pages,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant();
        return new("en", "pdf", sourceHash, "pdf-v1", ArticleReviewer.DefaultPolicyVersion,
            pages, 1, false, null) { SourceSpans = ArticleSourceCatalog.Create(pages) };
    }

    private static ArticleReviewEvidence Evidence(ReviewArticleRequest source)
    {
        ArticleSourceSpan span = source.SourceSpans!.Single();
        return new(span.SourceId, span.PageNumber, span.StartOffset, span.EndOffset, span.Text);
    }

    private static async Task<ArticleEvaluationProfile> Profile(AnalysisTestHost host, string profileId) =>
        (await host.Client.GetFromJsonAsync<ArticleEvaluationProfilesResponse>("api/v1/evaluations/profiles"))!
            .Profiles.Single(value => value.ProfileId == profileId);

    private static HttpResponseMessage GeminiResponse(string answer, int prompt = 100, int candidate = 20,
        int cached = 0, int thinking = 0, string finishReason = "STOP") => JsonResponse(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = answer } } }, finishReason }
            },
            usageMetadata = new
            {
                promptTokenCount = prompt,
                cachedContentTokenCount = cached,
                candidatesTokenCount = candidate,
                thoughtsTokenCount = thinking,
                totalTokenCount = prompt + candidate + thinking
            },
            modelVersion = "gemini-3.8-flash"
        });

    private static HttpResponseMessage OllamaResponse(string content) => JsonResponse(new
    {
        model = "qwen3.8:27b-q4_K_M",
        done = true,
        done_reason = "stop",
        prompt_eval_count = 100,
        eval_count = 20,
        message = new { content }
    });

    private static HttpResponseMessage OllamaTags(string digest) => JsonResponse(new
    {
        models = new[]
        {
            new
            {
                name = ArticleEvaluationProfileCatalog.OllamaModel,
                model = ArticleEvaluationProfileCatalog.OllamaModel,
                digest
            }
        }
    });

    private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(value)
    };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
