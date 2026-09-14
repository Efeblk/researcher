using System.Text.Json.Nodes;

namespace ResearcherAnalysisService.Analysis;

public static class FacultyAssistantVerificationPrompt
{
    public const string Version = "faculty-evidence-assistant-verification-v7";
    public const string Instructions = ArticleReviewVerificationPrompt.Instructions + """

        Faculty-assistant checks also require every material qualification, mathematical symbol, variable role,
        limiting condition, and proposed response framing to remain supported. Do not reconstruct flattened or garbled
        math. Proposed findings, their bases, and private context are never evidence. An article author's procedure does
        not establish that the faculty member performed the same action in different work. A question about the faculty
        member's unknown conduct must be conditional unless the cited source directly describes that person and work.
        A teaching adaptation must remain an optional suggestion and must not turn one paper's approach into a universal
        requirement. Changed numbers, symbols, conditions, dropped qualifiers, unsupported generalization, invented
        user conduct, or an unconditional presupposition cannot receive a supported verdict. Check the basis and
        response independently against this item's own cited sources. Every factual premise used in the response must
        also be stated consistently in the basis and supported by those same citations. If a response offers
        alternatives joined by "or" or "veya", every branch carrying a factual premise requires its own support; one
        supported branch cannot make an uncited alternative supported.
        For a theoretical guarantee or advice about conditions to check, require cited evidence for every material
        assumption, restriction, parameter schedule, and scope controlling the conclusion. An introduction or summary
        assertion of a result is not evidence of the theorem's assumptions. A claim that explicitly drops a cited
        condition or broadens the guarantee is unsupported. When a material claimed fact or required condition is
        absent, itself depends on incomplete or garbled extraction, or the evidence is insufficient to show that a
        proposed checklist is complete, use uncertain; never infer or invent the missing conditions. Unrelated damaged
        math or layout elsewhere in a cited span must not invalidate required conditions stated in
        independently clear prose. Exact formulas and symbol relationships still require unambiguous support. Exact
        formula reconstruction from damaged layout remains forbidden. Keep theoretical and empirical assertions attributed to the authors or study rather than
        accepting them as universal facts.
        Apply the formal-bound rule to every item kind, including an attributed source_observation. For example,
        "the authors proved an O(sqrt(T)) bound" is uncertain when its own citations contain only the theorem lead-in
        or conclusion and omit material bounded-gradient, bounded-iterate, or schedule assumptions. It can be
        supported when the same item's citations and basis include the result and all material conditions. Do not
        reject a narrower cited statement that a bound depends on one necessary condition merely because it does not
        claim sufficiency, completeness, or the bound itself; a neutral observation that the paper discusses theory
        likewise does not claim that a formal result was proved.
        For a teaching_adaptation only, the no-invented-premise rule has one narrow exception: hypothetical scenario
        assumptions explicitly labelled as hypothetical may be invented solely in the response to construct the
        requested learner exercise. This narrow allowance does not
        apply to the basis: every basis premise must remain directly source-supported. Never accept a hypothetical
        teaching assumption as a result, fact, input, or condition reported by the paper.
        Audit the entire basis and response before choosing the verdict. The reason must compactly identify every material
        source mismatch found, rather than stopping after the first mismatch. Organizational references such as "the
        first paper" and "the second paper" only identify items in the supplied set and are not research facts that must
        appear literally in a source. Names, authorship, titles, study attribution, and every substantive claim about a
        paper still require direct support from the item's own citations.
        """;

    public static JsonObject CreateSchema(IEnumerable<string> findingIds) =>
        ArticleReviewVerificationPrompt.CreateSchema(findingIds);
}
