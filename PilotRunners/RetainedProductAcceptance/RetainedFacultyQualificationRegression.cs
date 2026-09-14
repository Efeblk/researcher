using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using ResearcherAnalysisService.Analysis;

namespace ServiceAcceptancePilot;

internal static class RetainedFacultyQualificationRegression
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<JsonObject> RunAsync(IServiceProvider services, string database)
    {
        Dictionary<int, ArticleSourceSpan> spans = await ReadSpansAsync(database);
        Probe[] probes =
        [
            new("lead-only-bound", false, "source_observation",
                "Yazarlar Adam algoritması için O(karekök T) pişmanlık sınırını çevrim içi dışbükey fonksiyonlar çerçevesinde kuramsal olarak kanıtlamıştır.",
                null, [spans[471]]),
            new("fully-qualified-bound", true, "source_observation",
                "Yazarlar, çevrim içi dışbükey optimizasyon çerçevesinde; gradyanların sınırlı, Adam tarafından üretilen herhangi iki parametre vektörü arasındaki uzaklığın sınırlı ve γt=1/t olduğu varsayımları altında Adam için bir pişmanlık sınırı sunmaktadır.",
                null, [spans[471], spans[999]]),
            new("necessary-condition", true, "source_observation",
                "Sunulan kuramsal sonucun varsayımları arasında gradyanların ve Adam tarafından üretilen herhangi iki parametre vektörü arasındaki uzaklığın sınırlı olması bulunur.",
                null, [spans[999]]),
            new("uncited-nonstationary-premise", false, "review_question",
                "Yazarlar, optimizasyonun sonlarına doğru gradyanların seyrekleşme eğiliminde olduğu durumlarda en iyi sonuçların küçük β2 değerleri ve yanlılık düzeltmesi ile elde edildiğini bildirmektedir.",
                "Çalışmanızda gradyanların seyrekleştiği veya durağan olmayan durumlar varsa, küçük β2 değerlerinin ve yanlılık düzeltmesinin etkisini kontrol ettiniz mi?",
                [spans[1040]]),
            new("supported-sparse-bias-control", true, "review_question",
                "Yazarlar, optimizasyonun sonlarına doğru gradyanların seyrekleşme eğiliminde olduğu durumlarda en iyi sonuçların küçük β2 değerleri ve yanlılık düzeltmesi ile elde edildiğini bildirmektedir.",
                "Çalışmanızda optimizasyonun sonuna doğru gradyanlar seyrekleşiyorsa, küçük β2 değerlerinin ve yanlılık düzeltmesinin etkisini kontrol ettiniz mi?",
                [spans[1040]])
        ];
        IFacultyAssistantVerifier verifier = services.GetRequiredService<IFacultyAssistantVerifier>();
        JsonArray results = [];
        foreach (Probe probe in probes)
        {
            GeneratedArticleReviewFinding finding = new(probe.Id, "method", probe.Kind, probe.Basis,
                probe.Response, probe.Spans.Select(x => x.SourceId).ToArray());
            var request = new { role = "method", language = "tr", finding, sourceSpans = probe.Spans };
            await WriteAsync($"qualification-{probe.Id}-request.json", new
            {
                status = "request_saved_before_dispatch", probe.ExpectedSupported, request
            });
            GeneratedFacultyAssistantSourceCheck check = await verifier.VerifyAsync(
                "method", "tr", finding, probe.Spans, CancellationToken.None);
            bool supported = check.Status == "checked" && check.Verdict?.Verdict == "supported";
            bool matched = check.Status == "checked" && supported == probe.ExpectedSupported;
            JsonObject response = JsonSerializer.SerializeToNode(new
            {
                status = "response_saved_before_classification", probe.ExpectedSupported,
                matched, request, check
            }, JsonOptions)!.AsObject();
            await WriteAsync($"qualification-{probe.Id}-response.json", response);
            results.Add(response);
        }
        return new()
        {
            ["allCriteriaMatched"] = results.All(x => x!["matched"]!.GetValue<bool>()),
            ["facultyVerifier"] = results,
            ["freshCalls"] = probes.Length,
            ["totalCases"] = probes.Length
        };
    }

    internal static async Task<int> ValidateReadOnlySqlAsync(string database)
    {
        Dictionary<int, ArticleSourceSpan> spans = await ReadSpansAsync(database);
        Require(spans.Count == 3 && spans.Values.All(x => !string.IsNullOrWhiteSpace(x.SourceId)),
            "V14 qualification spans were not resolved.");
        return spans.Count;
    }

    private static async Task<Dictionary<int, ArticleSourceSpan>> ReadSpansAsync(string database)
    {
        await using SqlConnection connection = new(database);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT sp.SourceId,sp.PageNumber,sp.StartOffset,sp.EndOffset,sp.[Text]
            FROM [analysis].[ArticleSourceSpans] sp
            INNER JOIN [analysis].[ArticleSourceSnapshots] ss ON ss.Id=sp.ArticleSourceSnapshotId
            INNER JOIN [core].[CanonicalWorks] cw ON cw.Id=ss.CanonicalWorkId
            WHERE cw.NormalizedDoi='10.48550/arxiv.1412.6980'
              AND sp.StartOffset IN (471,999,1040)
            ORDER BY sp.StartOffset
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        Dictionary<int, ArticleSourceSpan> values = [];
        while (await reader.ReadAsync())
            values.Add(reader.GetInt32(2), new(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetString(4)));
        Require(values.Count == 3 && values[471].SourceId == "src-4-471-47a2656a865c4664" &&
            values[999].SourceId == "src-4-999-b95837048d371dae" &&
            values[1040].SourceId == "src-8-1040-2a494c9582eec2d8",
            "Exact V14 qualification sources changed.");
        return values;
    }

    private static Task WriteAsync(string file, object value) => FinalAcceptanceArtifacts.WriteAtomicAsync(
        Path.Combine(RetainedAcceptancePreflight.FindRoot(), "docs", Program.RunId, file),
        JsonSerializer.Serialize(value, JsonOptions));

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed record Probe(string Id, bool ExpectedSupported, string Kind, string Basis,
        string? Response, IReadOnlyList<ArticleSourceSpan> Spans);
}
