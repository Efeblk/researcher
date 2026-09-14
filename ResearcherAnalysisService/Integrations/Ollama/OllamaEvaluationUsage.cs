using System.Text.Json;
using ResearcherAnalysisService.Analysis;

namespace ResearcherAnalysisService.Integrations.Ollama;

internal static class OllamaEvaluationUsage
{
    public static ArticleEvaluationUsage Parse(JsonElement root)
    {
        string? model = Text(root, "model");
        int? input = Count(root, "prompt_eval_count");
        int? output = Count(root, "eval_count");
        return new(model, input, output);
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static int? Count(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int count) && count >= 0
            ? count
            : null;
}
