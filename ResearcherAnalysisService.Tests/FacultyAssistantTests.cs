using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.Gemini;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class FacultyAssistantTests
{
    [Fact]
    public async Task AnswerAsync_QuestionUsesRoleKindAndSeparateVerifier()
    {
        StubGenerator generator = new([new("review_question", "The source reports 40 participants.",
            "Could the class discuss the reported sample?", ["work:1:snapshot:2:span:3"])]);
        RecordingVerifier verifier = new("supported");
        FacultyAssistant assistant = Assistant(generator, verifier);

        FacultyAssistantAnalysisReport result = await assistant.AnswerAsync(Request("TeachingHelp"), default);

        GeneratedArticleReviewFinding finding = Assert.Single(verifier.Findings!);
        Assert.Equal("teaching", verifier.Role);
        Assert.Equal("review_question", finding.Kind);
        Assert.Equal("Could the class discuss the reported sample?", finding.Suggestion);
        Assert.Equal("completed", result.Outcome);
        Assert.Equal("automatically_checked", result.Verification.Status);
        Assert.Equal(new FacultyAssistantCoverage(1, 1, 1, 0, 0, 0, false), result.Coverage);
        Assert.Equal("fulfilled", result.RequestCoverage!.Status);
    }

    [Fact]
    public async Task AnswerAsync_AllRejectedCompletesWithCheckedEmptyReport()
    {
        StubGenerator generator = new([new("source_observation", "The source reports 40 participants.",
            null, ["work:1:snapshot:2:span:3"])]);
        RecordingVerifier verifier = new("unsupported");

        FacultyAssistantAnalysisReport result = await Assistant(generator, verifier)
            .AnswerAsync(Request("OwnPaperMethods"), default);

        GeneratedArticleReviewFinding finding = Assert.Single(verifier.Findings!);
        Assert.Equal("method", verifier.Role);
        Assert.Equal("source_observation", finding.Kind);
        Assert.Null(finding.Suggestion);
        Assert.Empty(result.Items);
        Assert.Equal("no_supported_items", result.Outcome);
        Assert.Equal("automatically_checked", result.Verification.Status);
        Assert.Equal(new FacultyAssistantCoverage(1, 1, 0, 1, 0, 1, true), result.Coverage);
    }

    [Fact]
    public async Task AnswerAsync_EmptyItemsSkipsVerifier()
    {
        RecordingVerifier verifier = new("supported");
        RecordingRequestCoverageVerifier requestCoverage = new();
        FacultyAssistantAnalysisReport result = await Assistant(new StubGenerator([]), verifier, requestCoverage)
            .AnswerAsync(Request("ExploreOwnRecord"), default);
        Assert.Equal("no_supported_items", result.Outcome);
        Assert.Equal("not_run", result.Verification.Status);
        Assert.Equal(new FacultyAssistantCoverage(0, 0, 0, 0, 0, 0, false), result.Coverage);
        Assert.Equal("unanswered", result.RequestCoverage!.Status);
        Assert.Empty(result.RequestCoverage.Model);
        Assert.Null(verifier.Findings);
        Assert.Equal(0, requestCoverage.Calls);
    }

    [Theory]
    [InlineData("...", "A complete response.")]
    [InlineData("\u2026", "A complete response.")]
    [InlineData("TODO", "A complete response.")]
    [InlineData("A complete basis.", "TBD")]
    public async Task AnswerAsync_WholeFieldPlaceholderFailsBeforeVerifier(string basis, string response)
    {
        RecordingVerifier verifier = new("supported");
        await Assert.ThrowsAsync<InvalidAnalysisException>(() => Assistant(new StubGenerator(
            [new("review_question", basis, response, ["work:1:snapshot:2:span:3"])]), verifier)
            .AnswerAsync(Request("TeachingHelp"), default));
        Assert.Null(verifier.Findings);
    }

    [Fact]
    public async Task AnswerAsync_NormalProseContainingEllipsisStillReachesVerifier()
    {
        RecordingVerifier verifier = new("supported");
        FacultyAssistantAnalysisReport result = await Assistant(new StubGenerator(
            [new("review_question", "The source reports a sequence...then a result.",
                "Could learners explain the sequence...and its result?", ["work:1:snapshot:2:span:3"])]), verifier)
            .AnswerAsync(Request("TeachingHelp"), default);

        Assert.NotNull(verifier.Findings);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task AnswerAsync_MixedVerdictsReturnsOnlySupportedItemsAndCoverage()
    {
        GeneratedFacultyAssistantItem[] candidates = Enumerable.Range(1, 4).Select(index =>
            new GeneratedFacultyAssistantItem("review_question", $"Basis {index}.",
                $"Could the reader consider case {index}?", ["work:1:snapshot:2:span:3"])).ToArray();
        RecordingVerifier verifier = new("supported", "supported", "uncertain", "supported");

        FacultyAssistantAnalysisReport result = await Assistant(
            new StubGenerator(candidates), verifier).AnswerAsync(Request("OwnPaperMethods"), default);

        Assert.Equal("partial", result.Outcome);
        Assert.Equal(3, result.Items.Count);
        Assert.DoesNotContain(result.Items, value => value.Basis == "Basis 3.");
        Assert.Equal(new FacultyAssistantCoverage(4, 4, 3, 0, 1, 1, true), result.Coverage);
    }

    [Fact]
    public async Task AnswerAsync_MissingRequestedLimitationsAndControlsMakesRequestCoveragePartial()
    {
        GeneratedFacultyRequestCoverage assessment = Coverage("partial",
            new("requirement-1", "Explain the method", "fulfilled", [1], "The method is explained."),
            new("requirement-2", "Identify a limitation", "unanswered", [], "No limitation is supplied."),
            new("requirement-3", "Suggest a control", "unanswered", [], "No control is supplied."));
        RecordingRequestCoverageVerifier requestCoverage = new(assessment);

        FacultyAssistantAnalysisReport result = await Assistant(new StubGenerator(
            [new("source_observation", "The source describes method M.", null,
                ["work:1:snapshot:2:span:3"])]), new RecordingVerifier("supported"), requestCoverage)
            .AnswerAsync(Request("OwnPaperMethods"), default);

        Assert.Single(result.Items);
        Assert.Equal("partial", result.Outcome);
        Assert.False(result.Coverage!.IsPartial);
        Assert.Equal("partial", result.RequestCoverage!.Status);
        Assert.Equal(3, result.RequestCoverage.Requirements.Count);
    }

    [Fact]
    public async Task AnswerAsync_TeachingSuggestionWithoutExerciseIsRequestCoveragePartial()
    {
        RecordingRequestCoverageVerifier requestCoverage = new(Coverage("partial",
            new GeneratedFacultyRequestCoverageRequirement(
                "requirement-1", "Provide a classroom exercise", "partial", [1],
                "The item suggests adding an example but supplies no learner task or answer guidance.")));
        GeneratedFacultyAssistantItem item = new("teaching_adaptation",
            "The source compares method M with method N.", "The instructor could add an example.",
            ["work:1:snapshot:2:span:3"]);

        FacultyAssistantAnalysisReport result = await Assistant(new StubGenerator([item]),
            new RecordingVerifier("supported"), requestCoverage)
            .AnswerAsync(Request("TeachingHelp"), default);

        Assert.Single(result.Items);
        Assert.Equal("partial", result.Outcome);
        Assert.False(result.Coverage!.IsPartial);
        Assert.Equal("partial", result.RequestCoverage!.Status);
    }

    [Fact]
    public async Task AnswerAsync_RejectedItemCannotSatisfyRequestCoverage()
    {
        GeneratedFacultyAssistantItem[] candidates =
        [
            new("source_observation", "The source describes method M.", null,
                ["work:1:snapshot:2:span:3"]),
            new("review_question", "Invented limitation.", "Could this error be fixed?",
                ["work:1:snapshot:2:span:3"])
        ];
        RecordingRequestCoverageVerifier requestCoverage = new(Coverage("unanswered",
            new GeneratedFacultyRequestCoverageRequirement(
                "requirement-1", "Provide a conditional issue recheck", "unanswered", [],
                "No retained item provides the requested recheck.")));

        FacultyAssistantAnalysisReport result = await Assistant(new StubGenerator(candidates),
            new RecordingVerifier("supported", "unsupported"), requestCoverage)
            .AnswerAsync(Request("OwnPaperIssues"), default);

        FacultyAssistantAnswerItem retained = Assert.Single(requestCoverage.RetainedItems!);
        Assert.Equal("The source describes method M.", retained.Basis);
        Assert.DoesNotContain(requestCoverage.RetainedItems!, value => value.Basis == "Invented limitation.");
        Assert.Equal("partial", result.Outcome);
        Assert.Equal("unanswered", result.RequestCoverage!.Status);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("out-of-range")]
    [InlineData("identity")]
    public async Task AnswerAsync_InvalidRequestCoverageReferencesFailClosedWithoutDroppingItems(string shape)
    {
        GeneratedFacultyRequestCoverageRequirement[] requirements = shape switch
        {
            "duplicate" => [new("requirement-1", "Answer", "fulfilled", [1, 1], "Duplicate.")],
            "out-of-range" => [new("requirement-1", "Answer", "fulfilled", [2], "Outside retained items.")],
            _ =>
            [
                new("requirement-1", "First", "fulfilled", [1], "Covered."),
                new("requirement-1", "Second", "fulfilled", [1], "Duplicate identity.")
            ]
        };
        RecordingRequestCoverageVerifier requestCoverage = new(Coverage("fulfilled", requirements));

        FacultyAssistantAnalysisReport result = await Assistant(new StubGenerator(
            [new("source_observation", "The source reports 40 participants.", null,
                ["work:1:snapshot:2:span:3"])]), new RecordingVerifier("supported"), requestCoverage)
            .AnswerAsync(Request("ExploreOwnRecord"), default);

        Assert.Single(result.Items);
        Assert.Equal("partial", result.Outcome);
        Assert.Equal("unavailable", result.RequestCoverage!.Status);
        Assert.Empty(result.RequestCoverage.Model);
        Assert.Equal(1, requestCoverage.Calls);
    }

    [Fact]
    public async Task AnswerAsync_RequestCoverageProviderFailurePreservesSupportedItemsWithoutRetry()
    {
        RecordingRequestCoverageVerifier requestCoverage = new(error:
            new InvalidAnalysisException(AnalysisFailure.OutputLimit));

        FacultyAssistantAnalysisReport result = await Assistant(new StubGenerator(
            [new("source_observation", "The source reports 40 participants.", null,
                ["work:1:snapshot:2:span:3"])]), new RecordingVerifier("supported"), requestCoverage)
            .AnswerAsync(Request("ExploreOwnRecord"), default);

        Assert.Single(result.Items);
        Assert.Equal("partial", result.Outcome);
        Assert.Equal("unavailable", result.RequestCoverage!.Status);
        Assert.Empty(result.RequestCoverage.Requirements);
        Assert.False(result.RequestCoverage.UsesSameModelFamily);
        Assert.Equal(1, requestCoverage.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    public async Task AnswerAsync_MalformedVerifierIdsStillFailClosed(string shape)
    {
        GeneratedFacultyAssistantItem[] candidates =
        [
            new("source_observation", "Basis 1.", null, ["work:1:snapshot:2:span:3"]),
            new("source_observation", "Basis 2.", null, ["work:1:snapshot:2:span:3"])
        ];
        IReadOnlyList<GeneratedArticleReviewVerdict> verdicts = shape switch
        {
            "missing" => [new("assistant-1", "supported", "ok")],
            "unknown" => [new("assistant-1", "supported", "ok"), new("other", "supported", "ok")],
            _ => [new("assistant-1", "supported", "ok"), new("assistant-1", "uncertain", "no")]
        };

        await Assert.ThrowsAsync<InvalidAnalysisException>(() => Assistant(
            new StubGenerator(candidates), new ExactVerifier(verdicts))
            .AnswerAsync(Request("OwnPaperMethods"), default));
    }

    [Fact]
    public async Task AnswerAsync_BadMathCandidateIsNotReturned()
    {
        StubGenerator generator = new(
        [
            new("teaching_adaptation", "The source says v_t.",
                "Students could trace v_t.", ["work:1:snapshot:2:span:3"]),
            new("teaching_adaptation", "The source says square root p_t.",
                "Students could derive square root p_t.", ["work:1:snapshot:2:span:3"])
        ]);

        FacultyAssistantAnalysisReport result = await Assistant(generator,
            new RecordingVerifier("supported", "uncertain")).AnswerAsync(Request("TeachingHelp"), default);

        FacultyAssistantAnswerItem item = Assert.Single(result.Items);
        Assert.Equal("The source says v_t.", item.Basis);
        Assert.Equal("partial", result.Outcome);
        Assert.Equal(1, result.Coverage!.UncertainItems);
    }

    [Fact]
    public async Task AnswerAsync_SingletonChecksRepairTwoSemanticSlotsWithoutRewritingSupportedItem()
    {
        FacultyAssistantAnalysisRequest request = Request("OwnPaperMethods");
        request.Evidence.Add(new("evidence-2", 1, 4, "src-2", 2, 0, 18,
            "Second exact source.", "pdf", false));
        request.Evidence.Add(new("evidence-3", 1, 5, "src-3", 3, 0, 17,
            "Third exact source.", "pdf", false));
        GeneratedFacultyAssistantItem original = new("source_observation", "Original supported basis.", null,
            ["work:1:snapshot:2:span:3"]);
        StubGenerator generator = new([
            original,
            new("review_question", "Unsupported premise.", "Check it?", ["evidence-2"]),
            new("review_question", "Uncertain premise.", "Check that?", ["evidence-3"])
        ]);
        ScriptedVerifier verifier = new(new Dictionary<string, string>
        {
            ["assistant-1"] = "supported", ["assistant-2"] = "unsupported",
            ["assistant-3"] = "uncertain", ["repair-assistant-2"] = "supported",
            ["repair-assistant-3"] = "unverified_output_limit"
        });
        RecordingRepairGenerator repair = new([
            new("assistant-2", new("review_question", "Corrected supported premise.",
                "Check the corrected premise?", ["evidence-2"])),
            new("assistant-3", new("review_question", "Still unresolved premise.",
                "Check the unresolved premise?", ["evidence-3"]))
        ]);
        RecordingRequestCoverageVerifier coverage = new(Coverage("fulfilled",
            new GeneratedFacultyRequestCoverageRequirement("requirement-1", "Answer", "fulfilled", [1, 2], "ok")));

        FacultyAssistantAnalysisReport result = await new FacultyAssistant(generator, verifier, repair, coverage)
            .AnswerAsync(request, default);

        Assert.Equal(["Original supported basis.", "Corrected supported premise."],
            result.Items.Select(value => value.Basis));
        Assert.Equal(["assistant-1", "assistant-2"], result.Items.Select(value => value.CandidateId));
        Assert.Equal(5, result.SourceChecks!.TotalCandidates);
        Assert.Equal(1, result.SourceChecks.UnverifiedOutputLimitItems);
        Assert.Equal(2, result.Repair!.RequestedCandidates);
        Assert.Equal(2, result.Repair.GeneratedCandidates);
        Assert.Equal(1, result.Repair.RetainedItems);
        Assert.Equal(3, result.Coverage!.CandidateItems);
        Assert.Equal(2, result.Coverage.SupportedItems);
        Assert.Equal(1, result.Coverage.UnverifiedItems);
        Assert.Equal(1, result.Coverage.OmittedItems);
        Assert.Equal("partial", result.Outcome);
        Assert.All(verifier.Calls, call => Assert.Equal(call.Finding.SourceIds,
            call.Spans.Select(value => value.SourceId)));
        Assert.Equal(1, repair.Calls);
        Assert.Equal(["assistant-2", "assistant-3"], repair.Targets!.Select(value => value.CandidateId));
        Assert.Same(original, Assert.Single(repair.SupportedItems!));
    }

    [Fact]
    public async Task AnswerAsync_UnverifiedInvalidSingletonIsOmittedAndNeverSentToRepair()
    {
        ScriptedVerifier verifier = new(new Dictionary<string, string>
            { ["assistant-1"] = "unverified_invalid_response" });
        NeverRepairGenerator repair = new();

        FacultyAssistantAnalysisReport result = await new FacultyAssistant(new StubGenerator(
            [new("source_observation", "Candidate basis.", null, ["work:1:snapshot:2:span:3"])]),
            verifier, repair, new RecordingRequestCoverageVerifier()).AnswerAsync(
                Request("OwnPaperMethods"), default);

        Assert.Empty(result.Items);
        Assert.Equal("partially_checked", result.Verification.Status);
        Assert.Equal(1, result.Coverage!.UnverifiedItems);
        Assert.Equal("not_run", result.Repair!.Status);
        Assert.Equal(0, repair.Calls);
        Assert.Equal("unanswered", result.RequestCoverage!.Status);
    }

    [Fact]
    public async Task AnswerAsync_IdenticalDocumentLocalSourceIdsAcrossWorksStayDistinct()
    {
        FacultyAssistantAnalysisRequest request = Request("RelatedWorks");
        request.Evidence.Add(new("work:2:snapshot:4:span:5", 2, 5, "src-1", 1, 0, 40,
            "The source reports 40 participants.", "pdf", false));
        StubGenerator generator = new([new("source_observation", "Two records report the same text.", null,
            ["work:1:snapshot:2:span:3", "work:2:snapshot:4:span:5"])]);
        RecordingVerifier verifier = new("supported");

        FacultyAssistantAnalysisReport result = await Assistant(generator, verifier)
            .AnswerAsync(request, default);

        Assert.Equal(2, result.Items.Single().Citations.Select(value => value.EvidenceId).Distinct().Count());
        Assert.All(result.Items.Single().Citations, value => Assert.StartsWith("work:", value.EvidenceId));
    }

    [Fact]
    public async Task AnswerAsync_VerifierReceivesOnlyExplicitlyCitedEvidence()
    {
        FacultyAssistantAnalysisRequest request = Request("OwnPaperMethods");
        request.Evidence.Add(new("work:2:snapshot:4:span:5", 2, 5, "src-9", 1, 0, 20,
            "Unrelated ranked hit.", "pdf", false));
        RecordingVerifier verifier = new("supported");

        await Assistant(new StubGenerator([new("source_observation",
            "The source reports 40 participants.", null, ["work:1:snapshot:2:span:3"])]), verifier)
            .AnswerAsync(request, default);

        ArticleSourceSpan source = Assert.Single(verifier.SourceSpans!);
        Assert.Equal("work:1:snapshot:2:span:3", source.SourceId);
    }

    [Theory]
    [InlineData("OwnPaperMethods", "teaching_adaptation")]
    [InlineData("TeachingHelp", "source_observation")]
    public async Task AnswerAsync_CapturedMixedModeKindsAreRejectedBeforeVerifier(
        string mode, string invalidKind)
    {
        RecordingVerifier verifier = new("supported");
        string validKind = mode == "TeachingHelp" ? "review_question" : "source_observation";
        string? validResponse = validKind == "source_observation" ? null : "Kaynağa dayalı bir soru.";
        string? invalidResponse = invalidKind == "source_observation" ? null :
            "Bu yaklaşım yöntem bölümüne uyarlanmalıdır.";
        StubGenerator generator = new(
        [
            new(validKind, "Adam makalesinde karşılaştırmalar aynı parametre ilklendirmesini kullanır.",
                validResponse, ["work:1:snapshot:2:span:3"]),
            new(invalidKind, "Adam makalesinde karşılaştırmalar aynı parametre ilklendirmesini kullanır.",
                invalidResponse, ["work:1:snapshot:2:span:3"])
        ]);

        await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            Assistant(generator, verifier).AnswerAsync(Request(mode), default));

        Assert.Null(verifier.Findings);
    }

    [Theory]
    [InlineData("source_observation", "Unexpected response", false)]
    [InlineData("review_question", null, false)]
    [InlineData("source_observation", null, true)]
    public async Task AnswerAsync_InvalidAdapterShapeIsRejectedBeforeVerifier(
        string kind, string? response, bool duplicateEvidence)
    {
        RecordingVerifier verifier = new("supported");
        IReadOnlyList<string> evidenceIds = duplicateEvidence
            ? ["work:1:snapshot:2:span:3", "work:1:snapshot:2:span:3"]
            : ["work:1:snapshot:2:span:3"];

        await Assert.ThrowsAsync<InvalidAnalysisException>(() => Assistant(
            new StubGenerator([new(kind, "Source-backed basis.", response, evidenceIds)]), verifier)
            .AnswerAsync(Request("OwnPaperMethods"), default));

        Assert.Null(verifier.Findings);
    }

    [Theory]
    [InlineData("OwnPaperMethods")]
    [InlineData("TeachingHelp")]
    public async Task AnswerAsync_PermittedCapturedItemBuildsCiteOnlyGeminiVerifierRequest(string mode)
    {
        FacultyAssistantAnalysisRequest request = CapturedRequest(mode);
        GeneratedFacultyAssistantItem item = CapturedPermittedItem(mode);
        using StubHttpHandler handler = new(async message =>
        {
            Assert.Equal(
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent",
                message.RequestUri!.AbsoluteUri);
            using JsonDocument body = await JsonDocument.ParseAsync(
                await message.Content!.ReadAsStreamAsync());
            Assert.Equal(8192, body.RootElement.GetProperty("generationConfig")
                .GetProperty("maxOutputTokens").GetInt32());
            Assert.Equal("medium", body.RootElement.GetProperty("generationConfig")
                .GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
            string serializedInput = body.RootElement.GetProperty("contents")[0]
                .GetProperty("parts")[0].GetProperty("text").GetString()!;
            using JsonDocument input = JsonDocument.Parse(serializedInput);
            Assert.Equal(mode == "TeachingHelp" ? "teaching" : "method",
                input.RootElement.GetProperty("role").GetString());
            JsonElement verificationItem = Assert.Single(input.RootElement.GetProperty("items").EnumerateArray());
            JsonElement source = Assert.Single(verificationItem.GetProperty("sources").EnumerateArray());
            Assert.Equal(request.Evidence[0].EvidenceId, source.GetProperty("sourceId").GetString());
            Assert.Equal(request.Evidence[0].ExactText, source.GetProperty("text").GetString());
            string responseJson = JsonSerializer.Serialize(new
            {
                verdicts = new[] { new { findingId = "assistant-1", verdict = "supported",
                    reason = "The cited source directly supports the basis." } }
            });
            return GeminiResponse(responseJson);
        });
        AiOptions settings = new()
        {
            ArticleProvider = "Gemini", ArticleModel = "gemini-3.8-flash",
            ArticleVerifierModel = "gemini-3.8-flash", ArticleVerifierMaxOutputTokens = 8192,
            ArticleVerifierThinkingLevel = "high", FacultyAssistantVerifierThinkingLevel = "medium"
        };
        IOptions<AiOptions> options = Options.Create(settings);
        GeminiArticleClient client = new(new HttpClient(handler), options,
            Options.Create(new GeminiOptions { ApiKey = "synthetic" }), new TestGeminiUsageRepository());
        GeminiFacultyAssistantVerifier verifier = new(client, options);

        FacultyAssistantAnalysisReport result = await Assistant(
            new StubGenerator([item]), verifier).AnswerAsync(request, default);

        Assert.Equal("completed", result.Outcome);
        Assert.Equal("gemini-3.8-flash", result.Verification.Model);
        Assert.Equal(FacultyAssistantVerificationPrompt.Version, result.Verification.PromptVersion);
        Assert.Equal(request.Evidence[0].ExactText,
            Assert.Single(Assert.Single(result.Items).Citations).ExactQuote);
    }

    private static FacultyAssistant Assistant(IFacultyAssistantGenerator generator,
        IFacultyAssistantVerifier verifier, IFacultyRequestCoverageVerifier? requestCoverage = null) =>
        new(generator, verifier, new NoRepairGenerator(),
            requestCoverage ?? new RecordingRequestCoverageVerifier());

    private static GeneratedFacultyRequestCoverage Coverage(string status,
        params GeneratedFacultyRequestCoverageRequirement[] requirements) =>
        new(status, requirements, "gemini-3.8-flash", FacultyRequestCoveragePrompt.Version);

    [Fact]
    public async Task Endpoint_OllamaArticleProviderReturnsUnavailableBeforeOutboundRequest()
    {
        int requests = 0;
        using CountingHandler handler = new(() => requests++);
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            geminiHandler: handler,
            settings: new Dictionary<string, string?>
            {
                ["Ai:ArticleProvider"] = "Ollama",
                ["Ai:ArticleModel"] = "synthetic-ollama"
            });

        FacultyAssistantAnalysisRequest request = Request("OwnPaperMethods");
        request.Evidence[0] = request.Evidence[0] with
        {
            EndOffset = request.Evidence[0].ExactText.Length
        };
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            "/api/v1/faculty-assistant", request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, requests);
    }

    private static FacultyAssistantAnalysisRequest Request(string mode) => new()
    {
        Mode = mode, Language = "en", Query = "sample", EvidenceCatalogHash = new string('a', 64),
        Evidence = [new("work:1:snapshot:2:span:3", 1, 3, "src-1", 1, 0, 40,
            "The source reports 40 participants.", "pdf", false)]
    };

    private static FacultyAssistantAnalysisRequest CapturedRequest(string mode)
    {
        string text = mode == "TeachingHelp" ? TeachingEvidence : MethodsEvidence;
        string evidenceId = mode == "TeachingHelp"
            ? "work:1:snapshot:1:span:34" : "work:1:snapshot:1:span:38";
        return new()
        {
            Mode = mode, Language = "tr", Query = "Adam source-keyword pilot query",
            EvidenceCatalogHash = new string('a', 64),
            Evidence = [new(evidenceId, 1, mode == "TeachingHelp" ? 34 : 38,
                mode == "TeachingHelp" ? "src-5-0-f352675a32301315" :
                    "src-5-2116-2649e76656c546a2",
                5, 0, text.Length, text, "pdf", true)]
        };
    }

    private static GeneratedFacultyAssistantItem CapturedPermittedItem(string mode) => mode == "TeachingHelp"
        ? new("teaching_adaptation",
            "Adam algoritmasında sıfır bellek durumunda sapma düzeltme terimleri 1'e eşittir ve güncelleme gradyanın işaretine indirgenir.",
            "Öğrencilerden bu dönüşümü adım adım türetmeleri istenebilecek bir ders etkinliği önerilebilir.",
            ["work:1:snapshot:1:span:34"])
        : new("source_observation",
            "Adam makalesindeki optimizasyon karşılaştırmaları aynı parametre ilklendirmesini kullanır ve hiperparametreleri yoğun bir ızgarada tarar.",
            null, ["work:1:snapshot:1:span:38"]);

    private static HttpResponseMessage GeminiResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = json } } },
                finishReason = "STOP" } },
            usageMetadata = new { promptTokenCount = 100, candidatesTokenCount = 20,
                totalTokenCount = 120 },
            modelVersion = "gemini-3.8-flash"
        })
    };

    private const string MethodsEvidence = """
         Using large models and datasets, we demonstrate how well Adam can solve
        practical deep learning problems.

        We use the same parameter initialization when comparing different optimization algorithms. The
        hyper-parameters, such as learning rate and momentum, are searched over a dense grid and the
        results are reported using the best hyper-parameter setting.

        6.1

        EXPERIMENT: LOGISTIC REGRESSION

        WeevaluateourproposedmethodonL2-regularizedmulti-classlogisticregressionusingtheMNIST
        dataset.
        """;

    private const string TeachingEvidence = """
        Under review as a conference paper at ICLR 2015

        RProp:
        The Rprop method Riedmiller & Braun (1992) is a robust algorithm for gradient-based
        optimization of non-stochastic objectives. In its basic form, Rprop takes steps proportional to only
        ). Rprop can be retrieved as a special case of Adam
        the sign of the gradient: θt+1 = θt −α·sign(gt
        where
        β1 = 1 and
        β2 = 1, i.e. the case with zero memory. In this case Adam’s bias correction terms
        √
        √
        2
        g
        /
        equal 1, and the update is: θt+1 = θt − α · mt
        / vt = θt − α · gt
        t = θ − α · sign(gt). In
        """;

    private sealed class StubGenerator(IReadOnlyList<GeneratedFacultyAssistantItem> items) : IFacultyAssistantGenerator
    {
        public Task<GeneratedFacultyAssistantAnswer> GenerateAsync(FacultyAssistantAnalysisRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new GeneratedFacultyAssistantAnswer(
                items, "gemini-3.8-flash", FacultyAssistantPrompt.Version,
                [new(1, "high", "Success", "gemini-3.8-flash", 120, 0.000150m,
                    "gemini-3.8-flash-standard-through-2026-12-31", true)]));
    }

    private sealed class RecordingVerifier(params string[] verdicts) : IFacultyAssistantVerifier
    {
        public string? Role { get; private set; }
        public List<GeneratedArticleReviewFinding>? Findings { get; private set; }
        public IReadOnlyList<ArticleSourceSpan>? SourceSpans { get; private set; }
        private int calls;
        public Task<GeneratedFacultyAssistantSourceCheck> VerifyAsync(string role, string language,
            GeneratedArticleReviewFinding finding, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            Role = role; (Findings ??= []).Add(finding); SourceSpans = sourceSpans;
            string verdict = verdicts.Length == 1 ? verdicts[0] : verdicts[calls++];
            GeneratedArticleReviewVerdict result = new(finding.FindingId, verdict, "synthetic");
            return Task.FromResult(new GeneratedFacultyAssistantSourceCheck(Guid.NewGuid(), "checked", result,
                "gemini-3.8-flash", FacultyAssistantVerificationPrompt.Version, result.Reason));
        }
    }

    private sealed class ExactVerifier(IReadOnlyList<GeneratedArticleReviewVerdict> verdicts) : IFacultyAssistantVerifier
    {
        public Task<GeneratedFacultyAssistantSourceCheck> VerifyAsync(string role, string language,
            GeneratedArticleReviewFinding finding, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            GeneratedArticleReviewVerdict? verdict = verdicts.FirstOrDefault(value => value.FindingId == finding.FindingId);
            return Task.FromResult(new GeneratedFacultyAssistantSourceCheck(Guid.NewGuid(), "checked", verdict,
                "gemini-3.8-flash", FacultyAssistantVerificationPrompt.Version, verdict?.Reason ?? "missing"));
        }
    }

    private sealed class NoRepairGenerator : IFacultyAssistantRepairGenerator
    {
        public Task<GeneratedFacultyAssistantRepair> RepairAsync(FacultyAssistantAnalysisRequest request,
            IReadOnlyList<GeneratedFacultyAssistantRepairCandidate> omittedCandidates,
            IReadOnlyList<GeneratedFacultyAssistantItem> supportedItems, CancellationToken cancellationToken) =>
            Task.FromResult(new GeneratedFacultyAssistantRepair("completed", [], "gemini-3.8-flash",
                FacultyAssistantRepairPrompt.Version,
                new GeneratedFacultyAssistantGenerationAttempt(1, "medium", "Success", "gemini-3.8-flash",
                    120, 0.00015m, "synthetic-pricing", true) { AttemptId = Guid.NewGuid() }));
    }

    private sealed class RecordingRepairGenerator(
        IReadOnlyList<GeneratedFacultyAssistantRepairItem> items) : IFacultyAssistantRepairGenerator
    {
        public int Calls { get; private set; }
        public IReadOnlyList<GeneratedFacultyAssistantRepairCandidate>? Targets { get; private set; }
        public IReadOnlyList<GeneratedFacultyAssistantItem>? SupportedItems { get; private set; }
        public Task<GeneratedFacultyAssistantRepair> RepairAsync(FacultyAssistantAnalysisRequest request,
            IReadOnlyList<GeneratedFacultyAssistantRepairCandidate> omittedCandidates,
            IReadOnlyList<GeneratedFacultyAssistantItem> supportedItems, CancellationToken cancellationToken)
        {
            Calls++; Targets = omittedCandidates; SupportedItems = supportedItems;
            return Task.FromResult(new GeneratedFacultyAssistantRepair("completed", items, "gemini-3.8-flash",
                FacultyAssistantRepairPrompt.Version,
                new GeneratedFacultyAssistantGenerationAttempt(1, "medium", "Success", "gemini-3.8-flash",
                    150, 0.0002m, "synthetic-pricing", true) { AttemptId = Guid.NewGuid() }));
        }
    }

    private sealed class NeverRepairGenerator : IFacultyAssistantRepairGenerator
    {
        public int Calls { get; private set; }
        public Task<GeneratedFacultyAssistantRepair> RepairAsync(FacultyAssistantAnalysisRequest request,
            IReadOnlyList<GeneratedFacultyAssistantRepairCandidate> omittedCandidates,
            IReadOnlyList<GeneratedFacultyAssistantItem> supportedItems, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Repair must not run."); }
    }

    private sealed class ScriptedVerifier(IReadOnlyDictionary<string, string> statuses)
        : IFacultyAssistantVerifier
    {
        public List<(GeneratedArticleReviewFinding Finding, IReadOnlyList<ArticleSourceSpan> Spans)> Calls { get; } = [];
        public Task<GeneratedFacultyAssistantSourceCheck> VerifyAsync(string role, string language,
            GeneratedArticleReviewFinding finding, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            Calls.Add((finding, sourceSpans));
            string status = statuses[finding.FindingId];
            GeneratedArticleReviewVerdict? verdict = status.StartsWith("unverified_", StringComparison.Ordinal)
                ? null : new(finding.FindingId, status, $"{status} reason");
            return Task.FromResult(new GeneratedFacultyAssistantSourceCheck(Guid.NewGuid(),
                verdict is null ? status : "checked", verdict, "gemini-3.8-flash",
                FacultyAssistantVerificationPrompt.Version, verdict?.Reason ?? $"{status} reason"));
        }
    }

    private sealed class RecordingRequestCoverageVerifier(
        GeneratedFacultyRequestCoverage? result = null,
        Exception? error = null) : IFacultyRequestCoverageVerifier
    {
        public int Calls { get; private set; }
        public string? Mode { get; private set; }
        public string? Query { get; private set; }
        public IReadOnlyList<FacultyAssistantAnswerItem>? RetainedItems { get; private set; }

        public Task<GeneratedFacultyRequestCoverage> VerifyAsync(string mode, string language,
            string query, IReadOnlyList<FacultyAssistantAnswerItem> retainedItems,
            CancellationToken cancellationToken)
        {
            Calls++;
            Mode = mode;
            Query = query;
            RetainedItems = retainedItems;
            if (error is not null) return Task.FromException<GeneratedFacultyRequestCoverage>(error);
            return Task.FromResult(result ?? Coverage("fulfilled",
                new GeneratedFacultyRequestCoverageRequirement(
                    "requirement-1", "Answer the request", "fulfilled", [1],
                    "The retained item answers the request.")));
        }
    }

    private sealed class CountingHandler(Action record) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            record();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
