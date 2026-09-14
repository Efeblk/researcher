using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using AcademicCollector.Analysis.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ServiceAcceptancePilot;

internal static class QualificationRegression
{
    private const string QualifiedClaim = "Yazarlar, çevrimiçi dışbükey optimizasyon çerçevesinde; gradyanların sınırlı, Adam tarafından üretilen herhangi iki parametre vektörü arasındaki uzaklığın sınırlı ve γt=1/t olduğu varsayımları altında Adam için bir pişmanlık sınırı sunmaktadır.";
    private const string ConditionalQuestion = "Bu varsayımlar kendi çalışmanızda sağlanmıyorsa, sunulan pişmanlık sınırını doğrudan bir garanti olarak kullanmaktan kaçınıp uygulanabilirliği ayrıca değerlendirdiniz mi?";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<JsonObject> RunAsync(
        WebApplication analysis, string database, ServiceAcceptanceBudget budget)
    {
        ServiceAcceptanceBudgetSnapshot before = budget.Snapshot();
        bool allCriteriaMatched = true;
        IReadOnlyList<ArticleSourceSpan> genericSpans = GenericSpans();
        IReadOnlyList<GeneratedArticleClaim> claims =
        [
            new("old-overbroad",
                "Cevrim ici disbukey optimizasyon altinda Adam algoritmasinin O(T^(1/2)) pismanlik sinirina ulastigi ve ortalama pismanliginin sifira yakinsadigi gosterilmistir.",
                ["src-4-471-47a2656a865c4664", "src-4-2037-50c75eeda7bc26ae"])
                { Section = "findings" },
            new("qualified", QualifiedClaim,
                ["src-4-471-47a2656a865c4664", "src-4-999-b95837048d371dae"])
                { Section = "findings" },
            new("first-order-control",
                "Yazarlar Adam'ı, yalnızca birinci derece gradyanlar gerektiren ve gradyanların birinci ve ikinci moment tahminlerinden uyarlanabilir öğrenme oranları hesaplayan bir yöntem olarak sunmaktadır.",
                ["src-1-3061-5f51631768f3e9eb"])
                { Section = "methods" }
        ];

        JsonArray genericResults = [];
        for (int index = 0; index < claims.Count; index++)
        {
            GeneratedArticleClaim claim = claims[index];
            ArticleSourceSpan[] citedSpans = genericSpans
                .Where(value => claim.SourceIds.Contains(value.SourceId, StringComparer.Ordinal)).ToArray();
            var request = new { language = "tr", claims = new[] { claim }, sourceSpans = citedSpans };
            string file = $"qualification-generic-{claim.ClaimId}.json";
            await WriteProbeAsync(file, new
            {
                status = "request_saved_before_dispatch", request, budget = budget.Snapshot()
            });
            budget.SetPhase($"qualification-generic-{claim.ClaimId}");
            GeneratedVerificationBatch response;
            try
            {
                await using AsyncServiceScope scope = analysis.Services.CreateAsyncScope();
                response = await scope.ServiceProvider.GetRequiredService<IArticleClaimVerifier>()
                    .VerifyAsync("tr", [claim], citedSpans, CancellationToken.None);
            }
            catch (Exception exception)
            {
                await WriteProbeAsync(file, new
                {
                    status = "provider_failed_before_verdict", request,
                    failure = new { type = exception.GetType().FullName, exception.Message },
                    budget = budget.Snapshot(), usage = await ReadUsageAsync(database, before.Calls + index)
                });
                throw;
            }
            JsonArray usage = await ReadUsageAsync(database, before.Calls + index);
            bool expectedSupported = claim.ClaimId != "old-overbroad";
            bool matched = SingletonVerdictMatched(
                response.Verdicts.Select(value => (value.ClaimId, value.Verdict)),
                claim.ClaimId, expectedSupported);
            await WriteProbeAsync(file, new
            {
                status = "verdict_received", request, result = response,
                expectedSupported, matched, budget = budget.Snapshot(), usage
            });
            allCriteriaMatched &= matched;
            Require(response.Model == ServiceAcceptanceBudget.RequiredModel && IsExactUsage(usage),
                "Generic singleton verifier model or usage was not exact and attributable.");
            genericResults.Add(Node(new { request, result = response, usage }));
        }

        IReadOnlyList<ArticleSourceSpan> facultySpans = FacultySpans();
        IReadOnlyList<GeneratedArticleReviewFinding> findings =
        [
            new("old-overbroad", "method", "review_question",
                "Adam algoritması kullanılarak çevrim içi konveks fonksiyon için yakınsama kanıtı ve O(sqrt(T)) mertebesinde pişmanlık (regret) sınırı verilmektedir.",
                "Kendi çalışmanızdaki amaç fonksiyonları konvekslik koşulunu sağlamıyorsa, sunulan O(sqrt(T)) pişmanlık garantisinin ve teorik yakınsama sınırlarının doğrudan geçerli olmayabileceğini göz önünde bulundurdunuz mu?",
                ["work:1:snapshot:1:span:28"]),
            new("qualified", "method", "review_question", QualifiedClaim, ConditionalQuestion,
                ["src-4-471-47a2656a865c4664", "src-4-999-b95837048d371dae"]),
            new("first-order-control", "method", "source_observation",
                "Yazarlar Adam'ı, yalnızca birinci derece gradyanlar gerektiren ve gradyanların birinci ve ikinci moment tahminlerinden uyarlanabilir öğrenme oranları hesaplayan bir yöntem olarak sunmaktadır.",
                null, ["work:1:snapshot:1:span:7"])
        ];
        JsonArray facultyResults = [];
        for (int index = 0; index < findings.Count; index++)
        {
            GeneratedArticleReviewFinding finding = findings[index];
            ArticleSourceSpan[] citedSpans = facultySpans
                .Where(value => finding.SourceIds.Contains(value.SourceId, StringComparer.Ordinal)).ToArray();
            var request = new { role = "method", language = "tr", findings = new[] { finding }, sourceSpans = citedSpans };
            string file = $"qualification-faculty-{finding.FindingId}.json";
            await WriteProbeAsync(file, new
            {
                status = "request_saved_before_dispatch", request, budget = budget.Snapshot()
            });
            budget.SetPhase($"qualification-faculty-{finding.FindingId}");
            GeneratedFacultyAssistantSourceCheck response;
            int usageOffset = before.Calls + claims.Count + index;
            try
            {
                await using AsyncServiceScope scope = analysis.Services.CreateAsyncScope();
                response = await scope.ServiceProvider.GetRequiredService<GeminiFacultyAssistantVerifier>()
                    .VerifyAsync("method", "tr", finding, citedSpans, CancellationToken.None);
            }
            catch (Exception exception)
            {
                await WriteProbeAsync(file, new
                {
                    status = "provider_failed_before_verdict", request,
                    failure = new { type = exception.GetType().FullName, exception.Message },
                    budget = budget.Snapshot(), usage = await ReadUsageAsync(database, usageOffset)
                });
                throw;
            }
            JsonArray usage = await ReadUsageAsync(database, usageOffset);
            bool expectedSupported = finding.FindingId != "old-overbroad";
            bool matched = response.Status == "checked" && response.Verdict is not null &&
                SingletonVerdictMatched([(response.Verdict.FindingId, response.Verdict.Verdict)],
                    finding.FindingId, expectedSupported);
            await WriteProbeAsync(file, new
            {
                status = "verdict_received", request, result = response,
                expectedSupported, matched, budget = budget.Snapshot(), usage
            });
            allCriteriaMatched &= matched;
            Require(response.Model == ServiceAcceptanceBudget.RequiredModel && IsExactUsage(usage),
                "Faculty singleton verifier model or usage was not exact and attributable.");
            facultyResults.Add(Node(new { request, result = response, usage }));
        }

        JsonArray coverageResults = await RunRequestCoverageRegressionAsync(
            analysis, database, budget, before.Calls + claims.Count + findings.Count);
        allCriteriaMatched &= coverageResults.All(value => value?["matched"]?.GetValue<bool>() == true);

        ServiceAcceptanceBudgetSnapshot after = budget.Snapshot();
        Require(after.Calls == before.Calls + 9 && after.UnknownUsageCalls == 0 && !after.DispatchStopped,
            "Qualification regression did not produce exactly nine attributable Gemini calls.");
        JsonArray allUsage = await ReadUsageAsync(database, before.Calls);
        Require(allUsage.Count == 9 && IsExactUsage(allUsage),
            "Qualification regression SQL usage was not exact and attributable.");
        return Node(new
        {
            expected = new { oldOverbroad = "not_supported", qualified = "supported", control = "supported" },
            allCriteriaMatched,
            genericSingletons = genericResults,
            facultySingletons = facultyResults,
            requestCoverageNegatives = coverageResults,
            budgetCallsBefore = before.Calls,
            budgetCallsAfter = after.Calls,
            usage = allUsage
        });
    }

