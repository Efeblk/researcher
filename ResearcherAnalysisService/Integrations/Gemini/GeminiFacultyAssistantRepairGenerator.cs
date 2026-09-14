using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed class GeminiFacultyAssistantRepairGenerator(
    GeminiArticleClient client, IOptions<AiOptions> options) : IFacultyAssistantRepairGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public async Task<GeneratedFacultyAssistantRepair> RepairAsync(
        FacultyAssistantAnalysisRequest request,
        IReadOnlyList<GeneratedFacultyAssistantRepairCandidate> omittedCandidates,
        IReadOnlyList<GeneratedFacultyAssistantItem> supportedItems,
        CancellationToken cancellationToken)
    {
        if (options.Value.ArticleProvider != "Gemini" || omittedCandidates.Count is < 1 or > 2)
            throw new AnalysisUnavailableException("Faculty assistant repair requires Gemini and one or two candidates.");
        string input = JsonSerializer.Serialize(new
        {
            request.Mode, request.Language, request.Query, request.PrivateContext,
            omittedCandidates = omittedCandidates.Select(value => new
            {
                value.CandidateId, value.Item.Kind, value.Item.Basis, value.Item.Response,
                value.Item.EvidenceIds, value.Verdict, value.Reason
            }),
            supportedItems = supportedItems.Select(value => new
                { value.Kind, value.Basis, value.Response, value.EvidenceIds }),
            evidence = request.Evidence.Select(value => new
                { value.EvidenceId, value.CanonicalWorkId, value.PageNumber, value.ExactText,
                    value.SourceKind, value.IsPartial })
        }, JsonOptions);
        string model = options.Value.ArticleModel;
        JsonObject schema = FacultyAssistantRepairPrompt.CreateSchema(request.Mode,
            omittedCandidates.Select(value => value.CandidateId),
            request.Evidence.Select(value => value.EvidenceId));
        GeminiArticleInvocationResult invocation = await client.GenerateAttemptAsync(model,
            FacultyAssistantRepairPrompt.Instructions, input, schema,
            options.Value.FacultyAssistantMaxOutputTokens, "medium", cancellationToken);
        GeneratedFacultyAssistantGenerationAttempt attempt = ToAttempt(invocation);
        if (invocation.Result is null)
        {
            if (!IsDurablyAttested(invocation, model))
                throw new InvalidAnalysisException(invocation.Failure ?? AnalysisFailure.InvalidReport);
            return new(invocation.Failure == AnalysisFailure.OutputLimit ? "output_limit" : "invalid_response",
                [], invocation.Completion.ReturnedModel!, FacultyAssistantRepairPrompt.Version, attempt);
        }
        if (!IsDurablyAttested(invocation, model))
            throw new AnalysisUnavailableException("Gemini usage tracking is unavailable.");
        try
        {
            Envelope? envelope = JsonSerializer.Deserialize<Envelope>(invocation.Result.Json, JsonOptions);
            HashSet<string> candidateIds = omittedCandidates.Select(value => value.CandidateId)
                .ToHashSet(StringComparer.Ordinal);
            HashSet<string> evidenceIds = request.Evidence.Select(value => value.EvidenceId)
                .ToHashSet(StringComparer.Ordinal);
            if (envelope?.Items is null || envelope.Items.Count > omittedCandidates.Count ||
                envelope.Items.Any(value => value is null || !candidateIds.Contains(value.CandidateId) ||
                    !FacultyAssistantPrompt.IsKindAllowed(request.Mode, value.Kind) ||
                    string.IsNullOrWhiteSpace(value.Basis) || value.Basis.Length > 1200 ||
                    FacultyAssistantPrompt.IsWholeFieldPlaceholder(value.Basis) ||
                    value.Kind == "source_observation" && value.Response is not null ||
                    value.Kind != "source_observation" && (string.IsNullOrWhiteSpace(value.Response) ||
                        value.Response.Length > 1200 || FacultyAssistantPrompt.IsWholeFieldPlaceholder(value.Response)) ||
                    value.EvidenceIds is null || value.EvidenceIds.Count is < 1 or > 2 ||
                    value.EvidenceIds.Any(id => !evidenceIds.Contains(id)) ||
                    value.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != value.EvidenceIds.Count) ||
                envelope.Items.Select(value => value!.CandidateId).Distinct(StringComparer.Ordinal).Count() !=
                    envelope.Items.Count)
                return new("invalid_response", [], invocation.Result.Model,
                    FacultyAssistantRepairPrompt.Version, attempt);
            List<GeneratedFacultyAssistantRepairItem> items = envelope.Items.Select(value => value!)
                .Select(value => new GeneratedFacultyAssistantRepairItem(value.CandidateId,
                    new(value.Kind, value.Basis, value.Response, value.EvidenceIds))).ToList();
            if (items.Any(replacement => supportedItems.Any(existing => Same(existing, replacement.Item))))
                return new("invalid_response", [], invocation.Result.Model,
                    FacultyAssistantRepairPrompt.Version, attempt);
            return new("completed", items, invocation.Result.Model,
                FacultyAssistantRepairPrompt.Version, attempt);
        }
        catch (JsonException)
        {
            return new("invalid_response", [], invocation.Result.Model,
                FacultyAssistantRepairPrompt.Version, attempt);
        }
    }

    private static bool Same(GeneratedFacultyAssistantItem left, GeneratedFacultyAssistantItem right) =>
        left.Kind == right.Kind && left.Basis == right.Basis && left.Response == right.Response;

    private static bool IsDurablyAttested(GeminiArticleInvocationResult invocation, string requestedModel) =>
        invocation.UsagePersisted && invocation.Completion.UsageValidForAttribution &&
        invocation.Completion.EstimatedUsd.HasValue &&
        !string.IsNullOrWhiteSpace(invocation.Completion.PricingVersion) &&
        string.Equals(invocation.Completion.ReturnedModel, requestedModel, StringComparison.Ordinal);

    private static GeneratedFacultyAssistantGenerationAttempt ToAttempt(GeminiArticleInvocationResult invocation)
    {
        GeminiUsageCompletion usage = invocation.Completion;
        return new(1, "medium", usage.Outcome, usage.ReturnedModel ?? string.Empty,
            usage.TotalTokenCount ?? 0, usage.EstimatedUsd ?? 0, usage.PricingVersion ?? string.Empty,
            invocation.UsagePersisted) { AttemptId = invocation.AttemptId };
    }

    private sealed class Envelope { public List<Item?>? Items { get; set; } }
    private sealed class Item
    {
        public string CandidateId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Basis { get; set; } = string.Empty;
        public string? Response { get; set; }
        public List<string> EvidenceIds { get; set; } = [];
    }
}
