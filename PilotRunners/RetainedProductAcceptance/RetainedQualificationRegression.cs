using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Data.SqlClient;
using ResearcherAnalysisService.Analysis;

namespace ServiceAcceptancePilot;

internal static class RetainedQualificationRegression
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<JsonObject> RunAsync(string database, bool writeArtifacts = true)
    {
        (ArticleSourceSpan lead, ArticleSourceSpan conditions) = await ReadAdamSpansAsync(database);
        Probe[] probes =
        [
            new("lead-in-only", false,
                "Yazarlar Adam algoritması için O(karekök T) pişmanlık (regret) sınırını çevrim içi dışbükey (online convex) fonksiyonlar çerçevesinde kuramsal olarak kanıtlamıştır.",
                null, [lead]),
            new("fully-qualified", true,
                "Yazarlar, çevrim içi dışbükey optimizasyon çerçevesinde gradyanların ve Adam iterasyonları arasındaki uzaklıkların sınırlı olduğu, ayrıca γt=1/t seçildiği koşullar altında biçimsel bir yakınsama ve pişmanlık garantisi sunmaktadır.",
                null, [lead, conditions]),
            new("necessary-condition", true,
                "Sunulan kuramsal sonucun varsayımları arasında gradyanların ve Adam iterasyonları arasındaki uzaklıkların sınırlı olması bulunur.",
                null, [conditions])
        ];
        JsonArray review = await LoadReusedReviewAsync(probes.Take(2).ToArray(), writeArtifacts);
        JsonArray remainingReview = await LoadReusedV8Async(
            "review", probes.Skip(2).ToArray(), writeArtifacts);
        foreach (JsonNode? item in remainingReview) review.Add(item?.DeepClone());
        JsonArray faculty = await LoadReusedV8Async("faculty", probes, writeArtifacts);
        bool matched = review.Concat(faculty).All(value => value!["matched"]!.GetValue<bool>());
        return new() { ["allCriteriaMatched"] = matched, ["reviewVerifier"] = review,
            ["facultyVerifier"] = faculty, ["freshCalls"] = 0, ["reusedCalls"] = 6,
            ["totalCases"] = 6 };
    }

    private static async Task<JsonArray> LoadReusedReviewAsync(IReadOnlyList<Probe> probes,
        bool writeArtifacts)
    {
        if (probes.Count != 2 || probes[0].Id != "lead-in-only" || probes[1].Id != "fully-qualified")
            throw new InvalidOperationException("The V7 reuse cases changed.");
        string root = RetainedAcceptancePreflight.FindRoot();
        string source = Path.Combine(root, "docs", "service-acceptance-20260914-v7");
        (string File, string Sha, string? RequestFile, string? RequestSha)[] files =
        [
            ("qualification-review-lead-in-only.json",
                "57ddebdc10b4cb53dd1b4cf13b9760437ba03b61fc2119bb457e91b6714c208e", null, null),
            ("qualification-review-fully-qualified.json.pending-e6dbf3bab49644e8b37620bcb723f147",
                "352f2c342ef7d89524a15e23cee898ef43488cf106ca8840817070490aab2146",
                "qualification-review-fully-qualified.json",
                "c83d0c809cbb4a3029ae2d5644219dfb43af03891fe57f6f2e4538e44d6fbe4d")
        ];
        JsonArray results = [];
        for (int index = 0; index < probes.Count; index++)
        {
            Probe probe = probes[index];
            var expectedRequest = new
            {
                role = "claim_evidence", language = "tr",
                findings = new[] { new GeneratedArticleReviewFinding(probe.Id, "claim_evidence",
                    "source_observation", probe.Basis, probe.Question,
                    probe.Spans.Select(value => value.SourceId).ToArray()) },
                sourceSpans = probe.Spans
            };
            string classifiedPath = Path.Combine(source, files[index].File);
            Require(await HashAsync(classifiedPath) == files[index].Sha,
                $"Frozen V7 {probe.Id} classified evidence changed.");
            if (files[index].RequestFile is not null)
                Require(await HashAsync(Path.Combine(source, files[index].RequestFile!)) ==
                    files[index].RequestSha, $"Frozen V7 {probe.Id} request evidence changed.");
            JsonObject classified = JsonNode.Parse(await File.ReadAllTextAsync(classifiedPath))!.AsObject();
            GeneratedArticleReviewVerification response = classified["response"]!
                .Deserialize<GeneratedArticleReviewVerification>(JsonOptions)!;
            GeneratedArticleReviewVerdict verdict = response.Verdicts.Single();
            bool matched = classified["status"]!.GetValue<string>() == "classified" &&
                classified["expectedSupported"]!.GetValue<bool>() == probe.ExpectedSupported &&
                classified["matched"]!.GetValue<bool>() &&
                JsonNode.DeepEquals(classified["request"],
                    JsonSerializer.SerializeToNode(expectedRequest, JsonOptions)) &&
                response.Model == ServiceAcceptanceBudget.RequiredModel && verdict.FindingId == probe.Id &&
                (verdict.Verdict == "supported") == probe.ExpectedSupported;
            Require(matched, $"Frozen V7 {probe.Id} evidence does not match the V10 case.");
            JsonObject item = JsonSerializer.SerializeToNode(new
            {
                probe.Id, probe.ExpectedSupported, matched, reusedFromRunId = "service-acceptance-20260914-v7",
                classifiedArtifact = files[index].File, classifiedSha256 = files[index].Sha,
                requestArtifact = files[index].RequestFile, requestSha256 = files[index].RequestSha,
                request = expectedRequest, response
            }, JsonOptions)!.AsObject();
            if (writeArtifacts)
                await WriteAsync($"qualification-review-{probe.Id}-reused.json", item);
            results.Add(item);
        }
        return results;
    }

    private static async Task<JsonArray> LoadReusedV8Async(string kind, IReadOnlyList<Probe> probes,
        bool writeArtifacts)
    {
        Dictionary<string, FrozenCase> files = new(StringComparer.Ordinal)
        {
            ["review:necessary-condition"] = new(
                "qualification-review-necessary-condition-request.json",
                "91f135e07d443f9620a7a444109e85bd1232abd6fe2c580771f241c6668cbfb3",
                "qualification-review-necessary-condition-response.json",
                "fef323cb7c03d320bdcbb9c02cd2e214e808833bbdbf12df0b370d0d4b55f10a",
                "qualification-review-necessary-condition-classified.json",
                "064260636a7bbf4c008141518f6248f8821ed87c31dd703898b306cd616f4438"),
            ["faculty:lead-in-only"] = new(
                "qualification-faculty-lead-in-only-request.json",
                "c5503f0da242c792b48152f792c9dc81e93f9c424a9aa3726569ce5ad6a06795",
                "qualification-faculty-lead-in-only-response.json",
                "0f2fd9124ab7485bbce9e98bd5d22404cd2636d4afd2d1e01fc19898b9002958",
                "qualification-faculty-lead-in-only-classified.json",
                "f99429b27cef2cf01ad86a3a28d55e3c486676e8481408f74e449e219d1f55a9"),
            ["faculty:fully-qualified"] = new(
                "qualification-faculty-fully-qualified-request.json",
                "8284482a99bc101d5f0e8c02ae8c12058c3f91ae415b602c077bb9fdc92ab67e",
                "qualification-faculty-fully-qualified-response.json",
                "b5acf7d03b67a42a5ae5c57e72f69a229c1acdde24e547f3c1906fd6bd8363e2",
                "qualification-faculty-fully-qualified-classified.json",
                "e4bd6cfd3b77a0f45a421da2099eb0fdf3108a48e2ffe637d8df7600e902e852"),
            ["faculty:necessary-condition"] = new(
                "qualification-faculty-necessary-condition-request.json",
                "347fd89aee117a00f9a630cacb53d59df3a65e394d4ccbb58fd7918b61fc75ca",
                "qualification-faculty-necessary-condition-response.json",
                "613def0be58517d327b1ce1cf2c0c5aac87634b8a8b4397a708ef2fa73db6372",
                "qualification-faculty-necessary-condition-classified.json",
                "db4dfa77ceaa9cb66dca237ed63266515ef062227c86ce1f541c195594fe9470")
        };
        string root = RetainedAcceptancePreflight.FindRoot();
        string source = Path.Combine(root, "docs", "service-acceptance-20260914-v8");
        JsonArray results = [];
        foreach (Probe probe in probes)
        {
            FrozenCase file = files[$"{kind}:{probe.Id}"];
            string role = kind == "faculty" ? "method" : "claim_evidence";
            var expectedRequest = new
            {
                role, language = "tr",
                findings = new[] { new GeneratedArticleReviewFinding(probe.Id, role,
                    "source_observation", probe.Basis, probe.Question,
                    probe.Spans.Select(value => value.SourceId).ToArray()) },
                sourceSpans = probe.Spans
            };
            string requestPath = Path.Combine(source, file.RequestFile);
            string responsePath = Path.Combine(source, file.ResponseFile);
            string classifiedPath = Path.Combine(source, file.ClassifiedFile);
            Require(await HashAsync(requestPath) == file.RequestSha &&
                await HashAsync(responsePath) == file.ResponseSha &&
                await HashAsync(classifiedPath) == file.ClassifiedSha,
                $"Frozen V8 {kind} {probe.Id} evidence changed.");
            JsonObject requestArtifact = JsonNode.Parse(await File.ReadAllTextAsync(requestPath))!.AsObject();
            JsonObject responseArtifact = JsonNode.Parse(await File.ReadAllTextAsync(responsePath))!.AsObject();
            JsonObject classified = JsonNode.Parse(await File.ReadAllTextAsync(classifiedPath))!.AsObject();
            GeneratedArticleReviewVerification response = classified["response"]!
                .Deserialize<GeneratedArticleReviewVerification>(JsonOptions)!;
            GeneratedArticleReviewVerdict verdict = response.Verdicts.Single();
            bool matched = requestArtifact["status"]!.GetValue<string>() == "request_saved_before_dispatch" &&
                responseArtifact["status"]!.GetValue<string>() == "response_saved_before_classification" &&
                classified["status"]!.GetValue<string>() == "classified" &&
                JsonNode.DeepEquals(requestArtifact["request"],
                    JsonSerializer.SerializeToNode(expectedRequest, JsonOptions)) &&
                JsonNode.DeepEquals(responseArtifact["request"], classified["request"]) &&
                JsonNode.DeepEquals(responseArtifact["response"], classified["response"]) &&
                classified["expectedSupported"]!.GetValue<bool>() == probe.ExpectedSupported &&
                classified["matched"]!.GetValue<bool>() && response.Model == ServiceAcceptanceBudget.RequiredModel &&
                verdict.FindingId == probe.Id && (verdict.Verdict == "supported") == probe.ExpectedSupported;
            Require(matched, $"Frozen V8 {kind} {probe.Id} evidence does not match the V10 case.");
            JsonObject item = JsonSerializer.SerializeToNode(new
            {
                probe.Id, probe.ExpectedSupported, matched, reusedFromRunId = "service-acceptance-20260914-v8",
                requestArtifact = file.RequestFile, requestSha256 = file.RequestSha,
                responseArtifact = file.ResponseFile, responseSha256 = file.ResponseSha,
                classifiedArtifact = file.ClassifiedFile, classifiedSha256 = file.ClassifiedSha,
                request = expectedRequest, response
            }, JsonOptions)!.AsObject();
            if (writeArtifacts)
                await WriteAsync($"qualification-{kind}-{probe.Id}-reused.json", item);
            results.Add(item);
        }
        return results;
    }

    private static async Task<string> HashAsync(string path) => Convert.ToHexString(
        SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();

    private static async Task<(ArticleSourceSpan Lead, ArticleSourceSpan Conditions)> ReadAdamSpansAsync(string database)
    {
        await using SqlConnection connection = new(database);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT sp.Id,sp.SourceId,sp.PageNumber,sp.StartOffset,sp.EndOffset,sp.[Text]
            FROM [analysis].[ArticleSourceSpans] sp
            INNER JOIN [analysis].[ArticleSourceSnapshots] ss ON ss.Id=sp.ArticleSourceSnapshotId
            INNER JOIN [core].[CanonicalWorks] cw ON cw.Id=ss.CanonicalWorkId
            WHERE cw.NormalizedDoi='10.48550/arxiv.1412.6980' AND sp.StartOffset IN (471,999)
            ORDER BY sp.StartOffset
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        List<ArticleSourceSpan> spans = [];
        while (await reader.ReadAsync())
            spans.Add(new(reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5)));
        if (spans.Count != 2)
            throw new InvalidOperationException("Exact Adam qualification spans 28/29 were not uniquely resolved.");
        return (spans[0], spans[1]);
    }

    internal static async Task<int> ValidateReadOnlySqlAsync(string database)
    {
        (ArticleSourceSpan lead, ArticleSourceSpan conditions) = await ReadAdamSpansAsync(database);
        Require(lead.StartOffset == 471 && conditions.StartOffset == 999 &&
            lead.SourceId.Length > 0 && conditions.SourceId.Length > 0,
            "Qualification source SQL returned unexpected retained spans.");
        return 2;
    }

    private static Task WriteAsync(string file, object value)
    {
        string path = Path.Combine(RetainedAcceptancePreflight.FindRoot(), "docs", Program.RunId, file);
        return FinalAcceptanceArtifacts.WriteAtomicAsync(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private sealed record Probe(string Id, bool ExpectedSupported, string Basis, string? Question,
        IReadOnlyList<ArticleSourceSpan> Spans);

    private sealed record FrozenCase(string RequestFile, string RequestSha,
        string ResponseFile, string ResponseSha, string ClassifiedFile, string ClassifiedSha);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
