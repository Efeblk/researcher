using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.DeepSeek;
using ResearcherAnalysisService.Integrations.Gemini;
using ResearcherAnalysisService.Integrations.Ollama;

namespace ResearcherAnalysisService.Analysis;

public sealed class ArticleEvaluationService(
    ArticleEvaluationProfileCatalog profileCatalog,
    IHttpClientFactory httpClientFactory,
    IOptions<AiOptions> productionAiOptions,
    IOptions<GeminiOptions> geminiOptions,
    IOptions<ArticleEvaluationOptions> evaluationOptions,
    IGeminiUsageRepository geminiUsageRepository)
{
    private static readonly JsonSerializerOptions IdentityJsonOptions = new(JsonSerializerDefaults.Web);

    public ArticleEvaluationProfilesResponse GetProfiles() =>
        new(profileCatalog.GetDefinitions().Select(value => value.ToContract()).ToList());

    public async Task<ArticleEvaluationResponse> ExecuteAsync(ArticleEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        ArticleEvaluationProfileDefinition profile = ValidatePreflight(request);
        ValidateSource(request.Source, evaluationOptions.Value.MaximumInputBytes);
        ValidateTaskShape(request);
        string sourceIdentity = CreateSourceIdentity(request.Source);
        using CancellationTokenSource total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(TimeSpan.FromSeconds(evaluationOptions.Value.TimeoutSeconds));
        await ValidateRuntimeProfileAsync(profile, total.Token);
        int callBudget = request.TaskKind switch
        {
            ArticleEvaluationTaskKinds.Calibration => 1,
            ArticleEvaluationTaskKinds.Review => 8,
            ArticleEvaluationTaskKinds.CrossCheck => 4,
            _ => throw new InvalidOperationException()
        };
        ArticleEvaluationAttemptRecorder recorder = new(callBudget);
        (IArticleClaimVerifier claimVerifier, IArticleReviewGenerator reviewGenerator,
            IArticleReviewVerifier reviewVerifier) = CreateAdapters(profile, recorder);
        try
        {
            ArticleReviewReport? reviewReport = null;
            IReadOnlyList<ArticleEvaluationVerdict>? verdicts = null;
            if (request.TaskKind == ArticleEvaluationTaskKinds.Calibration)
                verdicts = await CalibrateAsync(request, claimVerifier, recorder, total.Token);
            else if (request.TaskKind == ArticleEvaluationTaskKinds.Review)
                reviewReport = await ReviewAsync(request.Source, profile, reviewGenerator, reviewVerifier,
                    recorder, total.Token);
            else
                verdicts = await CrossCheckAsync(request, reviewVerifier, recorder, total.Token);
            elapsed.Stop();
            return CreateResponse(profile, request.TaskKind, sourceIdentity, ArticleEvaluationOutcomes.Completed,
                null, elapsed.ElapsedMilliseconds, recorder, reviewReport, verdicts);
        }
        catch (Exception exception) when (IsOperationalFailure(exception))
        {
            elapsed.Stop();
            string errorCode = ErrorCode(exception, cancellationToken, total.Token);
            if (exception is InvalidAnalysisException)
                recorder.MarkLatestCompletedAttemptFailed(FailureReason(exception, errorCode));
            if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested &&
                total.Token.IsCancellationRequested)
                recorder.MarkLatestCancelledAttemptTimedOut();
            return CreateResponse(profile, request.TaskKind, sourceIdentity, ArticleEvaluationOutcomes.Failed,
                errorCode, elapsed.ElapsedMilliseconds, recorder, null, null,
                Failure(exception, errorCode, recorder));
        }
    }

    private async Task ValidateRuntimeProfileAsync(ArticleEvaluationProfileDefinition profile,
        CancellationToken cancellationToken)
    {
        if (profile.Provider != "Ollama") return;
        Uri baseUrl = new(productionAiOptions.Value.OllamaBaseUrl, UriKind.Absolute);
        try
        {
            using HttpClient client = httpClientFactory.CreateClient("ArticleEvaluationProvider");
            using HttpResponseMessage response = await client.GetAsync(new Uri(baseUrl, "/api/tags"),
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new ArticleEvaluationRequestException(503, "profile_unavailable",
                    "The local evaluation profile could not be verified.");
            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement models = document.RootElement.GetProperty("models");
            if (models.ValueKind != JsonValueKind.Array)
                throw new JsonException();
            JsonElement? installed = models.EnumerateArray().Cast<JsonElement?>().SingleOrDefault(value =>
                value.HasValue && (Text(value.Value, "model") == profile.RequestedModel ||
                    Text(value.Value, "name") == profile.RequestedModel));
            if (!installed.HasValue)
                throw new ArticleEvaluationRequestException(503, "profile_model_not_found",
                    "The selected local evaluation model is not installed.");
            string? digest = Text(installed.Value, "digest");
            if (!string.Equals(digest, profile.ModelRevision, StringComparison.OrdinalIgnoreCase))
                throw new ArticleEvaluationRequestException(409, "model_revision_mismatch",
                    "The installed local evaluation model revision does not match the selected profile.");
        }
        catch (ArticleEvaluationRequestException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new ArticleEvaluationRequestException(503, "profile_unavailable",
                "The local evaluation profile verification timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or
            KeyNotFoundException or InvalidOperationException)
        {
            throw new ArticleEvaluationRequestException(503, "profile_unavailable",
                "The local evaluation profile could not be verified.");
        }
    }

    private ArticleEvaluationProfileDefinition ValidatePreflight(ArticleEvaluationRequest request)
    {
        if (request is null)
            throw new ArticleEvaluationRequestException(400, "invalid_request", "An evaluation request is required.");
        ArticleEvaluationProfileDefinition profile = profileCatalog.GetRequired(request.ProfileId);
        if (request.TaskKind is not (ArticleEvaluationTaskKinds.Calibration or ArticleEvaluationTaskKinds.Review or
            ArticleEvaluationTaskKinds.CrossCheck))
            throw new ArticleEvaluationRequestException(400, "unknown_task", "Choose a known evaluation task.");
        if (!string.Equals(request.ExpectedSettingsFingerprint, profile.SettingsFingerprint, StringComparison.Ordinal))
            throw new ArticleEvaluationRequestException(409, "settings_fingerprint_mismatch",
                "Refresh the evaluation profiles before executing this request.");
        if (profile.Availability != "configured")
            throw new ArticleEvaluationRequestException(503, "profile_not_configured",
                "The selected evaluation profile is not configured.");
        return profile;
    }

    private (IArticleClaimVerifier, IArticleReviewGenerator, IArticleReviewVerifier) CreateAdapters(
        ArticleEvaluationProfileDefinition profile, ArticleEvaluationAttemptRecorder recorder)
    {
        HttpClient httpClient = httpClientFactory.CreateClient("ArticleEvaluationProvider");
        AiOptions localAi = new()
        {
            Provider = productionAiOptions.Value.Provider,
            ApiKey = productionAiOptions.Value.ApiKey,
            Model = productionAiOptions.Value.Model,
            OllamaBaseUrl = productionAiOptions.Value.OllamaBaseUrl,
            OllamaContextTokens = productionAiOptions.Value.OllamaContextTokens,
            TimeoutSeconds = evaluationOptions.Value.TimeoutSeconds,
            MaxOutputTokens = productionAiOptions.Value.MaxOutputTokens,
            ArticleProvider = profile.Provider,
            ArticleModel = profile.RequestedModel,
            ArticleVerifierModel = profile.RequestedModel,
            ArticleGenerationThinkingLevel = profile.Provider == "Gemini"
                ? profile.ExecutionSettings["generationThinking"] : "high",
            ArticleVerifierThinkingLevel = profile.Provider == "Gemini"
                ? profile.ExecutionSettings["verifierThinking"] : "high",
            ArticleContextTokens = profile.ContextTokens,
            ArticleMaxOutputTokens = profile.MaxOutputTokens,
            ArticleVerifierMaxOutputTokens = profile.VerifierMaxOutputTokens,
            ArticleFallbackChunkBytes = Math.Min(productionAiOptions.Value.ArticleFallbackChunkBytes,
                profile.ContextTokens - profile.MaxOutputTokens - 512),
            ArticleReviewMaximumInputBytes = evaluationOptions.Value.MaximumInputBytes,
            ArticleReviewTimeoutSeconds = evaluationOptions.Value.TimeoutSeconds
        };
        IOptions<AiOptions> localOptions = Options.Create(localAi);
        if (profile.Provider == "Gemini")
        {
            GeminiArticleClient client = new(httpClient, localOptions, geminiOptions,
                geminiUsageRepository, recorder);
            return (new GeminiArticleClaimVerifier(client, localOptions),
                new GeminiArticleReviewGenerator(client, localOptions),
                new GeminiArticleReviewVerifier(client, localOptions));
        }
        if (profile.Provider == "Ollama")
            return (new OllamaArticleClaimVerifier(httpClient, localOptions, recorder),
                new OllamaArticleReviewGenerator(httpClient, localOptions, recorder),
                new OllamaArticleReviewVerifier(httpClient, localOptions, recorder));

        ArticleEvaluationOptions deepSeekSettings = new()
        {
            TimeoutSeconds = evaluationOptions.Value.TimeoutSeconds,
            MaximumInputBytes = evaluationOptions.Value.MaximumInputBytes,
            ContextTokens = profile.ContextTokens,
            MaxOutputTokens = profile.MaxOutputTokens,
            VerifierMaxOutputTokens = profile.VerifierMaxOutputTokens,
            DeepSeek = evaluationOptions.Value.DeepSeek
        };
        DeepSeekArticleClient deepSeekClient = new(httpClient, Options.Create(deepSeekSettings), recorder);
        return (new DeepSeekArticleClaimVerifier(deepSeekClient, profile.VerifierMaxOutputTokens),
            new DeepSeekArticleReviewGenerator(deepSeekClient, profile.MaxOutputTokens),
            new DeepSeekArticleReviewVerifier(deepSeekClient, profile.VerifierMaxOutputTokens));
    }

    private static async Task<IReadOnlyList<ArticleEvaluationVerdict>> CalibrateAsync(
        ArticleEvaluationRequest request, IArticleClaimVerifier verifier,
        ArticleEvaluationAttemptRecorder recorder, CancellationToken cancellationToken)
    {
        IReadOnlyList<GeneratedArticleClaim> claims = request.CalibrationClaims!.Select(value =>
            new GeneratedArticleClaim(value.ClaimId, value.Text, value.SourceIds) { Section = value.Section }).ToList();
        using (recorder.Enter("calibration_verify", null))
        {
            GeneratedVerificationBatch result = await verifier.VerifyAsync(request.Source.Language, claims,
                request.Source.SourceSpans!, cancellationToken);
            ValidateClaimVerdicts(claims, result);
            return result.Verdicts.Select(value =>
                new ArticleEvaluationVerdict(value.ClaimId, value.Verdict, value.Reason)).ToList();
        }
    }

    private static async Task<ArticleReviewReport> ReviewAsync(ReviewArticleRequest source,
        ArticleEvaluationProfileDefinition profile, IArticleReviewGenerator generator,
        IArticleReviewVerifier verifier, ArticleEvaluationAttemptRecorder recorder,
        CancellationToken cancellationToken)
    {
        ContextReviewGenerator contextualGenerator = new(generator, recorder);
        ContextReviewVerifier contextualVerifier = new(verifier, recorder, "review_verify");
        AiOptions options = new()
        {
            ArticleProvider = profile.Provider,
            ArticleModel = profile.RequestedModel,
            ArticleVerifierModel = profile.RequestedModel,
            ArticleContextTokens = profile.ContextTokens,
            ArticleMaxOutputTokens = profile.MaxOutputTokens,
            ArticleVerifierMaxOutputTokens = profile.VerifierMaxOutputTokens,
            ArticleReviewMaximumInputBytes = int.MaxValue,
            ArticleReviewTimeoutSeconds = 600
        };
        return await new ArticleReviewer(contextualGenerator, contextualVerifier, Options.Create(options))
            .ReviewAsync(source, cancellationToken);
    }

    private static async Task<IReadOnlyList<ArticleEvaluationVerdict>> CrossCheckAsync(
        ArticleEvaluationRequest request, IArticleReviewVerifier verifier,
        ArticleEvaluationAttemptRecorder recorder, CancellationToken cancellationToken)
    {
        List<ArticleEvaluationVerdict> result = [];
        foreach (string role in ArticleReviewer.Roles)
        {
            List<ArticleReviewFinding> roleFindings = request.Findings!.Where(value => value.Role == role).ToList();
            if (roleFindings.Count == 0) continue;
            IReadOnlyList<GeneratedArticleReviewFinding> generated = roleFindings.Select(value =>
                new GeneratedArticleReviewFinding(value.FindingId, value.Role, value.Kind, value.Basis,
                    value.Suggestion, value.Evidence.Select(evidence => evidence.SourceId).ToList())).ToList();
            using (recorder.Enter("cross_check", role))
            {
                GeneratedArticleReviewVerification verification = await verifier.VerifyAsync(role,
                    request.Source.Language, generated, request.Source.SourceSpans!, cancellationToken);
                ValidateReviewVerdicts(generated, verification);
                result.AddRange(verification.Verdicts.Select(value =>
                    new ArticleEvaluationVerdict(value.FindingId, value.Verdict, value.Reason)));
            }
        }
        return result;
    }

    private static ArticleEvaluationResponse CreateResponse(ArticleEvaluationProfileDefinition profile,
        string taskKind, string sourceIdentity, string outcome, string? errorCode, long elapsedMilliseconds,
        ArticleEvaluationAttemptRecorder recorder, ArticleReviewReport? reviewReport,
        IReadOnlyList<ArticleEvaluationVerdict>? verdicts, AnalysisFailureDetail? failure = null)
    {
        IReadOnlyList<ArticleEvaluationAttemptTelemetry> attempts = recorder.Snapshot();
        IReadOnlyList<string> returnedModels = attempts.Where(value => !string.IsNullOrWhiteSpace(value.ReturnedModel))
            .Select(value => value.ReturnedModel!).Distinct(StringComparer.Ordinal).ToList();
        return new(profile.ProfileId, taskKind, profile.Provider, profile.RequestedModel, returnedModels,
            profile.SettingsFingerprint, profile.SettingsVersion, sourceIdentity, outcome, errorCode,
            new(elapsedMilliseconds, attempts.Count, attempts))
        {
            ReviewReport = reviewReport,
            Verdicts = verdicts,
            ExecutionSettings = profile.ExecutionSettings,
            Failure = failure
        };
    }

    private static void ValidateTaskShape(ArticleEvaluationRequest request)
    {
        if (request.TaskKind == ArticleEvaluationTaskKinds.Calibration)
        {
            if (request.Findings is not null || request.CalibrationClaims is null ||
                request.CalibrationClaims.Count is < 1 or > 12)
                InvalidTaskShape();
            IReadOnlyList<ArticleEvaluationCalibrationClaim> claims = request.CalibrationClaims!;
            if (claims.Any(value => value is null || !Bounded(value.ClaimId, 100) ||
                    value.ClaimId != value.ClaimId.Trim() || !Bounded(value.Text, 1_200) ||
                    value.Section is not ("purpose" or "methods" or "data" or "findings" or "limitations") ||
                    value.SourceIds is null || value.SourceIds.Count is < 1 or > 2 ||
                    value.SourceIds.Distinct(StringComparer.Ordinal).Count() != value.SourceIds.Count ||
                    value.SourceIds.Any(id => !request.Source.SourceSpans!.Any(span => span.SourceId == id))) ||
                claims.Select(value => value.ClaimId).Distinct(StringComparer.Ordinal).Count() != claims.Count)
                InvalidTaskShape();
            return;
        }
        if (request.TaskKind == ArticleEvaluationTaskKinds.Review)
        {
            if (request.CalibrationClaims is not null || request.Findings is not null)
                InvalidTaskShape();
            return;
        }
        if (request.CalibrationClaims is not null || request.Findings is null || request.Findings.Count is < 1 or > 12)
            InvalidTaskShape();
        IReadOnlyList<ArticleReviewFinding> findings = request.Findings!;
        if (findings.Any(value => value is null || !Bounded(value.FindingId, 100) ||
                value.FindingId != value.FindingId.Trim() || !ArticleReviewer.Roles.Contains(value.Role) ||
                !ArticleReviewer.IsKindAllowed(value.Role, value.Kind) || !Bounded(value.Basis, 1_200) ||
                value.Evidence is null || value.Evidence.Count is < 1 or > 2 ||
                value.Evidence.Any(evidence => evidence is null) ||
                value.Kind == "source_observation" && value.Suggestion is not null ||
                value.Kind != "source_observation" && !Bounded(value.Suggestion, 1_200) ||
                value.Evidence.Select(evidence => evidence.SourceId).Distinct(StringComparer.Ordinal).Count() !=
                    value.Evidence.Count || value.Evidence.Any(evidence => !IsExactEvidence(evidence,
                    request.Source.SourceSpans!))) ||
            findings.Select(value => value.FindingId).Distinct(StringComparer.Ordinal).Count() != findings.Count)
            InvalidTaskShape();
    }

    private static void ValidateSource(ReviewArticleRequest source, int maximumInputBytes)
    {
        if (source is null || source.Pages is null || source.SourceSpans is null ||
            source.Pages.Count is 0 or > 500 || source.SourceSpans.Count is 0 or > 50_000 ||
            source.Pages.Any(page => page is null || page.Text is null) ||
            source.SourceSpans.Any(span => span is null || span.Text is null || span.SourceId is null) ||
            source.Language is not ("tr" or "en") || source.SourceKind is not ("pdf" or "html" or "abstract") ||
            source.SourceHash is null || source.SourceHash.Length != 64 || !Bounded(source.ExtractionVersion, 100) ||
            !Bounded(source.PolicyVersion, 100) || source.TotalSourcePages <= 0 ||
            source.TotalSourcePages < source.Pages.Count || source.TotalSourcePages > source.Pages.Count && !source.IsPartial ||
            source.SourceKind == "abstract" && !source.IsPartial || source.ScopeReason?.Length > 4_000 ||
            source.IsPartial && string.IsNullOrWhiteSpace(source.ScopeReason))
            throw new ArticleEvaluationRequestException(400, "invalid_source", "The evaluation source is invalid.");
        long sourceBytes = source.Pages.Sum(page => (long)Encoding.UTF8.GetByteCount(page.Text)) +
            source.SourceSpans.Sum(span => (long)Encoding.UTF8.GetByteCount(span.SourceId) +
                Encoding.UTF8.GetByteCount(span.Text) + 80L);
        if (sourceBytes > maximumInputBytes)
            throw new ArticleEvaluationRequestException(413, "input_too_large", "The evaluation source is too large.");
        string expectedSourceHash = Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(source.Pages, IdentityJsonOptions))).ToLowerInvariant();
        if (!string.Equals(source.SourceHash, expectedSourceHash, StringComparison.OrdinalIgnoreCase))
            throw new ArticleEvaluationRequestException(400, "source_hash_mismatch",
                "The evaluation source hash does not match its pages.");
        if (!ArticleSourceCatalog.IsValid(source.Pages, source.SourceSpans, source.SourceKind))
            throw new ArticleEvaluationRequestException(400, "invalid_source", "The source catalog is not canonical.");
    }

    private static void ValidateClaimVerdicts(IReadOnlyList<GeneratedArticleClaim> claims,
        GeneratedVerificationBatch verification)
    {
        if (verification is null || verification.PromptVersion != ArticleVerificationPrompt.Version ||
            !Bounded(verification.Model, 200) || verification.Verdicts is null ||
            verification.Verdicts.Count != claims.Count || verification.Verdicts.Any(value => value is null) ||
            verification.Verdicts.Select(value => value.ClaimId).Distinct(StringComparer.Ordinal).Count() != claims.Count ||
            verification.Verdicts.Any(value =>
                !claims.Any(claim => claim.ClaimId == value.ClaimId) ||
                value.Verdict is not ("supported" or "unsupported" or "uncertain") || !Bounded(value.Reason, 500)))
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }

    private static void ValidateReviewVerdicts(IReadOnlyList<GeneratedArticleReviewFinding> findings,
        GeneratedArticleReviewVerification verification)
    {
        if (verification is null || verification.PromptVersion != ArticleReviewVerificationPrompt.Version ||
            !Bounded(verification.Model, 200) || verification.Verdicts is null ||
            verification.Verdicts.Count != findings.Count || verification.Verdicts.Any(value => value is null) ||
            verification.Verdicts.Select(value => value.FindingId).Distinct(StringComparer.Ordinal).Count() != findings.Count ||
            verification.Verdicts.Any(value =>
                !findings.Any(finding => finding.FindingId == value.FindingId) ||
                value.Verdict is not ("supported" or "unsupported" or "uncertain") || !Bounded(value.Reason, 500)))
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }

    private static bool IsExactEvidence(ArticleReviewEvidence evidence,
        IReadOnlyList<ArticleSourceSpan> sourceSpans) => evidence is not null &&
        sourceSpans.Any(span => span.SourceId == evidence.SourceId && span.PageNumber == evidence.PageNumber &&
            span.StartOffset == evidence.StartOffset && span.EndOffset == evidence.EndOffset &&
            span.Text == evidence.Quote);

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string CreateSourceIdentity(ReviewArticleRequest source)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(source, IdentityJsonOptions);
        return Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
    }

    private static void InvalidTaskShape() => throw new ArticleEvaluationRequestException(400,
        "invalid_task_input", "The evaluation task input is invalid.");

    private static bool Bounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    private static bool IsOperationalFailure(Exception exception) => exception is
        AnalysisInputTooLargeException or AnalysisUnavailableException or InvalidAnalysisException or
        HttpRequestException or OperationCanceledException or InvalidOperationException;

    private static string ErrorCode(Exception exception, CancellationToken callerToken,
        CancellationToken totalToken) => exception switch
    {
        AnalysisInputTooLargeException => "input_too_large",
        AnalysisUnavailableException => "provider_unavailable",
        InvalidAnalysisException => "invalid_provider_response",
        HttpRequestException => "provider_failure",
        OperationCanceledException when callerToken.IsCancellationRequested => "cancelled",
        OperationCanceledException when totalToken.IsCancellationRequested => "timeout",
        OperationCanceledException => "cancelled",
        InvalidOperationException => "call_budget_exceeded",
        _ => "evaluation_failed"
    };

    private static AnalysisFailureDetail Failure(Exception exception, string fallbackReason,
        ArticleEvaluationAttemptRecorder recorder)
    {
        ArticleEvaluationAttemptTelemetry? latest = recorder.Snapshot().LastOrDefault();
        return exception switch
        {
            InvalidAnalysisException invalid => new(InvalidAnalysisException.CodeFor(invalid.Reason),
                invalid.Stage ?? latest?.Stage, invalid.Role ?? latest?.Role),
            ArticleReviewTimedOutException timeout => new("timeout", timeout.Stage, timeout.Role),
            OperationCanceledException => new(fallbackReason, latest?.Stage, latest?.Role),
            _ => new(fallbackReason, null, null)
        };
    }

    private static string FailureReason(Exception exception, string fallbackReason) =>
        exception is InvalidAnalysisException invalid
            ? InvalidAnalysisException.CodeFor(invalid.Reason)
            : fallbackReason;

    private sealed class ContextReviewGenerator(IArticleReviewGenerator inner,
        ArticleEvaluationAttemptRecorder recorder) : IArticleReviewGenerator
    {
        public async Task<GeneratedArticleReviewPass> GenerateAsync(string role, string language,
            string sourceKind, IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
        {
            using (recorder.Enter("review_generate", role))
                return await inner.GenerateAsync(role, language, sourceKind, sourceSpans, cancellationToken);
        }
    }

    private sealed class ContextReviewVerifier(IArticleReviewVerifier inner,
        ArticleEvaluationAttemptRecorder recorder, string stage) : IArticleReviewVerifier
    {
        public async Task<GeneratedArticleReviewVerification> VerifyAsync(string role, string language,
            IReadOnlyList<GeneratedArticleReviewFinding> findings, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            using (recorder.Enter(stage, role))
                return await inner.VerifyAsync(role, language, findings, sourceSpans, cancellationToken);
        }
    }
}
