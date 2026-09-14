using System.Text.RegularExpressions;

namespace ResearcherAnalysisService.SourceData.Works;

public static partial class AcademicDoiNormalizer
{
    public static string Normalize(string? value)
    {
        string result = (value ?? string.Empty).Trim();
        string[] prefixes =
        {
            "https://doi.org/", "http://doi.org/", "https://dx.doi.org/",
            "http://dx.doi.org/", "doi:"
        };
        bool changed;
        do
        {
            changed = false;
            foreach (string prefix in prefixes)
            {
                if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                result = result[prefix.Length..].Trim();
                changed = true;
                break;
            }
        }
        while (changed);

        return result.ToLowerInvariant();
    }

    public static string? NormalizeValid(string? value)
    {
        string normalized = Normalize(value);
        return normalized.Length <= 500 && DoiPattern().IsMatch(normalized)
            ? normalized
            : null;
    }

    [GeneratedRegex(@"^10\.\d{4,9}/\S+$", RegexOptions.CultureInvariant)]
    private static partial Regex DoiPattern();
}
