using System.ComponentModel.DataAnnotations;

namespace ResearcherAnalysisService.Configuration;

public sealed class AiOptions
{
    [Required, RegularExpression("^(Ollama|OpenAI)$")]
    public string Provider { get; set; } = "Ollama";

    public string? ApiKey { get; set; } = null;
    public string? Model { get; set; } = null;

    public string OllamaBaseUrl { get; set; } = "http://localhost:11434/";

    [Range(4096, 131072)]
    public int OllamaContextTokens { get; set; } = 8192;

    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 180;

    [Range(512, 16000)]
    public int MaxOutputTokens { get; set; } = 2000;
}
