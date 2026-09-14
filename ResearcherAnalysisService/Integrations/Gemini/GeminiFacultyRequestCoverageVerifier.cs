using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed class GeminiFacultyRequestCoverageVerifier(
    GeminiArticleClient client, IOptions<AiOptions> options) : IFacultyRequestCoverageVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedFacultyRequestCoverage> VerifyAsync(
        string mode, string language, string query,
        IReadOnlyList<FacultyAssistantAnswerItem> retainedItems,
        CancellationToken cancellationToken)
    {
        string input = JsonSerializer.Serialize(new
        {
            mode, language, query,
            retainedItems = retainedItems.Select((item, index) => new
            {
                itemIndex = index + 1, item.Kind, item.Basis, item.Response,
                evidenceIds = item.Citations.Select(citation => citation.EvidenceId)
            })
        }, JsonOptions);
        string model = string.IsNullOrWhiteSpace(options.Value.ArticleVerifierModel)
            ? options.Value.ArticleModel : options.Value.ArticleVerifierModel;
        GeminiArticleResult result = await client.GenerateAsync(model,
            FacultyRequestCoveragePrompt.Instructions, input,
            FacultyRequestCoveragePrompt.CreateSchema(retainedItems.Count),
            options.Value.ArticleVerifierMaxOutputTokens, options.Value.ArticleVerifierThinkingLevel,
            cancellationToken);
        try
        {
            Envelope envelope = JsonSerializer.Deserialize<Envelope>(result.Json, JsonOptions)
                ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            return new(envelope.Status, envelope.Requirements, result.Model,
                FacultyRequestCoveragePrompt.Version);
        }
        catch (JsonException)
        {
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
        }
    }

    private sealed record Envelope(
        string Status,
        IReadOnlyList<GeneratedFacultyRequestCoverageRequirement> Requirements);
}
