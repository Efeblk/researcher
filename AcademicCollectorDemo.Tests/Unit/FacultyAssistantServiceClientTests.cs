using System.Net;
using System.Net.Http.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using AcademicCollectorDemo.Modules.AcademicPerformance.FacultyAssistant;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class FacultyAssistantServiceClientTests
{
    [Fact]
    public void Options_DefaultProvidesBoundedMultiCallFacultyWindow()
    {
        FacultyAssistantOptions options = new();
        List<System.ComponentModel.DataAnnotations.ValidationResult> failures = [];

        bool valid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options,
            new System.ComponentModel.DataAnnotations.ValidationContext(options), failures, true);

        Assert.True(valid);
        Assert.Equal(1800, options.RequestTimeoutSeconds);
        options.RequestTimeoutSeconds = 1801;
        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options,
            new System.ComponentModel.DataAnnotations.ValidationContext(options), failures, true));
    }

    [Fact]
    public async Task AnswerAsync_ConfiguredAccessKeyIsSentAndEmptyValidatedReportIsAccepted()
    {
        bool sent = false;
        using StubHandler handler = new(request =>
        {
            sent = request.Headers.TryGetValues("X-Analysis-Key", out var values) && values.Single() == "service-key";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new FacultyAssistantAnalysisReport("ExploreOwnRecord", "en", [],
                    "gemini-3.8-flash", "faculty-evidence-assistant-v10", new("not_run", "", "", false,
                        "No generated items required verification."), "no_supported_items",
                    new(0, 0, 0, 0, 0, 0, false))
                {
                    RequestCoverage = new("unanswered",
                        [new("requirement-1", "Original request", "unanswered", [], "No retained items.")],
                        "", "", false, "No request-fulfillment model call was made."),
                    Generation = Generation(), SourceChecks = EmptySourceChecks(), Repair = NotRunRepair()
                })
            });
        });
        FacultyAssistantServiceClient client = new(new HttpClient(handler)
        { BaseAddress = new Uri("http://127.0.0.1/") }, Options.Create(new AnalysisServiceOptions { ApiKey = "service-key" }));

        FacultyAssistantAnalysisReport report = await client.AnswerAsync(Request(), default);

        Assert.True(sent);
        Assert.Equal("no_supported_items", report.Outcome);
        Assert.NotNull(report.Coverage);
        Assert.Equal("unanswered", report.RequestCoverage!.Status);
    }

    [Fact]
    public async Task AnswerAsync_CurrentEmptyReportWithDeterministicRequestCoverageIsAccepted()
    {
        FacultyAssistantAnalysisReport expected = new("ExploreOwnRecord", "en", [],
            "gemini-3.8-flash", "faculty-evidence-assistant-v10", new("not_run", "", "", false,
                "No generated items required verification."), "no_supported_items",
            new(0, 0, 0, 0, 0, 0, false))
        {
            RequestCoverage = new("unanswered",
                [new("requirement-1", "Original request", "unanswered", [], "No retained items.")],
                "", "", false, "No request-fulfillment model call was made."),
            Generation = Generation(), SourceChecks = EmptySourceChecks(), Repair = NotRunRepair()
        };

        FacultyAssistantAnalysisReport report = await ClientFor(expected).AnswerAsync(Request(), default);

        Assert.Equal("unanswered", report.RequestCoverage!.Status);
        Assert.Empty(report.RequestCoverage.Model);
    }

    [Theory]
    [InlineData("faculty-evidence-assistant-v5")]
    [InlineData("faculty-evidence-assistant-v4")]
    [InlineData("downgraded-or-unparseable")]
    public async Task AnswerAsync_MissingRequestCoverageIsRejectedRegardlessOfPromptVersion(
        string promptVersion)
    {
        FacultyAssistantAnalysisReport report = new("ExploreOwnRecord", "en", [],
            "gemini-3.8-flash", promptVersion, new("not_run", "", "", false,
                "No candidates."), "no_supported_items", new(0, 0, 0, 0, 0, 0, false))
        {
            Generation = Generation()
        };

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => ClientFor(report)
            .AnswerAsync(Request(), default));
    }

    [Fact]
    public async Task AnswerAsync_UnavailableRequestCoveragePreservesUsablePartialReport()
    {
        FacultyAssistantAnalysisReport expected = CurrentReport("partial") with
        {
            RequestCoverage = new("unavailable", [], "", "", false,
                "Automatic request-fulfillment checking was unavailable.")
        };

        FacultyAssistantAnalysisReport report = await ClientFor(expected).AnswerAsync(Request(), default);

        Assert.Single(report.Items);
        Assert.Equal("partial", report.Outcome);
        Assert.Equal("unavailable", report.RequestCoverage!.Status);
    }

    [Fact]
    public async Task AnswerAsync_MissingGenerationMetadataIsRejected()
    {
        FacultyAssistantAnalysisReport report = CurrentReport("completed") with
        {
            Generation = null,
            RequestCoverage = new("fulfilled",
                [new("requirement-1", "Explain", "fulfilled", [1], "Covered.")],
                "gemini-3.8-flash", "faculty-request-coverage-v1", true,
                "Automatic request-fulfillment coverage.")
        };

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => ClientFor(report)
            .AnswerAsync(Request(), default));
    }

    [Theory]
    [InlineData("source-checks")]
    [InlineData("repair")]
    public async Task AnswerAsync_MissingFreshAuditMetadataIsRejected(string field)
    {
        FacultyAssistantAnalysisReport report = CurrentReport("completed") with
        {
            SourceChecks = field == "source-checks" ? null : CurrentReport("completed").SourceChecks,
            Repair = field == "repair" ? null : NotRunRepair(),
            RequestCoverage = new("fulfilled",
                [new("requirement-1", "Explain", "fulfilled", [1], "Covered.")],
                "gemini-3.8-flash", "faculty-request-coverage-v1", true, "Coverage.")
        };

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => ClientFor(report)
            .AnswerAsync(Request(), default));
    }

    [Theory]
    [InlineData("duplicate-attempt")]
    [InlineData("wrong-final-slot")]
    [InlineData("wrong-repair-slot")]
    public async Task AnswerAsync_InvalidFreshAuditLinkageIsRejected(string shape)
    {
        FacultyAssistantAnalysisReport report = CurrentReport("completed") with
        {
            RequestCoverage = new("fulfilled",
                [new("requirement-1", "Explain", "fulfilled", [1], "Covered.")],
                "gemini-3.8-flash", "faculty-request-coverage-v1", true, "Coverage.")
        };
        if (shape == "duplicate-attempt")
        {
            FacultyAssistantSourceCheck check = report.SourceChecks!.Checks.Single() with
                { AttemptId = report.Generation!.Attempts.Single().AttemptId };
            report = report with { SourceChecks = report.SourceChecks with { Checks = [check] } };
        }
        else if (shape == "wrong-final-slot")
        {
            FacultyAssistantSourceCheck check = report.SourceChecks!.Checks.Single() with
                { Status = "uncertain" };
            report = report with { SourceChecks = new(1, 1, 0, 0, 1, 0, 0, [check]) };
        }
        else
        {
            FacultyAssistantSourceCheck original = report.SourceChecks!.Checks.Single() with
                { Status = "unsupported" };
            FacultyAssistantSourceCheck repair = original with
            {
                AttemptId = Guid.NewGuid(), CandidateId = "assistant-9", Origin = "repair", Status = "supported"
            };
            report = report with
            {
                SourceChecks = new(2, 2, 1, 1, 0, 0, 0, [original, repair]),
                Repair = new("completed", 1, 1, 1, "gemini-3.8-flash",
                    "faculty-evidence-assistant-repair-v2",
                    new(1, "medium", "Success", "gemini-3.8-flash", 120, 0.00015m,
                        "pricing", true) { AttemptId = Guid.NewGuid() }),
                Coverage = report.Coverage! with { RepairCandidateItems = 1 }
            };
        }

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => ClientFor(report)
            .AnswerAsync(Request(), default));
    }

    [Fact]
    public async Task AnswerAsync_FulfilledRequestCoverageAcceptsCompletedReport()
    {
        FacultyAssistantAnalysisReport expected = CurrentReport("completed") with
        {
            RequestCoverage = new("fulfilled",
                [new("requirement-1", "Explain the evidence", "fulfilled", [1],
                    "The retained item explains the evidence.")],
                "gemini-3.8-flash", "faculty-request-coverage-v1", true,
                "Automatic request-fulfillment coverage.")
        };

        FacultyAssistantAnalysisReport report = await ClientFor(expected).AnswerAsync(Request(), default);

        Assert.Equal("completed", report.Outcome);
        Assert.Equal("fulfilled", report.RequestCoverage!.Status);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("out-of-range")]
    [InlineData("aggregate")]
    public async Task AnswerAsync_InvalidRequestCoverageIsRejected(string shape)
    {
        IReadOnlyList<FacultyRequestCoverageRequirement> requirements = shape switch
        {
            "duplicate" => [new("requirement-1", "Answer", "fulfilled", [1, 1], "Duplicate.")],
            "out-of-range" => [new("requirement-1", "Answer", "fulfilled", [2], "Invalid index.")],
            _ => [new("requirement-1", "Answer", "unanswered", [], "Missing.")]
        };
        FacultyAssistantAnalysisReport report = CurrentReport("completed") with
        {
            RequestCoverage = new("fulfilled", requirements, "gemini-3.8-flash",
                "faculty-request-coverage-v1", true, "Automatic coverage.")
        };

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => ClientFor(report)
            .AnswerAsync(Request(), default));
    }

    [Fact]
    public async Task AnswerAsync_InvalidPartialCoverageContractsAreRejected()
    {
        FacultyAssistantAnswerItem item = new("source_observation", "Source.", null,
            [new("work:1:snapshot:2:span:3", "Source.")]);
        FacultyAssistantVerification verification = new("automatically_checked", "gemini-3.8-flash",
            "faculty-verify-v1", true, "Synthetic verification.");
        FacultyAssistantAnalysisReport[] invalid =
        [
            new("ExploreOwnRecord", "en", [item], "gemini-3.8-flash", "faculty-v3",
                verification, "partial", null),
            new("ExploreOwnRecord", "en", [item], "gemini-3.8-flash", "faculty-v3",
                verification, "partial", new(2, 2, 2, 0, 0, 0, false)),
            new("ExploreOwnRecord", "en", [item], "gemini-3.8-flash", "faculty-v3",
                verification, "completed", new(2, 2, 1, 0, 1, 1, true)),
            new("ExploreOwnRecord", "en", [], "gemini-3.8-flash", "faculty-v3",
                new("not_run", "gemini-3.8-flash", "faculty-verify-v1", true, "Invalid."),
                "no_supported_items", new(0, 0, 0, 0, 0, 0, false))
        ];

        foreach (FacultyAssistantAnalysisReport report in invalid)
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => ClientFor(report)
                .AnswerAsync(Request(), default));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"candidateItems\":0,\"automaticallyCheckedItems\":0,\"supportedItems\":0,\"unsupportedItems\":0,\"omittedItems\":0,\"isPartial\":false}")]
    public async Task AnswerAsync_IncompleteCoverageJsonIsRejected(string coverageJson)
    {
        string json = $$"""
            {"mode":"ExploreOwnRecord","language":"en","items":[],"model":"gemini-3.8-flash",
             "promptVersion":"faculty-v3","verification":{"status":"not_run","model":"","promptVersion":"",
             "usesSameModelFamily":false,"limitation":"No candidates."},"outcome":"no_supported_items",
             "coverage":{{coverageJson}}}
            """;

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => ClientForJson(json)
            .AnswerAsync(Request(), default));
    }

    private static FacultyAssistantAnalysisRequest Request() => new()
    {
        Mode = "ExploreOwnRecord", Language = "en", Query = "sample", EvidenceCatalogHash = new string('a', 64),
        Evidence = [new("work:1:snapshot:2:span:3", 1, 3, "src-1", 1, 0, 7, "Source.", "pdf", false)]
    };

    private static FacultyAssistantAnalysisReport CurrentReport(string outcome)
    {
        FacultyAssistantAnswerItem item = new("source_observation", "Source.", null,
            [new("work:1:snapshot:2:span:3", "Source.")]) { CandidateId = "assistant-1" };
        return new FacultyAssistantAnalysisReport("ExploreOwnRecord", "en", [item], "gemini-3.8-flash",
            "faculty-evidence-assistant-v10", new("automatically_checked", "gemini-3.8-flash",
                "faculty-evidence-assistant-verification-v7", true, "Automatic source verification."),
            outcome, new(1, 1, 1, 0, 0, 0, false))
        {
            Generation = Generation(),
            SourceChecks = new(1, 1, 1, 0, 0, 0, 0,
                [new(Guid.NewGuid(), "assistant-1", "initial", "supported", "Supported.",
                    "gemini-3.8-flash", "faculty-evidence-assistant-verification-v7",
                    ["work:1:snapshot:2:span:3"])]),
            Repair = NotRunRepair()
        };
    }

    private static FacultyAssistantGeneration Generation() => new(
        [new(1, "high", "Success", "gemini-3.8-flash", 120, 0.000150m,
            "gemini-3.8-flash-standard-through-2026-12-31", true) { AttemptId = Guid.NewGuid() }], false);

    private static FacultyAssistantSourceChecks EmptySourceChecks() => new(0, 0, 0, 0, 0, 0, 0, []);
    private static FacultyAssistantRepair NotRunRepair() => new("not_run", 0, 0, 0, "", "", null);

    private static FacultyAssistantServiceClient ClientFor(FacultyAssistantAnalysisReport report)
    {
        StubHandler handler = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = JsonContent.Create(report) }));
        return new FacultyAssistantServiceClient(new HttpClient(handler)
            { BaseAddress = new Uri("http://127.0.0.1/") }, Options.Create(new AnalysisServiceOptions()));
    }

    private static FacultyAssistantServiceClient ClientForJson(string json)
    {
        StubHandler handler = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") }));
        return new FacultyAssistantServiceClient(new HttpClient(handler)
            { BaseAddress = new Uri("http://127.0.0.1/") }, Options.Create(new AnalysisServiceOptions()));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
