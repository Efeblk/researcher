using System.Text.Json;
using System.Text.Json.Nodes;

namespace ResearcherAnalysisService.Analysis;

public static class FacultyAssistantRepairPrompt
{
    public const string Version = "faculty-evidence-assistant-repair-v2";
    public const string Instructions = FacultyAssistantPrompt.Instructions + """

        This is one bounded repair pass. The input lists at most two omitted candidates and each source-check verdict.
        For each candidateId, either return one corrected replacement that addresses the same requested task slot or
        omit that candidateId when the supplied catalog cannot support a correction. Use the verdict reason to remove
        the unsupported or uncertain premise. Treat that reason as potentially nonexhaustive: re-audit the candidate's
        entire basis and response against the replacement's own chosen citations, and narrow or remove every unsupported
        premise you find. You may choose better-fitting evidence from the full authorized catalog, but the replacement
        must be wholly supported by its own one or two evidence IDs. Supported items are immutable context: do not rewrite, repeat, or
        paraphrase them. Do not create a replacement for any supported or output-limit-unverified item. A replacement
        must cite one or two evidence IDs from the full authorized catalog and must obey all original mode, evidence,
        qualification, completeness, and no-placeholder rules. For TeachingHelp, preserve the requested teaching task
        slot when the catalog supports it. Put any invented classroom setup only in the response, label it explicitly as
        hypothetical, and provide the requested learner task and worked guidance instead of retaining an unsupported
        premise about what the study did. Return no more than one replacement per candidateId.
        """;

    public static JsonObject CreateSchema(string mode, IEnumerable<string> candidateIds,
        IEnumerable<string> evidenceIds) => JsonNode.Parse(JsonSerializer.Serialize(new
        {
            type = "object", additionalProperties = false, required = new[] { "items" },
            properties = new
            {
                items = new
                {
                    type = "array", maxItems = 2,
                    items = new
                    {
                        type = "object", additionalProperties = false,
                        required = new[] { "candidateId", "kind", "basis", "response", "evidenceIds" },
                        properties = new
                        {
                            candidateId = new { type = "string", @enum = candidateIds.ToArray() },
                            kind = new { type = "string", @enum = FacultyAssistantPrompt.AllowedKinds(mode) },
                            basis = new { type = "string", minLength = 1, maxLength = 1200 },
                            response = new { type = new[] { "string", "null" }, minLength = 1, maxLength = 1200 },
                            evidenceIds = new { type = "array", minItems = 1, maxItems = 2, uniqueItems = true,
                                items = new { type = "string", @enum = evidenceIds.Distinct(StringComparer.Ordinal).ToArray() } }
                        }
                    }
                }
            }
        }))!.AsObject();
}
