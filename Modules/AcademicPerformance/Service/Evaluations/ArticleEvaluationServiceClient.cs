using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Evaluations;

public sealed class ArticleEvaluationServiceClient(
    HttpClient client,
    IOptions<AnalysisServiceOptions> analysisOptions)
{
    public async Task<ArticleEvaluationProfilesResponse> GetProfilesAsync(
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Create(HttpMethod.Get, "api/v1/evaluations/profiles");
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        ArticleEvaluationProfilesResponse result = await response.Content
            .ReadFromJsonAsync<ArticleEvaluationProfilesResponse>(cancellationToken)
            ?? throw new JsonException("The analysis service returned no evaluation profiles.");
        if (result.Profiles is null || result.Profiles.Count > 20 || result.Profiles.Any(profile =>
                profile is null || !Bounded(profile.ProfileId, 100) || !Bounded(profile.DisplayName, 200) ||
                !Bounded(profile.Provider, 100) || !Bounded(profile.RequestedModel, 300) ||
                !Bounded(profile.SettingsFingerprint, 64) || !Bounded(profile.SettingsVersion, 100) ||
                profile.TaskKinds is null || profile.TaskKinds.Count > 3 ||
                profile.TaskKinds.Any(value => !Bounded(value, 30)) || profile.ExecutionSettings?.Count > 30 ||
                profile.ExecutionSettings?.Any(value => !Bounded(value.Key, 100) || !Bounded(value.Value, 500)) == true))
            throw new JsonException("The analysis service returned invalid evaluation profile metadata.");
        return result;
    }

    public async Task<AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse> ExecuteAsync(
        ArticleEvaluationProfile profile,
        ArticleEvaluationRequest payload,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Create(HttpMethod.Post, "api/v1/evaluations/execute");
        request.Content = JsonContent.Create(payload);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.ServiceUnavailable)
            throw new ArticleEvaluationPreflightException(response.StatusCode);
        response.EnsureSuccessStatusCode();
        AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse result = await response.Content
            .ReadFromJsonAsync<AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse>(cancellationToken)
            ?? throw new JsonException("The analysis service returned no evaluation result.");
        Validate(result, profile, payload);
        return result;
    }

    private HttpRequestMessage Create(HttpMethod method, string uri)
    {
        HttpRequestMessage request = new(method, uri);
        if (!string.IsNullOrWhiteSpace(analysisOptions.Value.ApiKey))
            request.Headers.Add("X-Analysis-Key", analysisOptions.Value.ApiKey);
        return request;
    }

    private static void Validate(
        AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse result,
        ArticleEvaluationProfile profile,
        ArticleEvaluationRequest request)
    {
        if (result.ProfileId != request.ProfileId || result.TaskKind != request.TaskKind ||
            result.SettingsFingerprint != request.ExpectedSettingsFingerprint ||
            result.Provider != profile.Provider || result.RequestedModel != profile.RequestedModel ||
            result.SettingsVersion != profile.SettingsVersion ||
            !DictionaryEqual(result.ExecutionSettings, profile.ExecutionSettings) ||
            !Bounded(result.Provider, 100) || !Bounded(result.RequestedModel, 300) ||
            !Bounded(result.SettingsVersion, 100) || !Bounded(result.SourceIdentity, 200) ||
            result.ReturnedModels is null || result.ReturnedModels.Count > 20 ||
            result.ReturnedModels.Any(model => !Bounded(model, 500)) || result.Telemetry is null ||
            result.Telemetry.Attempts is null || result.Telemetry.AttemptCount != result.Telemetry.Attempts.Count ||
            result.Telemetry.Attempts.Count > 8 || result.ExecutionSettings?.Count > 30 ||
            result.Outcome is not (ArticleEvaluationOutcomes.Completed or ArticleEvaluationOutcomes.Failed))
            throw new JsonException("The evaluation response does not match its pinned request.");
        if (result.Failure is not null && (!SafeCode(result.Failure.Reason) ||
                result.Failure.Stage is not null && !SafeCode(result.Failure.Stage) ||
                result.Failure.Role is not null && !SafeCode(result.Failure.Role)))
            throw new JsonException("The evaluation response contains invalid failure detail.");
        if (result.Outcome == ArticleEvaluationOutcomes.Completed && result.Failure is not null)
            throw new JsonException("The evaluation response failure detail does not match its outcome.");
        string sourceIdentity = Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(request.Source, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
            .ToLowerInvariant();
        if (result.SourceIdentity != sourceIdentity)
            throw new JsonException("The evaluation response identifies a different immutable source.");
        if (result.Outcome == ArticleEvaluationOutcomes.Completed && result.ReturnedModels.Count == 0)
            throw new JsonException("A completed evaluation omitted the actual returned model identity.");
        int maximumAttempts = request.TaskKind switch
        {
            ArticleEvaluationTaskKinds.Calibration => 1,
            ArticleEvaluationTaskKinds.Review => 8,
            ArticleEvaluationTaskKinds.CrossCheck => 4,
            _ => 0
        };
        if (result.Telemetry.Attempts.Select(value => value.AttemptNumber).Distinct().Count() !=
                result.Telemetry.Attempts.Count ||
            result.Telemetry.Attempts.Any(value => value is null || value.AttemptNumber is < 1 || value.AttemptNumber > maximumAttempts ||
                value.Provider != profile.Provider || value.RequestedModel != profile.RequestedModel ||
                value.ElapsedMilliseconds < 0 || value.CompletedAtUtc < value.StartedAtUtc ||
                value.InputTokens < 0 || value.OutputTokens < 0 || value.CacheReadTokens < 0 ||
                value.CacheWriteTokens < 0 || value.ThinkingTokens < 0 ||
                value.ThinkingTokens > value.OutputTokens ||
                value.InputTokens.HasValue && value.CacheReadTokens.GetValueOrDefault() +
                    value.CacheWriteTokens.GetValueOrDefault() > value.InputTokens.Value ||
                value.EstimatedCostUsd < 0 ||
                value.ReturnedModel is not null && value.ReturnedModel.Length > 500))
            throw new JsonException("The evaluation response contains invalid per-attempt telemetry.");
        if (result.Outcome == ArticleEvaluationOutcomes.Completed && request.TaskKind == ArticleEvaluationTaskKinds.Calibration)
            ValidateVerdicts(result.Verdicts, request.CalibrationClaims?.Select(value => value.ClaimId));
        if (result.Outcome == ArticleEvaluationOutcomes.Completed && request.TaskKind == ArticleEvaluationTaskKinds.CrossCheck)
            ValidateVerdicts(result.Verdicts, request.Findings?.Select(value => value.FindingId));
    }

    private static bool Bounded(string? value, int length) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= length;

    private static bool SafeCode(string? value) => Bounded(value, 100) &&
        value!.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private static void ValidateVerdicts(IReadOnlyList<ArticleEvaluationVerdict>? verdicts,
        IEnumerable<string>? expectedIds)
    {
        List<string> expected = expectedIds?.ToList() ?? [];
        if (verdicts is null || verdicts.Count != expected.Count ||
            verdicts.Select(value => value.ItemId).Distinct(StringComparer.Ordinal).Count() != verdicts.Count ||
            !verdicts.Select(value => value.ItemId).Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            verdicts.Any(value => value is null || value.Verdict is not ("supported" or "unsupported" or "uncertain") ||
                !Bounded(value.Reason, 1000)))
            throw new JsonException("The evaluation response contains invalid verdicts.");
    }

    private static bool DictionaryEqual(IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right) =>
        (left ?? new Dictionary<string, string>()).OrderBy(value => value.Key, StringComparer.Ordinal)
            .SequenceEqual((right ?? new Dictionary<string, string>()).OrderBy(value => value.Key, StringComparer.Ordinal));
}

public sealed class ArticleEvaluationPreflightException(HttpStatusCode statusCode) : Exception
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
