using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Products.FacultyAssistant;

namespace ResearcherAnalysisService.Tests;

public sealed class FacultyAssistantServiceClientTests
{
    private const string EvidenceId = "work:1:snapshot:2:span:3";
    private const string Model = "gemini-3.8-flash";

    [Fact]
    public void Options_DefaultProvidesBoundedMultiCallFacultyWindow()
    {
        FacultyAssistantOptions options = new();
        List<ValidationResult> failures = [];

        Assert.True(Validator.TryValidateObject(options,
            new ValidationContext(options), failures, true));
        Assert.Equal(1800, options.RequestTimeoutSeconds);

        options.RequestTimeoutSeconds = 1801;
        failures.Clear();
        Assert.False(Validator.TryValidateObject(options,
            new ValidationContext(options), failures, true));
    }

    [Fact]
    public async Task AnswerAsync_EmptyGeneration_CreatesDeterministicCoverageAndSkipsLaterCalls()
    {
        StubFacultyVerifier verifier = NeverVerifier();
        StubRepairGenerator repair = NeverRepair();
        StubCoverageVerifier coverage = NeverCoverage();
        FacultyAssistantServiceClient client = Client(
            Generator([]), verifier, repair, coverage);

        FacultyAssistantAnalysisReport report = await client.AnswerAsync(Request(), default);

        Assert.Equal("no_supported_items", report.Outcome);
        Assert.Empty(report.Items);
        Assert.Equal("unanswered", report.RequestCoverage!.Status);
        Assert.Empty(report.RequestCoverage.Model);
        Assert.Equal(0, verifier.Calls);
        Assert.Equal(0, repair.Calls);
        Assert.Equal(0, coverage.Calls);
    }

    [Fact]
    public async Task AnswerAsync_SupportedItemAndFulfilledCoverage_ReturnsCompletedReport()
    {
        FacultyAssistantServiceClient client = Client(
            Generator([Item(EvidenceId)]), SupportedVerifier(), NeverRepair(), FulfilledCoverage());

        FacultyAssistantAnalysisReport report = await client.AnswerAsync(Request(), default);

        Assert.Equal("completed", report.Outcome);
        FacultyAssistantAnswerItem item = Assert.Single(report.Items);
        Assert.Equal("assistant-1", item.CandidateId);
        Assert.Equal(EvidenceId, Assert.Single(item.Citations).EvidenceId);
        Assert.Equal("fulfilled", report.RequestCoverage!.Status);
    }

    [Fact]
    public async Task AnswerAsync_RequestCoverageUnavailable_PreservesSupportedPartialReport()
    {
        StubCoverageVerifier unavailable = new((_, _, _, _, _) =>
            throw new AnalysisUnavailableException("Synthetic coverage outage."));
        FacultyAssistantServiceClient client = Client(
            Generator([Item(EvidenceId)]), SupportedVerifier(), NeverRepair(), unavailable);

        FacultyAssistantAnalysisReport report = await client.AnswerAsync(Request(), default);

        Assert.Equal("partial", report.Outcome);
        Assert.Single(report.Items);
        Assert.Equal("unavailable", report.RequestCoverage!.Status);
    }

    [Fact]
    public async Task AnswerAsync_UnknownEvidenceId_FailsBeforeVerification()
    {
        StubFacultyVerifier verifier = NeverVerifier();
        FacultyAssistantServiceClient client = Client(
            Generator([Item("unknown-evidence")]), verifier, NeverRepair(), NeverCoverage());

        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            client.AnswerAsync(Request(), default));

        Assert.Equal(AnalysisFailure.InvalidEvidence, exception.Reason);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task AnswerAsync_InvalidGenerationBudgetMetadata_FailsClosed()
    {
        GeneratedFacultyAssistantGenerationAttempt invalid = Attempt() with { EstimatedUsd = 0 };
        StubFacultyGenerator generator = new((_, _) => Task.FromResult(
            new GeneratedFacultyAssistantAnswer([], Model, FacultyAssistantPrompt.Version, [invalid])));

        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Client(generator, NeverVerifier(), NeverRepair(), NeverCoverage())
                .AnswerAsync(Request(), default));

