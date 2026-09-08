using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.OpenAi;

public sealed class OpenAiReportGenerator(HttpClient client, IOptions<AiOptions> options)
    : IResearcherReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedFindings> GenerateAsync(AnalyzeResearcherRequest request, CancellationToken cancellationToken)
    {
        AiOptions settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Model))
            throw new AnalysisUnavailableException("Configure Ai:ApiKey and Ai:Model in the analysis service.");

        using HttpRequestMessage message = new(HttpMethod.Post, "https://api.openai.com/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        message.Content = JsonContent.Create(new
        {
            model = settings.Model,
            store = false,
            max_output_tokens = settings.MaxOutputTokens,
            instructions = ReportPrompt.Instructions,
            input = JsonSerializer.Serialize(new
            {
                request.Language,
                request.TotalPublicationCount,
                Publications = request.Publications.Select(publication => new
                {
                    publication.Id, publication.Title, publication.Year,
                    publication.Abstract, publication.Keywords
                })
            }, JsonOptions),
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "researcher_analysis",
                    strict = true,
                    schema = ReportPrompt.Schema
                }
            }
        });

        // One bounded request, with no automatic retries that could duplicate paid work.
        using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("AI provider request failed.", null, response.StatusCode);

        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            if (root.GetProperty("status").GetString() != "completed")
                throw new InvalidAnalysisException();

            List<string> texts = [];
            foreach (JsonElement item in root.GetProperty("output").EnumerateArray())
            {
                if (item.GetProperty("type").GetString() != "message")
                    continue;
                foreach (JsonElement content in item.GetProperty("content").EnumerateArray())
                {
                    string? type = content.GetProperty("type").GetString();
                    if (type == "refusal")
                        throw new InvalidAnalysisException();
                    if (type == "output_text")
                        texts.Add(content.GetProperty("text").GetString() ?? string.Empty);
                }
            }

            if (texts.Count != 1)
                throw new InvalidAnalysisException();
            AnalysisFindings findings = JsonSerializer.Deserialize<AnalysisFindings>(texts[0], JsonOptions)
                ?? throw new InvalidAnalysisException();
            return new GeneratedFindings(findings, root.GetProperty("model").GetString() ?? settings.Model,
                ReportPrompt.Version);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidAnalysisException();
        }
    }
}
