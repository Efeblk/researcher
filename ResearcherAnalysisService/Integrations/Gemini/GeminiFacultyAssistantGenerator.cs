using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed class GeminiFacultyAssistantGenerator(GeminiArticleClient client, IOptions<AiOptions> options)
    : IFacultyAssistantGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public async Task<GeneratedFacultyAssistantAnswer> GenerateAsync(
        FacultyAssistantAnalysisRequest request, CancellationToken cancellationToken)
    {
        if (options.Value.ArticleProvider != "Gemini")
            throw new AnalysisUnavailableException("Faculty assistant requires the configured Gemini article provider.");
        string input = JsonSerializer.Serialize(new
        {
            request.Mode, request.Language, request.Query, request.PrivateContext,
            evidence = request.Evidence.Select(value => new
            { value.EvidenceId, value.CanonicalWorkId, value.PageNumber, value.ExactText, value.SourceKind, value.IsPartial })
        }, JsonOptions);
        string model = options.Value.ArticleModel;
        int maxOutputTokens = options.Value.FacultyAssistantMaxOutputTokens;
        string initialThinkingLevel = options.Value.FacultyAssistantGenerationThinkingLevel;
        JsonObject schema = FacultyAssistantPrompt.CreateSchema(request.Mode,
            request.Evidence.Select(value => value.EvidenceId));
        List<GeneratedFacultyAssistantGenerationAttempt> attempts = [];
        GeminiArticleInvocationResult invocation = await client.GenerateAttemptAsync(model,
            FacultyAssistantPrompt.Instructions, input, schema, maxOutputTokens,
            initialThinkingLevel, cancellationToken);
        attempts.Add(ToAttempt(1, initialThinkingLevel, invocation));
        if (invocation.Failure == AnalysisFailure.OutputLimit &&
            string.Equals(initialThinkingLevel, "high", StringComparison.Ordinal) &&
            IsDurablyAttested(invocation, model))
        {
            invocation = await client.GenerateAttemptAsync(model, FacultyAssistantPrompt.Instructions,
                input, schema, maxOutputTokens, "medium", cancellationToken);
            attempts.Add(ToAttempt(2, "medium", invocation));
        }
        if (invocation.Result is null)
            throw new InvalidAnalysisException(invocation.Failure ?? AnalysisFailure.InvalidReport);
        if (!invocation.UsagePersisted)
            throw new AnalysisUnavailableException("Gemini usage tracking is unavailable.");
        GeminiArticleResult result = invocation.Result;
        Envelope? envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(result.Json, JsonOptions); }
        catch (JsonException) { throw new InvalidAnalysisException(AnalysisFailure.InvalidJson); }
        if (envelope?.Items is null || envelope.Items.Count > 6 || envelope.Items.Any(value => value is null ||
            string.IsNullOrWhiteSpace(value.Basis) || value.Basis.Length > 1200 ||
            FacultyAssistantPrompt.IsWholeFieldPlaceholder(value.Basis) ||
            value.Kind is not ("source_observation" or "review_question" or "teaching_adaptation") ||
            value.Kind == "source_observation" && value.Response is not null ||
            value.Kind != "source_observation" && (string.IsNullOrWhiteSpace(value.Response) || value.Response.Length > 1200 ||
                FacultyAssistantPrompt.IsWholeFieldPlaceholder(value.Response)) ||
            value.EvidenceIds is null || value.EvidenceIds.Count is < 1 or > 2 ||
            value.EvidenceIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 300) ||
            value.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != value.EvidenceIds.Count))
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
        return new(envelope.Items.Select(value => value!).Select(value => new GeneratedFacultyAssistantItem(
            value.Kind, value.Basis, value.Response, value.EvidenceIds)).ToList(), result.Model,
            FacultyAssistantPrompt.Version, attempts);
    }

    private static bool IsDurablyAttested(GeminiArticleInvocationResult invocation, string requestedModel) =>
        invocation.UsagePersisted && invocation.Completion.Outcome == "OutputLimit" &&
        invocation.Completion.UsageValidForAttribution && invocation.Completion.EstimatedUsd.HasValue &&
        !string.IsNullOrWhiteSpace(invocation.Completion.PricingVersion) &&
        string.Equals(invocation.Completion.ReturnedModel, requestedModel, StringComparison.Ordinal);

    private static GeneratedFacultyAssistantGenerationAttempt ToAttempt(int ordinal, string thinkingLevel,
        GeminiArticleInvocationResult invocation)
    {
        GeminiUsageCompletion usage = invocation.Completion;
        return new(ordinal, thinkingLevel, usage.Outcome, usage.ReturnedModel ?? string.Empty,
            usage.TotalTokenCount ?? 0, usage.EstimatedUsd ?? 0, usage.PricingVersion ?? string.Empty,
            invocation.UsagePersisted) { AttemptId = invocation.AttemptId };
    }

    private sealed class Envelope { public List<Item?>? Items { get; set; } }
    private sealed class Item
    {
        public string Kind { get; set; } = string.Empty;
        public string Basis { get; set; } = string.Empty;
        public string? Response { get; set; }
        public List<string> EvidenceIds { get; set; } = [];
    }
}