        Assert.Equal(AnalysisFailure.InvalidEvidence, exception.Reason);
    }

    [Fact]
    public async Task AnswerAsync_DuplicateAttemptAcrossGenerationAndVerification_IsRejected()
    {
        Guid duplicate = Guid.NewGuid();
        StubFacultyGenerator generator = Generator([Item(EvidenceId)], duplicate);
        StubFacultyVerifier verifier = new((_, _, finding, _, _) => Task.FromResult(
            new GeneratedFacultyAssistantSourceCheck(duplicate, "checked",
                new(finding.FindingId, "supported", "Supported."), Model,
                FacultyAssistantVerificationPrompt.Version, "Supported.")));

        await Assert.ThrowsAsync<JsonException>(() =>
            Client(generator, verifier, NeverRepair(), FulfilledCoverage())
                .AnswerAsync(Request(), default));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("out-of-range")]
    [InlineData("aggregate")]
    public async Task AnswerAsync_InvalidRequestCoverage_IsQuarantinedAsUnavailable(string shape)
    {
        IReadOnlyList<int> itemIndexes = shape == "duplicate" ? [1, 1] :
            shape == "out-of-range" ? [2] : [];
        string requirementStatus = shape == "aggregate" ? "unanswered" : "fulfilled";
        StubCoverageVerifier coverage = new((_, _, _, _, _) => Task.FromResult(
            new GeneratedFacultyRequestCoverage("fulfilled",
                [new("requirement-1", "Explain", requirementStatus, itemIndexes, "Synthetic.")],
                Model, FacultyRequestCoveragePrompt.Version)));

        FacultyAssistantAnalysisReport report = await
            Client(Generator([Item(EvidenceId)]), SupportedVerifier(), NeverRepair(), coverage)
                .AnswerAsync(Request(), default);

        Assert.Equal("partial", report.Outcome);
        Assert.Single(report.Items);
        Assert.Equal("unavailable", report.RequestCoverage!.Status);
        Assert.Empty(report.RequestCoverage.Requirements);
    }

    [Fact]
    public async Task AnswerAsync_RepairForUnknownCandidate_IsRejected()
    {
        StubFacultyVerifier verifier = new((_, _, finding, _, _) => Task.FromResult(
            new GeneratedFacultyAssistantSourceCheck(Guid.NewGuid(), "checked",
                new(finding.FindingId, "unsupported", "Unsupported."), Model,
                FacultyAssistantVerificationPrompt.Version, "Unsupported.")));
        StubRepairGenerator repair = new((_, _, _, _) => Task.FromResult(
            new GeneratedFacultyAssistantRepair("completed",
                [new("assistant-9", Item(EvidenceId))], Model, FacultyAssistantRepairPrompt.Version,
                Attempt("medium"))));

        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Client(Generator([Item(EvidenceId)]), verifier, repair, NeverCoverage())
                .AnswerAsync(Request(), default));

        Assert.Equal(AnalysisFailure.InvalidEvidence, exception.Reason);
    }

    [Fact]
    public async Task AnswerAsync_CallerCancellation_PropagatesToGenerator()
    {
        StubFacultyGenerator generator = new(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(generator, NeverVerifier(), NeverRepair(), NeverCoverage())
                .AnswerAsync(Request(), cancellation.Token));
    }

    private static FacultyAssistantServiceClient Client(StubFacultyGenerator generator,
        StubFacultyVerifier verifier, StubRepairGenerator repair,
        StubCoverageVerifier coverage, FacultyAssistantOptions? options = null) => new(
        new ResearcherAnalysisService.Analysis.FacultyAssistant(generator, verifier, repair, coverage),
        Options.Create(options ?? new FacultyAssistantOptions()));

    private static StubFacultyGenerator Generator(
        IReadOnlyList<GeneratedFacultyAssistantItem> items, Guid? attemptId = null) => new((_, _) =>
        Task.FromResult(new GeneratedFacultyAssistantAnswer(items, Model, FacultyAssistantPrompt.Version,
            [Attempt("high", attemptId)])));

    private static GeneratedFacultyAssistantItem Item(string evidenceId) =>
        new("source_observation", "Source-supported observation.", null, [evidenceId]);

    private static GeneratedFacultyAssistantGenerationAttempt Attempt(string thinkingLevel = "high",
        Guid? attemptId = null) =>
        new(1, thinkingLevel, "Success", Model, 120, 0.00015m, "synthetic-pricing-v1", true)
        {
            AttemptId = attemptId ?? Guid.NewGuid()
        };

    private static StubFacultyVerifier SupportedVerifier() => new((_, _, finding, _, _) => Task.FromResult(
        new GeneratedFacultyAssistantSourceCheck(Guid.NewGuid(), "checked",
            new(finding.FindingId, "supported", "Supported."), Model,
            FacultyAssistantVerificationPrompt.Version, "Supported.")));

    private static StubFacultyVerifier NeverVerifier() => new((_, _, _, _, _) =>
        throw new InvalidOperationException("Verifier must not run."));

    private static StubRepairGenerator NeverRepair() => new((_, _, _, _) =>
        throw new InvalidOperationException("Repair must not run."));

    private static StubCoverageVerifier NeverCoverage() => new((_, _, _, _, _) =>
        throw new InvalidOperationException("Coverage verifier must not run."));

    private static StubCoverageVerifier FulfilledCoverage() => new((_, _, _, _, _) => Task.FromResult(
        new GeneratedFacultyRequestCoverage("fulfilled",
            [new("requirement-1", "Explain the evidence", "fulfilled", [1], "Covered.")],
            Model, FacultyRequestCoveragePrompt.Version)));

    private static FacultyAssistantAnalysisRequest Request() => new()
    {
        Mode = "ExploreOwnRecord",
        Language = "en",
        Query = "Explain the evidence.",
        EvidenceCatalogHash = new string('a', 64),
        Evidence = [new(EvidenceId, 1, 3, "src-1", 1, 0, 7, "Source.", "pdf", false)]
    };

    private sealed class StubFacultyGenerator(
        Func<FacultyAssistantAnalysisRequest, CancellationToken,
            Task<GeneratedFacultyAssistantAnswer>> generate) : IFacultyAssistantGenerator
    {
        public Task<GeneratedFacultyAssistantAnswer> GenerateAsync(
            FacultyAssistantAnalysisRequest request, CancellationToken cancellationToken) =>
            generate(request, cancellationToken);
    }

    private sealed class StubFacultyVerifier(
        Func<string, string, GeneratedArticleReviewFinding, IReadOnlyList<ArticleSourceSpan>,
            CancellationToken, Task<GeneratedFacultyAssistantSourceCheck>> verify) : IFacultyAssistantVerifier
    {
        public int Calls { get; private set; }

        public Task<GeneratedFacultyAssistantSourceCheck> VerifyAsync(string role, string language,
            GeneratedArticleReviewFinding finding, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            Calls++;
            return verify(role, language, finding, sourceSpans, cancellationToken);
        }
    }

    private sealed class StubRepairGenerator(
        Func<FacultyAssistantAnalysisRequest,
            IReadOnlyList<GeneratedFacultyAssistantRepairCandidate>,
            IReadOnlyList<GeneratedFacultyAssistantItem>, CancellationToken,
            Task<GeneratedFacultyAssistantRepair>> repair) : IFacultyAssistantRepairGenerator
    {
        public int Calls { get; private set; }

        public Task<GeneratedFacultyAssistantRepair> RepairAsync(FacultyAssistantAnalysisRequest request,
            IReadOnlyList<GeneratedFacultyAssistantRepairCandidate> omittedCandidates,
            IReadOnlyList<GeneratedFacultyAssistantItem> supportedItems,
            CancellationToken cancellationToken)
        {
            Calls++;
            return repair(request, omittedCandidates, supportedItems, cancellationToken);
        }
    }

    private sealed class StubCoverageVerifier(
        Func<string, string, string, IReadOnlyList<FacultyAssistantAnswerItem>, CancellationToken,
            Task<GeneratedFacultyRequestCoverage>> verify) : IFacultyRequestCoverageVerifier
    {
        public int Calls { get; private set; }

        public Task<GeneratedFacultyRequestCoverage> VerifyAsync(string mode, string language,
            string query, IReadOnlyList<FacultyAssistantAnswerItem> retainedItems,
            CancellationToken cancellationToken)
        {
            Calls++;
            return verify(mode, language, query, retainedItems, cancellationToken);
        }
    }
}