    private static async Task<JsonArray> RunRequestCoverageRegressionAsync(
        WebApplication analysis, string database, ServiceAcceptanceBudget budget, int usageOffset)
    {
        string root = FinalServiceAcceptancePreflight.FindRoot();
        string path = Path.Combine(root, "docs", "service-acceptance-20260914-v4", "result.json");
        const string expectedSha = "87491e17cd00a42475f2daf77ddc8641efcc7c90e5ee86573082b8a40a1b266a";
        string sourceSha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)))
            .ToLowerInvariant();
        Require(sourceSha == expectedSha, "The frozen v4 result fixture hash changed.");
        JsonObject frozen = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        string[] names = ["facultyMethods", "facultyTeaching", "facultyIssues"];
        JsonArray results = [];
        for (int index = 0; index < names.Length; index++)
        {
            string name = names[index];
            JsonObject source = frozen[name]!.AsObject();
            JsonObject completed = source["completed"]!.AsObject();
            FacultyAssistantAnalysisReport report = completed["report"]!
                .Deserialize<FacultyAssistantAnalysisReport>(JsonOptions)!;
            string query = source["query"]!.GetValue<string>();
            string sourceRunId = completed["runId"]!.GetValue<string>();
            var request = new
            {
                sourceLifecycle = "service-acceptance-20260914-v4",
                sourceResultSha256 = sourceSha,
                sourceFacultyRunId = sourceRunId,
                mode = report.Mode,
                language = report.Language,
                query,
                retainedItems = report.Items,
                frozenReport = report
            };
            string file = $"request-coverage-negative-{name}.json";
            await WriteProbeAsync(file, new
            {
                status = "request_saved_before_dispatch", request, budget = budget.Snapshot()
            });
            budget.SetPhase($"request-coverage-negative-{name}");
            GeneratedFacultyRequestCoverage response;
            try
            {
                await using AsyncServiceScope scope = analysis.Services.CreateAsyncScope();
                response = await scope.ServiceProvider.GetRequiredService<IFacultyRequestCoverageVerifier>()
                    .VerifyAsync(report.Mode, report.Language, query, report.Items, CancellationToken.None);
            }
            catch (Exception exception)
            {
                await WriteProbeAsync(file, new
                {
                    status = "provider_failed_before_coverage", request,
                    failure = new { type = exception.GetType().FullName, exception.Message },
                    budget = budget.Snapshot(), usage = await ReadUsageAsync(database, usageOffset + index)
                });
                throw;
            }
            JsonArray usage = await ReadUsageAsync(database, usageOffset + index);
            await WriteProbeAsync(file, new
            {
                status = "coverage_received_before_validation", request, result = response,
                expected = "not_fulfilled", budget = budget.Snapshot(), usage
            });
            bool valid = ValidCoverage(response, report.Items.Count);
            bool matched = valid && response.Status != "fulfilled";
            await WriteProbeAsync(file, new
            {
                status = "coverage_received", request, result = response,
                expected = "not_fulfilled", valid, matched, budget = budget.Snapshot(), usage
            });
            Require(response.Model == ServiceAcceptanceBudget.RequiredModel && IsExactUsage(usage),
                $"{name} request-coverage model or usage was not exact and attributable.");
            results.Add(Node(new
            {
                name, sourceLifecycle = "service-acceptance-20260914-v4",
                sourceResultSha256 = sourceSha, sourceFacultyRunId = sourceRunId,
                expected = "not_fulfilled", valid, matched, request, result = response, usage
            }));
        }
        return results;
    }

    private static bool ValidCoverage(GeneratedFacultyRequestCoverage? value, int itemCount)
    {
        if (value is null || value.Model != ServiceAcceptanceBudget.RequiredModel ||
            value.PromptVersion != FacultyRequestCoveragePrompt.Version ||
            value.Status is not ("fulfilled" or "partial" or "unanswered") ||
            value.Requirements is null || value.Requirements.Count is < 1 or > 8)
            return false;
        for (int index = 0; index < value.Requirements.Count; index++)
        {
            GeneratedFacultyRequestCoverageRequirement? requirement = value.Requirements[index];
            if (requirement is null || requirement.RequirementId != $"requirement-{index + 1}" ||
                string.IsNullOrWhiteSpace(requirement.Requirement) || requirement.Requirement.Length > 500 ||
                string.IsNullOrWhiteSpace(requirement.Reason) || requirement.Reason.Length > 500 ||
                requirement.Status is not ("fulfilled" or "partial" or "unanswered") ||
                requirement.ItemIndexes is null ||
                requirement.ItemIndexes.Any(itemIndex => itemIndex < 1 || itemIndex > itemCount) ||
                requirement.ItemIndexes.Distinct().Count() != requirement.ItemIndexes.Count ||
                requirement.Status == "unanswered" && requirement.ItemIndexes.Count != 0 ||
                requirement.Status != "unanswered" && requirement.ItemIndexes.Count == 0)
                return false;
        }
        string expected = value.Requirements.All(requirement => requirement.Status == "fulfilled") ? "fulfilled" :
            value.Requirements.All(requirement => requirement.Status == "unanswered") ? "unanswered" : "partial";
        return value.Status == expected;
    }

    private static bool SingletonVerdictMatched(IEnumerable<(string Id, string Verdict)> values,
        string expectedId, bool expectedSupported)
    {
        (string Id, string Verdict) verdict = values.Single();
        return verdict.Id == expectedId && (verdict.Verdict == "supported") == expectedSupported;
    }

    private static bool IsExactUsage(JsonArray usage) => usage.Count > 0 && usage.All(value =>
        value?["returnedModel"]?.GetValue<string>() == ServiceAcceptanceBudget.RequiredModel &&
        value["totalTokenCount"] is not null && value["estimatedUsd"] is not null);

    private static IReadOnlyList<ArticleSourceSpan> GenericSpans() =>
    [
        new("src-4-471-47a2656a865c4664", 4, 471, 999, RegretText),
        new("src-4-999-b95837048d371dae", 4, 999, 1500, AssumptionText),
        new("src-4-2037-50c75eeda7bc26ae", 4, 2037, 2561, AverageRegretText),
        new("src-1-3061-5f51631768f3e9eb", 1, 3061, 3537, FirstOrderText)
    ];

    private static IReadOnlyList<ArticleSourceSpan> FacultySpans() =>
    [
        new("work:1:snapshot:1:span:28", 4, 471, 999, RegretText),
        new("src-4-471-47a2656a865c4664", 4, 471, 999, RegretText),
        new("src-4-999-b95837048d371dae", 4, 999, 1500, AssumptionText),
        new("work:1:snapshot:1:span:7", 1, 3061, 3537, FirstOrderText)
    ];

    private static async Task<JsonArray> ReadUsageAsync(string database, int skip)
    {
        await using SqlConnection connection = new(database);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT AttemptId,StartedAt,CompletedAt,RequestedModel,ReturnedModel,Outcome,HttpStatus,
                PromptTokenCount,CachedTokenCount,CandidateTokenCount,ThoughtTokenCount,TotalTokenCount,
                PricingVersion,EstimatedUsd
            FROM [analysis].[GeminiUsageAttempts]
            ORDER BY StartedAt,AttemptId OFFSET @skip ROWS
            """;
        command.Parameters.AddWithValue("@skip", skip);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        JsonArray rows = [];
        while (await reader.ReadAsync())
            rows.Add(Node(new
            {
                attemptId = reader.GetGuid(0), startedAt = reader.GetDateTime(1), completedAt = reader.GetDateTime(2),
                requestedModel = reader.GetString(3), returnedModel = reader.GetString(4), outcome = reader.GetString(5),
                httpStatus = reader.GetInt32(6), promptTokenCount = reader.GetInt64(7), cachedTokenCount = reader.GetInt64(8),
                candidateTokenCount = reader.GetInt64(9), thoughtTokenCount = reader.GetInt64(10),
                totalTokenCount = reader.GetInt64(11), pricingVersion = reader.GetString(12), estimatedUsd = reader.GetDecimal(13)
            }));
        return rows;
    }

    private static JsonObject Node(object value) =>
        JsonSerializer.SerializeToNode(value, JsonOptions)!.AsObject();

    private static Task WriteProbeAsync(string file, object value)
    {
        string root = FinalServiceAcceptancePreflight.FindRoot();
        string path = Path.Combine(root, "docs", Program.RunId, file);
        return FinalAcceptanceArtifacts.WriteAtomicAsync(path,
            JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string RegretText = "using regret, that is the sum of all the previous difference between the online prediction ft(θt ) and\n(θ ∗ ) for all the previous steps. Concretely, we have the regret is\n\nthe best ﬁxed point parameter ft\ndeﬁned as:\n\nR(T) =\n\nT\n∑\n[ft (θt ) −\n\nt=1\n\nft\n\n∗\n(θ )]\n\n(5)\n\n∗\nθ = argmin\nθ∈X\n\nT\n∑\n\nft\n\n(θ)\n\n(6)\n\nt=1\n√\nWe give a convergence proof and a regret O( T) for the online convex function using the Adam\nalgorithm. Our result is comparable to the best known bound for this general convex online learning\nproblem.\n≤ G,\nTheorem 4.1.";
    private const string AssumptionText = " Assume that the functions ft have bounded gradients, ‖∇ft (θ)‖2\nd\n‖∇ft (θ)‖∞ ≤ G∞ for all θ ∈ R and distance between any θt generated by Adam is bounded,\n‖θn −\nθn ‖∞ ≤ D∞ for any m,n ∈ {1,...,T}. With γt = 1/t, Adam achieves\nθm ‖2 ≤ D, ‖θm −\nthe following guarantee, for all T ≥ 1.\n∑ d\n\nR(T) ≤ (\n\nD\n\n2\n\n2η\n\n+ η)\n\ni=1\n\n‖g1:T,i ‖2 + 2D\n\n2 1 + η\n\nη\n\nlog(T) +\n\n3\n\n2\n\n2\ndηD\n∞\n\nG∞\n\nSimilarly to AdaGrad, when the data features are sparse, the summation term can be much smaller\n∑\nd\n\n√\nthan its upper bound\n";
    private const string AverageRegretText = "θt generated by Adam is bounded,\n‖θm − θn ‖∞ ≤ D∞ for any m,n ∈ {1,...,T}. With γt = 1/t, Adam achieves the following\nguarantee, for all T ≥ 1.\n\nR(T)\n\nT\n\n1\n= O(√\n\nT\n\n)\n\nThis result can be obtained by using Theorem 4.1 and\nR(T)\nlimT→∞ T\n\n∑\nd\n\ni=1\n\n√\n‖g1:T,i ‖2 ≤ dG∞ T.\n\nThus,\n\n= 0.\n\n5\n\nRELATED WORK\n\nOptimization methods bearing a direct relation to Adam include RProp Riedmiller & Braun (1992),\nRMSProp Tieleman & Hinton (2012); Graves et al. (2013) and AdaGrad Duchi et al. (2011); these\nrelationships are discussed below.";
    private const string FirstOrderText = "We propose Adam, a method for efﬁcient stochastic optimization that only requires ﬁrst-order gradi-\nentsandrequireslittlememory. Themethodcomputesindividualadaptivelearningratesfordifferent\nparameters from estimates of ﬁrst and second moments of the gradients; the name Adam is derived\nfrom adaptive moment estimation. Our method is designed to combine the advantages of two re-\ncently popular methods: AdaGrad Duchi et al. (2011), which works well with sparse gradients, and\n";
}
