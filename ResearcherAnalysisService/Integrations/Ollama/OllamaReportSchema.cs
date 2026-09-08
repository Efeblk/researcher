using System.Text.Json.Nodes;
using ResearcherAnalysisService.Analysis;
using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Integrations.Ollama;

internal static class OllamaReportSchema
{
    public static JsonNode Create(List<AnalysisPublication> publications)
    {
        // Use a fresh schema per request: publication IDs must never leak between requests.
        JsonNode schema = JsonNode.Parse(ReportPrompt.Schema.GetRawText())!;
        JsonNode properties = schema["properties"]!;
        properties["researchFocus"]!["maxItems"] = 6;
        properties["writingObservations"]!["maxItems"] = 6;

        JsonNode definitions = schema["$defs"]!;
        JsonNode observation = definitions["observation"]!["properties"]!;
        observation["observation"]!["minLength"] = 1;
        // maxLength: 2000 exceeds Ollama 0.33.3's grammar repetition limit.
        // Keep that limit in the prompt and backend validation, not the sampling grammar.
        observation["evidence"]!["minItems"] = 1;
        observation["evidence"]!["maxItems"] = 5;

        JsonNode evidence = definitions["evidence"]!["properties"]!;
        evidence["publicationId"]!["enum"] = new JsonArray(publications
            .Select(publication => (JsonNode?)JsonValue.Create(publication.Id)).ToArray());
        evidence["quote"]!["minLength"] = 10;
        evidence["quote"]!["maxLength"] = 600;

        JsonNode writingEvidence = definitions["evidence"]!.DeepClone();
        writingEvidence["properties"]!["field"]!["enum"] = new JsonArray("abstract");
        definitions["writingEvidence"] = writingEvidence;
        JsonNode writingObservation = definitions["observation"]!.DeepClone();
        writingObservation["properties"]!["evidence"]!["items"]!["$ref"] = "#/$defs/writingEvidence";
        definitions["writingObservation"] = writingObservation;
        properties["writingObservations"]!["items"]!["$ref"] = "#/$defs/writingObservation";
        if (publications.All(publication => string.IsNullOrWhiteSpace(publication.Abstract)))
            properties["writingObservations"]!["maxItems"] = 0;

        return schema;
    }
}
