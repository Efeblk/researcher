using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Analysis;

public sealed class FacultyAssistant(
    IFacultyAssistantGenerator generator,
    IFacultyAssistantVerifier verifier,
    IFacultyAssistantRepairGenerator repairGenerator,
    IFacultyRequestCoverageVerifier requestCoverageVerifier)
{
    public async Task<FacultyAssistantAnalysisReport> AnswerAsync(
        FacultyAssistantAnalysisRequest request, CancellationToken cancellationToken)
    {
        Dictionary<string, FacultyAssistantEvidence> evidence = request.Evidence
            .ToDictionary(value => value.EvidenceId, StringComparer.Ordinal);
        GeneratedFacultyAssistantAnswer generated = await generator.GenerateAsync(request, cancellationToken);
        ValidateGenerated(generated, request.Mode, evidence);
        FacultyAssistantGeneration generation = ValidateGeneration(generated);
        if (generated.Items.Count == 0)
            return EmptyReport(request, generated, generation);

        string role = request.Mode == "TeachingHelp" ? "teaching" :
            request.Mode == "OwnPaperMethods" ? "method" : "claim_evidence";
        List<FacultyAssistantAnswerItem> items = [];
        List<GeneratedFacultyAssistantItem> supportedGenerated = [];
        List<FacultyAssistantSourceCheck> checkAudit = [];
        List<GeneratedFacultyAssistantRepairCandidate> semanticOmissions = [];
        Dictionary<string, string> slotStatuses = new(StringComparer.Ordinal);

        for (int index = 0; index < generated.Items.Count; index++)
        {
            string candidateId = $"assistant-{index + 1}";
            GeneratedFacultyAssistantItem item = generated.Items[index];
            GeneratedFacultyAssistantSourceCheck check = await CheckAsync(
                role, request.Language, candidateId, item, evidence, cancellationToken);
            string status = StatusOf(check);
            slotStatuses[candidateId] = status;
            checkAudit.Add(ToAudit(check, candidateId, "initial", item.EvidenceIds, status));
            if (status == "supported")
            {
                supportedGenerated.Add(item);
                items.Add(ToAnswer(item, evidence, candidateId));
            }
            else if (status is "unsupported" or "uncertain")
            {
                semanticOmissions.Add(new(candidateId, item, status, check.Reason));
            }
        }

        FacultyAssistantRepair repairAudit = NotRunRepair();
        IReadOnlyList<GeneratedFacultyAssistantRepairCandidate> repairTargets = semanticOmissions.Take(2).ToList();
        if (repairTargets.Count > 0)
        {
            GeneratedFacultyAssistantRepair repair = await repairGenerator.RepairAsync(
                request, repairTargets, supportedGenerated.ToArray(), cancellationToken);
            ValidateRepair(repair, repairTargets, request.Mode, evidence);
            int retainedRepairs = 0;
            if (repair.Status == "completed")
            {
                foreach (GeneratedFacultyAssistantRepairItem replacement in repair.Items)
                {
                    string candidateId = replacement.CandidateId;
                    GeneratedFacultyAssistantSourceCheck check = await CheckAsync(role, request.Language,
                        $"repair-{candidateId}", replacement.Item, evidence, cancellationToken);
                    string status = StatusOf(check);
                    slotStatuses[candidateId] = status;
                    checkAudit.Add(ToAudit(check, candidateId, "repair", replacement.Item.EvidenceIds, status));
                    if (status == "supported")
                    {
                        supportedGenerated.Add(replacement.Item);
                        items.Add(ToAnswer(replacement.Item, evidence, candidateId));
                        retainedRepairs++;
                    }
                }
            }
            repairAudit = ToRepairAudit(repair, repairTargets.Count, retainedRepairs);
        }

        int supported = slotStatuses.Count(value => value.Value == "supported");
        int unsupported = slotStatuses.Count(value => value.Value == "unsupported");
        int uncertain = slotStatuses.Count(value => value.Value == "uncertain");
        int unverified = slotStatuses.Count(value => value.Value.StartsWith("unverified_", StringComparison.Ordinal));
        if (supported != items.Count || supported + unsupported + uncertain + unverified != generated.Items.Count)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        int omitted = unsupported + uncertain + unverified;
        FacultyAssistantCoverage coverage = new(generated.Items.Count,
            generated.Items.Count - unverified, supported, unsupported, uncertain, omitted, omitted > 0)
        {
            UnverifiedItems = unverified,
            RepairCandidateItems = checkAudit.Count(value => value.Origin == "repair")
        };
        FacultyAssistantSourceChecks sourceChecks = CreateSourceChecks(checkAudit);
        FacultyRequestCoverage requestCoverage = await GetRequestCoverage(
            request, items, generated.Model, cancellationToken);
        string outcome = items.Count == 0 ? "no_supported_items" :
            omitted == 0 && requestCoverage.Status == "fulfilled" ? "completed" : "partial";
        string[] models = checkAudit.Select(value => value.Model).Distinct(StringComparer.Ordinal).ToArray();
        bool sameFamily = models.Length > 0 && models.All(value =>
            Family(value).Equals(Family(generated.Model), StringComparison.OrdinalIgnoreCase));
        string verificationStatus = unverified == 0 ? "automatically_checked" : "partially_checked";
        return new(request.Mode, request.Language, items, generated.Model, generated.PromptVersion,
            new(verificationStatus, string.Join(',', models), FacultyAssistantVerificationPrompt.Version, sameFamily,
                "Each candidate was checked independently against only its own one or two citations. " +
                "Unverified provider outputs were omitted; checking does not prove scientific correctness or complete recall."),
            outcome, coverage)
        {
            RequestCoverage = requestCoverage,
            Generation = generation,
            SourceChecks = sourceChecks,
            Repair = repairAudit
        };
    }

    private async Task<GeneratedFacultyAssistantSourceCheck> CheckAsync(string role, string language,
        string findingId, GeneratedFacultyAssistantItem item,
        IReadOnlyDictionary<string, FacultyAssistantEvidence> evidence, CancellationToken cancellationToken)
    {
        GeneratedArticleReviewFinding finding = new(findingId, role, item.Kind,
            item.Basis, item.Response, item.EvidenceIds);
        List<ArticleSourceSpan> spans = item.EvidenceIds.Select(id => evidence[id])
            .Select(value => new ArticleSourceSpan(value.EvidenceId, value.PageNumber,
                value.StartOffset, value.EndOffset, value.ExactText)).ToList();
        GeneratedFacultyAssistantSourceCheck check = await verifier.VerifyAsync(
            role, language, finding, spans, cancellationToken);
        if (check is null || check.AttemptId == Guid.Empty || !HasBoundedText(check.Model, 200) ||
            check.PromptVersion != FacultyAssistantVerificationPrompt.Version ||
            !HasBoundedText(check.Reason, 500) ||
            check.Status is not ("checked" or "unverified_output_limit" or "unverified_invalid_response") ||
            check.Status == "checked" && (check.Verdict is null || check.Verdict.FindingId != findingId ||
                check.Verdict.Verdict is not ("supported" or "unsupported" or "uncertain") ||
                !HasBoundedText(check.Verdict.Reason, 500)) ||
            check.Status != "checked" && check.Verdict is not null)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        return check;
    }

    private static string StatusOf(GeneratedFacultyAssistantSourceCheck check) =>
        check.Status == "checked" ? check.Verdict!.Verdict : check.Status;

    private static FacultyAssistantSourceCheck ToAudit(GeneratedFacultyAssistantSourceCheck check,
        string candidateId, string origin, IReadOnlyList<string> evidenceIds, string status) =>
        new(check.AttemptId, candidateId, origin, status, check.Reason,
            check.Model, check.PromptVersion, evidenceIds);

    private static FacultyAssistantSourceChecks CreateSourceChecks(
        IReadOnlyList<FacultyAssistantSourceCheck> checks) => new(checks.Count,
        checks.Count(value => value.Status is "supported" or "unsupported" or "uncertain"),
        checks.Count(value => value.Status == "supported"),
        checks.Count(value => value.Status == "unsupported"),
        checks.Count(value => value.Status == "uncertain"),
        checks.Count(value => value.Status == "unverified_output_limit"),
        checks.Count(value => value.Status == "unverified_invalid_response"), checks);

    private async Task<FacultyRequestCoverage> GetRequestCoverage(FacultyAssistantAnalysisRequest request,
        IReadOnlyList<FacultyAssistantAnswerItem> items, string generatorModel,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return EmptyRequestCoverage();
        try
        {
            GeneratedFacultyRequestCoverage generatedCoverage = await requestCoverageVerifier.VerifyAsync(
                request.Mode, request.Language, request.Query, items, cancellationToken);
            return ValidateRequestCoverage(generatedCoverage, items.Count, generatorModel);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsCoverageFailure(exception))
        {
            return UnavailableRequestCoverage();
        }
    }

    private static void ValidateGenerated(GeneratedFacultyAssistantAnswer generated, string mode,
        IReadOnlyDictionary<string, FacultyAssistantEvidence> evidence)
    {
        if (generated?.Items is null || !HasBoundedText(generated.Model, 200) ||
            generated.PromptVersion != FacultyAssistantPrompt.Version || generated.Items.Count > 6 ||
            generated.Items.Any(item => !ValidItem(item, mode, evidence)))
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }

    private static bool ValidItem(GeneratedFacultyAssistantItem? item, string mode,
        IReadOnlyDictionary<string, FacultyAssistantEvidence> evidence) => item is not null &&
        item.EvidenceIds is not null && FacultyAssistantPrompt.IsKindAllowed(mode, item.Kind) &&
        HasBoundedText(item.Basis, 1200) && !FacultyAssistantPrompt.IsWholeFieldPlaceholder(item.Basis) &&
        (item.Response is null || item.Response.Length <= 1200) &&
        !FacultyAssistantPrompt.IsWholeFieldPlaceholder(item.Response) &&
        item.EvidenceIds.Count is >= 1 and <= 2 &&
        item.EvidenceIds.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 300 && evidence.ContainsKey(id)) &&
        item.EvidenceIds.Distinct(StringComparer.Ordinal).Count() == item.EvidenceIds.Count &&
        (item.Kind == "source_observation" ? item.Response is null : HasBoundedText(item.Response, 1200));

    private static void ValidateRepair(GeneratedFacultyAssistantRepair repair,
        IReadOnlyList<GeneratedFacultyAssistantRepairCandidate> targets, string mode,
        IReadOnlyDictionary<string, FacultyAssistantEvidence> evidence)
    {
        if (repair is null || repair.Status is not ("completed" or "output_limit" or "invalid_response") ||
            repair.Items is null || !HasBoundedText(repair.Model, 200) ||
            repair.PromptVersion != FacultyAssistantRepairPrompt.Version || repair.Attempt is null ||
            repair.Attempt.AttemptId == Guid.Empty || repair.Attempt.Ordinal != 1 ||
            repair.Attempt.ThinkingLevel != "medium" || repair.Attempt.Model != repair.Model ||
            repair.Attempt.TotalTokenCount <= 0 || repair.Attempt.EstimatedUsd <= 0 ||
            !HasBoundedText(repair.Attempt.PricingVersion, 100) || !repair.Attempt.UsagePersisted ||
            repair.Status == "completed" && repair.Attempt.Outcome != "Success" ||
            repair.Status == "output_limit" && repair.Attempt.Outcome != "OutputLimit" ||
            repair.Status != "completed" && repair.Items.Count != 0 || repair.Items.Count > targets.Count ||
            repair.Items.Select(value => value.CandidateId).Distinct(StringComparer.Ordinal).Count() != repair.Items.Count ||
            repair.Items.Any(value => !targets.Any(target => target.CandidateId == value.CandidateId) ||
                !ValidItem(value.Item, mode, evidence)))
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }

    private static FacultyAssistantRepair ToRepairAudit(GeneratedFacultyAssistantRepair repair,
        int requestedCandidates, int retainedItems) => new(repair.Status, requestedCandidates,
        repair.Items.Count, retainedItems, repair.Model, repair.PromptVersion,
        new(repair.Attempt.Ordinal, repair.Attempt.ThinkingLevel, repair.Attempt.Outcome,
            repair.Attempt.Model, repair.Attempt.TotalTokenCount, repair.Attempt.EstimatedUsd,
            repair.Attempt.PricingVersion, repair.Attempt.UsagePersisted)
        { AttemptId = repair.Attempt.AttemptId });

    private static FacultyAssistantRepair NotRunRepair() =>
        new("not_run", 0, 0, 0, string.Empty, string.Empty, null);

    private static FacultyAssistantAnswerItem ToAnswer(GeneratedFacultyAssistantItem item,
        IReadOnlyDictionary<string, FacultyAssistantEvidence> evidence, string candidateId) => new(item.Kind,
        item.Basis, item.Response, item.EvidenceIds.Select(id =>
            new FacultyAssistantCitation(id, evidence[id].ExactText)).ToList()) { CandidateId = candidateId };

    private static FacultyAssistantAnalysisReport EmptyReport(FacultyAssistantAnalysisRequest request,
        GeneratedFacultyAssistantAnswer generated, FacultyAssistantGeneration generation) =>
        new(request.Mode, request.Language, [], generated.Model, generated.PromptVersion,
            new("not_run", string.Empty, string.Empty, false,
                "No generated items required verification."), "no_supported_items",
            new(0, 0, 0, 0, 0, 0, false))
        {
            RequestCoverage = EmptyRequestCoverage(), Generation = generation,
            SourceChecks = new(0, 0, 0, 0, 0, 0, 0, []), Repair = NotRunRepair()
        };

    private static FacultyAssistantGeneration ValidateGeneration(GeneratedFacultyAssistantAnswer generated)
    {
        IReadOnlyList<GeneratedFacultyAssistantGenerationAttempt>? attempts = generated.GenerationAttempts;
        if (attempts is null || attempts.Count is < 1 or > 2)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        for (int index = 0; index < attempts.Count; index++)
        {
            GeneratedFacultyAssistantGenerationAttempt attempt = attempts[index];
            if (attempt is null || attempt.Ordinal != index + 1 ||
                attempt.ThinkingLevel is not ("low" or "medium" or "high") ||
                attempt.Outcome is not ("Success" or "OutputLimit") ||
                !string.Equals(attempt.Model, generated.Model, StringComparison.Ordinal) ||
                attempt.TotalTokenCount <= 0 || attempt.EstimatedUsd <= 0 ||
                !HasBoundedText(attempt.PricingVersion, 100) || !attempt.UsagePersisted)
                throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        }
        bool recovered = attempts.Count == 2;
        if (attempts[^1].Outcome != "Success" || recovered &&
            (attempts[0].Outcome != "OutputLimit" || attempts[0].ThinkingLevel != "high" ||
             attempts[1].ThinkingLevel != "medium") || !recovered && attempts[0].Outcome != "Success")
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        return new(attempts.Select(value => new FacultyAssistantGenerationAttempt(value.Ordinal,
            value.ThinkingLevel, value.Outcome, value.Model, value.TotalTokenCount,
            value.EstimatedUsd, value.PricingVersion, value.UsagePersisted)
            { AttemptId = value.AttemptId }).ToList(), recovered);
    }

    private static FacultyRequestCoverage ValidateRequestCoverage(
        GeneratedFacultyRequestCoverage generated, int itemCount, string generatorModel)
    {
        if (generated is null || !HasBoundedText(generated.Model, 200) ||
            generated.PromptVersion != FacultyRequestCoveragePrompt.Version ||
            generated.Status is not ("fulfilled" or "partial" or "unanswered") ||
            generated.Requirements is null || generated.Requirements.Count is < 1 or > 8)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        for (int index = 0; index < generated.Requirements.Count; index++)
        {
            GeneratedFacultyRequestCoverageRequirement requirement = generated.Requirements[index];
            if (requirement is null || requirement.RequirementId != $"requirement-{index + 1}" ||
                !HasBoundedText(requirement.Requirement, 500) ||
                requirement.Status is not ("fulfilled" or "partial" or "unanswered") ||
                requirement.ItemIndexes is null || !HasBoundedText(requirement.Reason, 500) ||
                requirement.ItemIndexes.Any(itemIndex => itemIndex < 1 || itemIndex > itemCount) ||
                requirement.ItemIndexes.Distinct().Count() != requirement.ItemIndexes.Count ||
                requirement.Status == "unanswered" && requirement.ItemIndexes.Count != 0 ||
                requirement.Status != "unanswered" && requirement.ItemIndexes.Count == 0)
                throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        }
        string expectedStatus = generated.Requirements.All(value => value.Status == "fulfilled") ? "fulfilled" :
            generated.Requirements.All(value => value.Status == "unanswered") ? "unanswered" : "partial";
        if (generated.Status != expectedStatus)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
        bool sameFamily = Family(generatorModel).Equals(Family(generated.Model), StringComparison.OrdinalIgnoreCase);
        return new(generated.Status, generated.Requirements.Select(value => new FacultyRequestCoverageRequirement(
                value.RequirementId, value.Requirement, value.Status, value.ItemIndexes, value.Reason)).ToList(),
            generated.Model, generated.PromptVersion, sameFamily,
            "Automatic request-fulfillment checking assessed only retained source-supported items. " +
            "It may use the same model family and does not prove scientific correctness or exhaustive recall.");
    }

    private static FacultyRequestCoverage EmptyRequestCoverage() => new("unanswered",
        [new("requirement-1", "Original faculty request", "unanswered", [],
            "No source-supported items were retained to answer the request.")],
        string.Empty, string.Empty, false,
        "No request-fulfillment model call was made because no source-supported items were retained.");

    private static FacultyRequestCoverage UnavailableRequestCoverage() => new("unavailable", [],
        string.Empty, string.Empty, false,
        "Automatic request-fulfillment checking was unavailable. Retained items remain source-checked, " +
        "but request completion is unknown.");

    private static bool IsCoverageFailure(Exception exception) => exception is
        AnalysisUnavailableException or AnalysisInputTooLargeException or InvalidAnalysisException or
        HttpRequestException or OperationCanceledException;

    private static string Family(string model)
    {
        int separator = model.IndexOfAny(['-', ':']);
        return separator < 0 ? model : model[..separator];
    }

    private static bool HasBoundedText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;
}
