using System.Net;
using System.Text;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ServiceAcceptancePilot;

internal static class RetainedAcceptanceSelfTest
{
    public static async Task<int> RunAsync()
    {
        await AssertAsync();
        Console.WriteLine("SELF_TEST_OK");
        return 0;
    }

    internal static async Task AssertAsync()
    {
        await AssertAtomicWriteRetryAsync();
        await AssertProviderCaptureAsync();
        ServiceAcceptanceBudget diagnosticLimit = new(_ => Task.CompletedTask,
            maximumCalls: 4, maximumSpendUsd: 0.30m);
        Require(diagnosticLimit.Snapshot() is { MaximumCalls: 4, MaximumSpendUsd: 0.30m },
            "The diagnostic budget did not retain its stricter released limits.");
        ServiceAcceptanceBudget mediumBudget = new(_ => Task.CompletedTask);
        using (HttpClient mediumClient = new(new RetainedBudgetHandler(mediumBudget,
            allowFacultyInitialMedium: true) { InnerHandler = new StubHandler(_ => Response("STOP")) }))
        using (await mediumClient.SendAsync(Request("medium", "initial-medium"))) { }
        Require(mediumBudget.Snapshot() is { Calls: 1, UnknownUsageCalls: 0 },
            "The released initial medium faculty generation was not admitted.");
        int dispatches = 0;
        ServiceAcceptanceBudget budget = new(_ => Task.CompletedTask);
        using HttpClient client = new(new RetainedBudgetHandler(budget)
        {
            InnerHandler = new StubHandler(_ =>
            {
                dispatches++;
                return Response(dispatches == 1 ? "MAX_TOKENS" : "STOP");
            })
        });
        using (await client.SendAsync(Request("high", "same"))) { }
        using (await client.SendAsync(Request("medium", "same"))) { }
        Require(dispatches == 2 && budget.Snapshot() is { Calls: 2, UnknownUsageCalls: 0 },
            "Matching high-to-medium recovery was not admitted exactly once.");
        await RejectAsync(() => client.SendAsync(Request("medium", "same")));
        await RejectAsync(() => NewClient(Response("STOP")).SendAsync(Request("medium", "same")));

        ServiceAcceptanceBudget changed = new(_ => Task.CompletedTask);
        using HttpClient changedClient = new(new RetainedBudgetHandler(changed)
        {
            InnerHandler = new StubHandler(_ => Response("MAX_TOKENS"))
        });
        using (await changedClient.SendAsync(Request("high", "one"))) { }
        await RejectAsync(() => changedClient.SendAsync(Request("medium", "two")));
        await RejectAsync(() => changedClient.SendAsync(Request("medium", "one")));
        Require(changed.Snapshot().Calls == 1, "A changed recovery body reached the budget ledger.");

        ServiceAcceptanceBudget invalidTarget = new(_ => Task.CompletedTask);
        using HttpClient invalidTargetClient = new(new RetainedBudgetHandler(invalidTarget)
        { InnerHandler = new StubHandler(_ => Response("MAX_TOKENS")) });
        using (await invalidTargetClient.SendAsync(Request("high", "same"))) { }
        HttpRequestMessage wrongTarget = Request("medium", "same");
        wrongTarget.RequestUri = new("https://example.invalid/not-gemini");
        await RejectAsync(() => invalidTargetClient.SendAsync(wrongTarget));
        await RejectAsync(() => invalidTargetClient.SendAsync(Request("medium", "same")));

        ServiceAcceptanceBudget nonFaculty = new(_ => Task.CompletedTask);
        using HttpClient nonFacultyClient = new(new RetainedBudgetHandler(nonFaculty)
        {
            InnerHandler = new StubHandler(_ => Response("MAX_TOKENS"))
        });
        using (await nonFacultyClient.SendAsync(Request("high", "same", false))) { }
        await RejectAsync(() => nonFacultyClient.SendAsync(Request("medium", "same", false)));

        int reviewDispatches = 0;
        ServiceAcceptanceBudget review = new(_ => Task.CompletedTask);
        using HttpClient reviewClient = new(new RetainedBudgetHandler(review)
        {
            InnerHandler = new StubHandler(_ =>
            {
                reviewDispatches++;
                return Response(reviewDispatches == 1 ? "MAX_TOKENS" : "STOP");
            })
        });
        using (await reviewClient.SendAsync(ReviewRequest("high", "same"))) { }
        using (await reviewClient.SendAsync(ReviewRequest("medium", "same"))) { }
        await RejectAsync(() => reviewClient.SendAsync(ReviewRequest("medium", "same")));
        Require(reviewDispatches == 2 && review.Snapshot() is { Calls: 2, UnknownUsageCalls: 0 },
            "Matching article review high-to-medium recovery was not admitted exactly once.");

        ServiceAcceptanceBudget facultyVerifier = new(_ => Task.CompletedTask);
        using HttpClient verifierClient = new(new RetainedBudgetHandler(facultyVerifier,
            allowFacultyInitialMedium: true)
        {
            InnerHandler = new StubHandler(_ => Response("STOP"))
        });
        using (await verifierClient.SendAsync(FacultyVerifierRequest("medium", "same"))) { }
        Require(facultyVerifier.Snapshot() is { Calls: 1, UnknownUsageCalls: 0 },
            "The released singleton faculty verifier medium dispatch was not admitted.");

        ServiceAcceptanceBudget repair = new(_ => Task.CompletedTask);
        using HttpClient repairClient = new(new RetainedBudgetHandler(repair,
            allowFacultyInitialMedium: true) { InnerHandler = new StubHandler(_ => Response("STOP")) });
        using (await repairClient.SendAsync(FacultyRepairRequest("medium", "same"))) { }
        Require(repair.Snapshot() is { Calls: 1, UnknownUsageCalls: 0 },
            "The released bounded repair medium dispatch was not admitted.");

        string validName = RetainedAcceptanceDatabase.TargetPrefix + new string('a', 32);
        RetainedAcceptanceDatabase.ValidateTargetName(validName);
        bool sourceRejected = false;
        try
        {
            RetainedAcceptanceDatabase.ValidateTargetName(RetainedAcceptanceDatabase.SourceName);
        }
        catch (InvalidOperationException) { sourceRejected = true; }
        Require(sourceRejected, "Source database passed target ownership validation.");
        Require(RetainedAcceptanceDatabase.ConvertBackupSize(123L) == 123 &&
            RetainedAcceptanceDatabase.ConvertBackupSize(456m) == 456,
            "Supported backup size representations were not converted exactly.");
        RejectBackupSize(-1L);
        RejectBackupSize(1.5m);
        RejectBackupSize((decimal)long.MaxValue + 1);
    }

