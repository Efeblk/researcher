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

    [Required, RegularExpression("^(Gemini|Ollama)$")]
    public string ArticleProvider { get; set; } = "Gemini";

    public string ArticleModel { get; set; } = "gemini-3.8-flash";
    public string? ArticleVerifierModel { get; set; } = null;

    [Range(4096, 1048576)]
    public int ArticleContextTokens { get; set; } = 131072;

    [Range(512, 16000)]
    public int ArticleMaxOutputTokens { get; set; } = 8192;

    [Range(512, 16000)]
    public int ArticleVerifierMaxOutputTokens { get; set; } = 8192;

    [Range(1000, 64000)]
    public int ArticleFallbackChunkBytes { get; set; } = 10000;
}
