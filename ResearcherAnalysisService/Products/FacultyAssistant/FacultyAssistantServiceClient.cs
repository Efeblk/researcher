using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Products.FacultyAssistant;

public sealed class FacultyAssistantServiceClient(ResearcherAnalysisService.Analysis.FacultyAssistant assistant,
    IOptions<FacultyAssistantOptions> options)
{
    private const string CurrentPromptVersion = "faculty-evidence-assistant-v10";
    private const string CurrentVerifierVersion = "faculty-evidence-assistant-verification-v7";
    private const string CurrentRepairVersion = "faculty-evidence-assistant-repair-v2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FacultyAssistantAnalysisReport> AnswerAsync(
        FacultyAssistantAnalysisRequest request, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));
        FacultyAssistantAnalysisReport? report = await assistant.AnswerAsync(request, timeout.Token);
        if (report is null || report.Items is null || report.Verification is null ||
            report.Coverage is null || report.Generation is null || report.SourceChecks is null ||
            report.Repair is null ||
            report.Mode != request.Mode || report.Language != request.Language ||
            string.IsNullOrWhiteSpace(report.Model) || string.IsNullOrWhiteSpace(report.PromptVersion) ||
            report.PromptVersion != CurrentPromptVersion ||
            report.Outcome is not ("completed" or "partial" or "no_supported_items"))
            throw new JsonException("The faculty assistant report is unusable.");
        FacultyAssistantCoverage coverage = report.Coverage;
        int[] counts = [coverage.CandidateItems, coverage.AutomaticallyCheckedItems,
            coverage.SupportedItems, coverage.UnsupportedItems, coverage.UncertainItems,
            coverage.OmittedItems, coverage.UnverifiedItems, coverage.RepairCandidateItems];
        if (counts.Any(value => value is < 0 or > 6) ||
            coverage.RepairCandidateItems > 2 ||
            coverage.AutomaticallyCheckedItems != coverage.CandidateItems - coverage.UnverifiedItems ||
            coverage.AutomaticallyCheckedItems != coverage.SupportedItems +
                coverage.UnsupportedItems + coverage.UncertainItems ||
            coverage.OmittedItems != coverage.UnsupportedItems + coverage.UncertainItems +
                coverage.UnverifiedItems ||
            coverage.SupportedItems != report.Items.Count || coverage.IsPartial != (coverage.OmittedItems > 0) ||
            coverage.CandidateItems == 0 && (report.Outcome != "no_supported_items" ||
                report.Verification.Status != "not_run" || report.Items.Count != 0 ||
                !string.IsNullOrEmpty(report.Verification.Model) ||
                !string.IsNullOrEmpty(report.Verification.PromptVersion)) ||
            coverage.CandidateItems > 0 && (report.Verification.Status !=
                    (coverage.UnverifiedItems == 0 ? "automatically_checked" : "partially_checked") ||
                string.IsNullOrWhiteSpace(report.Verification.Model) ||
                report.Verification.Model != report.Model ||
                report.Verification.PromptVersion != CurrentVerifierVersion ||
                !report.Verification.UsesSameModelFamily) ||
            report.Outcome == "completed" && (coverage.CandidateItems == 0 || coverage.OmittedItems != 0 ||
                report.RequestCoverage is not null && report.RequestCoverage.Status != "fulfilled") ||
            report.Outcome == "partial" && (coverage.SupportedItems == 0 || coverage.OmittedItems == 0 &&
                (report.RequestCoverage is null || report.RequestCoverage.Status == "fulfilled")) ||
            report.Outcome == "no_supported_items" && coverage.SupportedItems != 0)
            throw new JsonException("The faculty assistant report coverage is inconsistent.");
        Dictionary<string, FacultyAssistantEvidence> evidence = request.Evidence.ToDictionary(value => value.EvidenceId);
        if (report.Items.Count > 6 ||
            report.Items.Any(item => item is null || item.Citations is null ||
            string.IsNullOrWhiteSpace(item.CandidateId) || item.CandidateId.Length > 100 ||
            string.IsNullOrWhiteSpace(item.Basis) || item.Basis.Length > 1200 ||
            item.Response?.Length > 1200 ||
            !IsKindAllowed(request.Mode, item.Kind) ||
            item.Kind == "source_observation" && item.Response is not null ||
            item.Kind != "source_observation" && string.IsNullOrWhiteSpace(item.Response) ||
            item.Citations.Count is < 1 or > 2 || item.Citations.Any(citation => citation is null ||
                string.IsNullOrWhiteSpace(citation.EvidenceId) || citation.ExactQuote?.Length > 12000 ||
                !evidence.TryGetValue(citation.EvidenceId, out var source) ||
                citation.ExactQuote != source.ExactText) ||
            item.Citations.Select(value => value.EvidenceId).Distinct(StringComparer.Ordinal).Count() !=
                item.Citations.Count))
            throw new JsonException("The faculty assistant report contains invalid evidence.");
        if (report.Items.Select(item => item.CandidateId).Distinct(StringComparer.Ordinal).Count() != report.Items.Count ||
            report.Items.Any(item =>
            {
                FacultyAssistantSourceCheck? finalCheck = report.SourceChecks!.Checks
                    .Where(check => check.CandidateId == item.CandidateId)
                    .OrderBy(check => check.Origin == "repair" ? 1 : 0).LastOrDefault();
                return finalCheck is null || finalCheck.Status != "supported" ||
                    !finalCheck.EvidenceIds.SequenceEqual(item.Citations.Select(citation => citation.EvidenceId),
                        StringComparer.Ordinal);
            }))
            throw new JsonException("The faculty assistant retained-item provenance is invalid.");
        ValidateGeneration(report);
        ValidateSourceChecks(report, request);
        ValidateRepair(report);
        ValidateAttemptIds(report);
        ValidateRequestCoverage(report);
        return report;
    }

    private static void ValidateSourceChecks(FacultyAssistantAnalysisReport report,
        FacultyAssistantAnalysisRequest request)
    {
        FacultyAssistantSourceChecks value = report.SourceChecks!;
        if (value.Checks is null || value.TotalCandidates != value.Checks.Count ||
            value.TotalCandidates is < 0 or > 8 || value.CompletedChecks != value.SupportedItems +
                value.UnsupportedItems + value.UncertainItems ||
            value.TotalCandidates != value.CompletedChecks + value.UnverifiedOutputLimitItems +
                value.UnverifiedInvalidResponseItems ||
            value.Checks.Count(check => check.Status == "supported") != value.SupportedItems ||
            value.Checks.Count(check => check.Status == "unsupported") != value.UnsupportedItems ||
            value.Checks.Count(check => check.Status == "uncertain") != value.UncertainItems ||
            value.Checks.Count(check => check.Status == "unverified_output_limit") !=
                value.UnverifiedOutputLimitItems ||
            value.Checks.Count(check => check.Status == "unverified_invalid_response") !=
                value.UnverifiedInvalidResponseItems ||
            value.Checks.Count(check => check.Origin == "initial") != report.Coverage!.CandidateItems ||
            value.Checks.Count(check => check.Origin == "repair") != report.Coverage.RepairCandidateItems)
            throw new JsonException("The faculty assistant source-check metadata is invalid.");
        HashSet<string> evidenceIds = request.Evidence.Select(item => item.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (FacultyAssistantSourceCheck check in value.Checks)
        {
            if (check is null || check.AttemptId == Guid.Empty ||
                string.IsNullOrWhiteSpace(check.CandidateId) || check.CandidateId.Length > 100 ||
                check.Origin is not ("initial" or "repair") ||
                check.Status is not ("supported" or "unsupported" or "uncertain" or
                    "unverified_output_limit" or "unverified_invalid_response") ||
                string.IsNullOrWhiteSpace(check.Reason) || check.Reason.Length > 500 ||
                string.IsNullOrWhiteSpace(check.Model) || check.Model.Length > 200 ||
                check.Model != report.Model || check.PromptVersion != CurrentVerifierVersion ||
                check.EvidenceIds is null || check.EvidenceIds.Count is < 1 or > 2 ||
                check.EvidenceIds.Any(id => !evidenceIds.Contains(id)) ||
                check.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != check.EvidenceIds.Count)
                throw new JsonException("The faculty assistant source-check metadata is invalid.");
        }
        if (value.Checks.Select(check => check.AttemptId).Distinct().Count() != value.Checks.Count ||
            value.Checks.GroupBy(check => (check.Origin, check.CandidateId)).Any(group => group.Count() != 1))
            throw new JsonException("The faculty assistant source-check metadata is invalid.");
        FacultyAssistantSourceCheck[] initial = value.Checks.Where(check => check.Origin == "initial").ToArray();
        if (!initial.Select(check => check.CandidateId).SequenceEqual(
                Enumerable.Range(1, report.Coverage!.CandidateItems).Select(index => $"assistant-{index}"),
                StringComparer.Ordinal))
            throw new JsonException("The faculty assistant source-check metadata is invalid.");
        string[] repairable = initial.Where(check => check.Status is "unsupported" or "uncertain")
            .Take(2).Select(check => check.CandidateId).ToArray();
        FacultyAssistantSourceCheck[] repairs = value.Checks.Where(check => check.Origin == "repair").ToArray();
        if (repairs.Any(check => !repairable.Contains(check.CandidateId, StringComparer.Ordinal)) ||
            report.Repair!.RequestedCandidates != repairable.Length ||
            report.Repair.GeneratedCandidates != repairs.Length ||
            report.Repair.RetainedItems != repairs.Count(check => check.Status == "supported"))
            throw new JsonException("The faculty assistant repair/source-check linkage is invalid.");
        string[] finalStatuses = initial.Select(check => repairs.LastOrDefault(repair =>
            repair.CandidateId == check.CandidateId)?.Status ?? check.Status).ToArray();
        FacultyAssistantCoverage coverage = report.Coverage;
        int finalUnverified = finalStatuses.Count(status => status.StartsWith("unverified_", StringComparison.Ordinal));
        if (finalStatuses.Count(status => status == "supported") != coverage.SupportedItems ||
            finalStatuses.Count(status => status == "unsupported") != coverage.UnsupportedItems ||
            finalStatuses.Count(status => status == "uncertain") != coverage.UncertainItems ||
            finalUnverified != coverage.UnverifiedItems ||
            finalStatuses.Length - finalUnverified != coverage.AutomaticallyCheckedItems)
            throw new JsonException("The faculty assistant final-slot coverage is invalid.");
    }

    private static void ValidateRepair(FacultyAssistantAnalysisReport report)
    {
        FacultyAssistantRepair repair = report.Repair!;
        if (repair.Status is not ("not_run" or "completed" or "output_limit" or "invalid_response") ||
            repair.RequestedCandidates is < 0 or > 2 || repair.GeneratedCandidates is < 0 or > 2 ||
            repair.GeneratedCandidates > repair.RequestedCandidates || repair.RetainedItems is < 0 or > 2 ||
            repair.RetainedItems > repair.GeneratedCandidates)
            throw new JsonException("The faculty assistant repair metadata is invalid.");
        if (repair.Status == "not_run")
        {
            if (repair.RequestedCandidates != 0 || repair.GeneratedCandidates != 0 || repair.RetainedItems != 0 ||
                !string.IsNullOrEmpty(repair.Model) || !string.IsNullOrEmpty(repair.PromptVersion) ||
                repair.Attempt is not null)
                throw new JsonException("The faculty assistant repair metadata is invalid.");
            return;
        }
        FacultyAssistantGenerationAttempt? attempt = repair.Attempt;
        if (repair.RequestedCandidates == 0 || attempt is null || attempt.AttemptId == Guid.Empty ||
            attempt.Ordinal != 1 || attempt.ThinkingLevel != "medium" || attempt.Model != repair.Model ||
            attempt.TotalTokenCount <= 0 || attempt.EstimatedUsd <= 0 || !attempt.UsagePersisted ||
            string.IsNullOrWhiteSpace(attempt.PricingVersion) || attempt.PricingVersion.Length > 100 ||
            repair.Model != report.Model || repair.PromptVersion != CurrentRepairVersion ||
            repair.Status == "completed" && attempt.Outcome != "Success" ||
            repair.Status == "output_limit" && attempt.Outcome != "OutputLimit" ||
            repair.Status == "invalid_response" && attempt.Outcome is not
                ("Success" or "InvalidJson" or "IncompleteOutput" or "InvalidResponse" or "InvalidEvidence") ||
            repair.Status != "completed" && repair.GeneratedCandidates != 0)
            throw new JsonException("The faculty assistant repair metadata is invalid.");
    }

    private static void ValidateAttemptIds(FacultyAssistantAnalysisReport report)
    {
        IEnumerable<Guid> generation = report.Generation!.Attempts.Select(value => value.AttemptId);
        IEnumerable<Guid> source = report.SourceChecks!.Checks.Select(value => value.AttemptId);
        IEnumerable<Guid> repair = report.Repair!.Attempt is null ? [] : [report.Repair.Attempt.AttemptId];
        Guid[] ids = generation.Concat(source).Concat(repair).ToArray();
        if (ids.Any(value => value == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new JsonException("The faculty assistant attempt identities are invalid.");
    }

    private static void ValidateGeneration(FacultyAssistantAnalysisReport report)
    {
        FacultyAssistantGeneration generation = report.Generation!;
        if (generation.Attempts is null || generation.Attempts.Count is < 1 or > 2 ||
            generation.UsedOutputLimitRecovery != (generation.Attempts.Count == 2))
            throw new JsonException("The faculty assistant generation metadata is invalid.");
        for (int index = 0; index < generation.Attempts.Count; index++)
        {
            FacultyAssistantGenerationAttempt attempt = generation.Attempts[index];
            if (attempt is null || attempt.Ordinal != index + 1 ||
                attempt.ThinkingLevel is not ("low" or "medium" or "high") ||
                attempt.Outcome is not ("Success" or "OutputLimit") ||
                attempt.Model != report.Model || attempt.TotalTokenCount <= 0 ||
                attempt.EstimatedUsd <= 0 || string.IsNullOrWhiteSpace(attempt.PricingVersion) ||
                attempt.PricingVersion.Length > 100 || !attempt.UsagePersisted)
                throw new JsonException("The faculty assistant generation metadata is invalid.");
        }
        bool recovered = generation.UsedOutputLimitRecovery;
        if (generation.Attempts[^1].Outcome != "Success" || recovered &&
            (generation.Attempts[0].Outcome != "OutputLimit" ||
             generation.Attempts[0].ThinkingLevel != "high" ||
             generation.Attempts[1].ThinkingLevel != "medium") ||
            !recovered && generation.Attempts[0].Outcome != "Success")
            throw new JsonException("The faculty assistant generation metadata is invalid.");
    }

    private static void ValidateRequestCoverage(FacultyAssistantAnalysisReport report)
    {
        FacultyRequestCoverage? coverage = report.RequestCoverage;
        if (coverage is null)
            throw new JsonException("The faculty assistant request coverage is missing.");
        if (coverage.Model is null || coverage.PromptVersion is null ||
            string.IsNullOrWhiteSpace(coverage.Limitation) || coverage.Limitation.Length > 500 ||
            coverage.Requirements is null)
            throw new JsonException("The faculty assistant request coverage is invalid.");
        if (coverage.Status == "unavailable")
        {
            if (report.Items.Count == 0 || coverage.Requirements.Count != 0 ||
                !string.IsNullOrEmpty(coverage.Model) || !string.IsNullOrEmpty(coverage.PromptVersion) ||
                coverage.UsesSameModelFamily)
                throw new JsonException("The faculty assistant request coverage is invalid.");
            return;
        }
        if (coverage.Status is not ("fulfilled" or "partial" or "unanswered") ||
            coverage.Requirements.Count is < 1 or > 8)
            throw new JsonException("The faculty assistant request coverage is invalid.");
        bool deterministicEmpty = report.Items.Count == 0;
        if (deterministicEmpty != string.IsNullOrEmpty(coverage.Model) ||
            deterministicEmpty != string.IsNullOrEmpty(coverage.PromptVersion) ||
            coverage.Model.Length > 200 || coverage.PromptVersion.Length > 200 ||
            deterministicEmpty && coverage.UsesSameModelFamily ||
            !deterministicEmpty && (string.IsNullOrWhiteSpace(coverage.Model) ||
                string.IsNullOrWhiteSpace(coverage.PromptVersion) ||
                coverage.UsesSameModelFamily != SameFamily(report.Model, coverage.Model)))
            throw new JsonException("The faculty assistant request coverage is invalid.");
        for (int index = 0; index < coverage.Requirements.Count; index++)
        {
            FacultyRequestCoverageRequirement requirement = coverage.Requirements[index];
            if (requirement is null || requirement.RequirementId != $"requirement-{index + 1}" ||
                string.IsNullOrWhiteSpace(requirement.Requirement) || requirement.Requirement.Length > 500 ||
                requirement.Status is not ("fulfilled" or "partial" or "unanswered") ||
                requirement.ItemIndexes is null || string.IsNullOrWhiteSpace(requirement.Reason) ||
                requirement.Reason.Length > 500 ||
                requirement.ItemIndexes.Any(itemIndex => itemIndex < 1 || itemIndex > report.Items.Count) ||
                requirement.ItemIndexes.Distinct().Count() != requirement.ItemIndexes.Count ||
                requirement.Status == "unanswered" && requirement.ItemIndexes.Count != 0 ||
                requirement.Status != "unanswered" && requirement.ItemIndexes.Count == 0)
                throw new JsonException("The faculty assistant request coverage is invalid.");
        }
        string expectedStatus = coverage.Requirements.All(value => value.Status == "fulfilled") ? "fulfilled" :
            coverage.Requirements.All(value => value.Status == "unanswered") ? "unanswered" : "partial";
        if (coverage.Status != expectedStatus || deterministicEmpty && coverage.Status != "unanswered")
            throw new JsonException("The faculty assistant request coverage is invalid.");
    }

    private static bool SameFamily(string left, string right) =>
        string.Equals(Family(left), Family(right), StringComparison.OrdinalIgnoreCase);

    private static string Family(string model)
    {
        int separator = model.IndexOfAny(['-', ':']);
        return separator < 0 ? model : model[..separator];
    }

    private static bool IsKindAllowed(string mode, string kind) => mode == "TeachingHelp"
        ? kind is "teaching_adaptation" or "review_question"
        : kind is "source_observation" or "review_question";
}