    private static HttpClient NewClient(HttpResponseMessage response) => new(new RetainedBudgetHandler(
        new ServiceAcceptanceBudget(_ => Task.CompletedTask)) { InnerHandler = new StubHandler(_ => response) });

    private static HttpRequestMessage Request(string thinking, string marker, bool faculty = true) => new(HttpMethod.Post,
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent")
    {
        Content = new StringContent(GeminiArticleClient.CreateSerializedBody(
            faculty ? FacultyAssistantPrompt.Instructions : "unrelated verifier",
            marker, FacultyAssistantPrompt.CreateSchema("OwnPaperMethods", ["evidence-1"]), 16384, thinking),
            Encoding.UTF8, "application/json")
    };

    private static HttpRequestMessage ReviewRequest(string thinking, string marker) => new(HttpMethod.Post,
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent")
    {
        Content = new StringContent(GeminiArticleClient.CreateSerializedBody(
            ArticleReviewPrompt.InstructionsForRole("method"), marker,
            ArticleReviewPrompt.CreateSchema("method"), 8192, thinking), Encoding.UTF8, "application/json")
    };

    private static HttpRequestMessage FacultyVerifierRequest(string thinking, string marker) => new(HttpMethod.Post,
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent")
    {
        Content = new StringContent(GeminiArticleClient.CreateSerializedBody(
            FacultyAssistantVerificationPrompt.Instructions, marker,
            FacultyAssistantVerificationPrompt.CreateSchema(["assistant-1"]), 8192, thinking),
            Encoding.UTF8, "application/json")
    };

    private static HttpRequestMessage FacultyRepairRequest(string thinking, string marker) => new(HttpMethod.Post,
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent")
    {
        Content = new StringContent(GeminiArticleClient.CreateSerializedBody(
            FacultyAssistantRepairPrompt.Instructions, marker,
            FacultyAssistantRepairPrompt.CreateSchema("OwnPaperMethods", ["assistant-1"], ["evidence-1"]),
            16384, thinking), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Response(string finishReason) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"modelVersion\":\"gemini-3.8-flash\",\"candidates\":[{\"finishReason\":\"" +
            finishReason + "\"}],\"usageMetadata\":{\"promptTokenCount\":10,\"candidatesTokenCount\":5," +
            "\"thoughtsTokenCount\":2,\"totalTokenCount\":17}}", Encoding.UTF8, "application/json")
    };

    private static async Task RejectAsync(Func<Task<HttpResponseMessage>> action)
    {
        try { using HttpResponseMessage ignored = await action(); }
        catch (ServiceAcceptanceBudgetException) { return; }
        throw new InvalidOperationException("Forbidden medium dispatch was accepted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RejectBackupSize(object value)
    {
        try { RetainedAcceptanceDatabase.ConvertBackupSize(value); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("An invalid backup file size was accepted.");
    }

    private static async Task AssertAtomicWriteRetryAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "researcher-retained-atomic-self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string transient = Path.Combine(directory, "transient.json");
            await File.WriteAllTextAsync(transient, "old");
            using (FileStream held = new(transient, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Task write = FinalAcceptanceArtifacts.WriteAtomicAsync(transient, "new");
                await Task.Delay(200);
                Require(!write.IsCompleted, "Atomic write did not encounter the held sharing lock.");
                held.Dispose();
                await write;
            }
            Require((await File.ReadAllTextAsync(transient)).Trim() == "new",
                "Atomic write did not recover after the sharing lock was released.");

            string permanent = Path.Combine(directory, "permanent.json");
            await File.WriteAllTextAsync(permanent, "old");
            bool failed = false;
            using (FileStream held = new(permanent, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { await FinalAcceptanceArtifacts.WriteAtomicAsync(permanent, "new"); }
                catch (UnauthorizedAccessException) { failed = true; }
                catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
                {
                    failed = true;
                }
            }
            Require(failed && Directory.GetFiles(directory, "permanent.json.pending-*").Length == 1 &&
                (await File.ReadAllTextAsync(permanent)) == "old",
                "A permanent sharing lock did not fail boundedly with its pending artifact retained.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task AssertProviderCaptureAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "researcher-retained-provider-capture-self-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            RetainedProviderCapture capture = new(directory);
            using HttpRequestMessage request = Request("high", "capture-marker");
            request.Headers.Authorization = new("Bearer", "never-write-this-secret");
            byte[] body = await request.Content!.ReadAsByteArrayAsync();
            await capture.WriteRequestAsync(1, DateTime.UtcNow, request, body, CancellationToken.None);
            using HttpResponseMessage response = Response("STOP");
            byte[] responseBody = await response.Content.ReadAsByteArrayAsync();
            await capture.WriteResponseAsync(1, response, responseBody, CancellationToken.None);
            string requestArtifact = await File.ReadAllTextAsync(Path.Combine(directory, "attempt-01-request.json"));
            Require(!requestArtifact.Contains("never-write-this-secret", StringComparison.Ordinal) &&
                !requestArtifact.Contains("Authorization", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(directory, "attempt-01-response.json")),
                "The sanitized provider capture exposed headers or omitted the response.");
            bool immutable = false;
            try { await capture.WriteRequestAsync(1, DateTime.UtcNow, request, body, CancellationToken.None); }
            catch (IOException) { immutable = true; }
            Require(immutable, "A provider attempt capture was overwritten.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
