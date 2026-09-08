using System.Text.RegularExpressions;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

public sealed partial class ResearcherProviderInputNormalizer(ResearcherIdentifierParser parser)
{
    public const int MaximumProviderInputLength = 4096;

    public ResearcherProviderInputNormalizationResult Normalize(ResearcherProviderInput input)
    {
        List<string> warnings = [];
        ResearcherProviderInput normalized = new()
        {
            Orcid = NormalizeField("ORCID", input.Orcid, NormalizeOrcidCandidate, warnings),
            GoogleScholarId = NormalizeField("Google Scholar ID", input.GoogleScholarId, NormalizeScholarCandidate, warnings),
            WebOfScienceResearcherId = NormalizeField("Web of Science ResearcherID", input.WebOfScienceResearcherId,
                NormalizeWosCandidate, warnings)
        };
        if (!string.IsNullOrWhiteSpace(input.ScopusId))
            warnings.Add("Scopus ID: collection is unsupported and the value was not used.");
        if (normalized.Orcid is null && normalized.GoogleScholarId is null && normalized.WebOfScienceResearcherId is null)
            return new(normalized, warnings, "No usable supported provider identifier remains after validation.");
        try { parser.Create(ToCollectionRequest(normalized)); }
        catch (ArgumentException)
        {
            return new(normalized, warnings, "No usable supported provider identifier remains after validation.");
        }
        return new(normalized, warnings, null);
    }

    public static ResearcherCollectRequest ToCollectionRequest(ResearcherProviderInput input)
    {
        List<string> identifiers = [];
        if (input.Orcid is not null) identifiers.AddRange(["--orcid", input.Orcid]);
        if (input.GoogleScholarId is not null) identifiers.AddRange(["--scholar", input.GoogleScholarId]);
        if (input.WebOfScienceResearcherId is not null) identifiers.AddRange(["--researcherid", input.WebOfScienceResearcherId]);
        return new() { Identifiers = identifiers };
    }

    private static string? NormalizeField(string fieldName, string? value, Func<string, string?> normalize,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > MaximumProviderInputLength)
        {
            warnings.Add($"{fieldName}: value exceeded {MaximumProviderInputLength} characters and was not used for collection.");
            return null;
        }
        string trimmed = value.Trim();
        if (IsMissingValue(trimmed))
        {
            warnings.Add($"{fieldName}: placeholder value was not used for collection.");
            return null;
        }
        string? result = normalize(trimmed);
        if (result is null) warnings.Add($"{fieldName}: invalid or ambiguous value was not used for collection.");
        return result;
    }

    private static string? NormalizeOrcidCandidate(string value)
    {
        string candidate = OrcidPrefixRegex().Replace(TrimSafeWrappers(value), string.Empty);
        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
        {
            if (uri.Scheme is not ("http" or "https") || !uri.Host.Equals("orcid.org", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return null;
            candidate = uri.AbsolutePath.Trim('/');
        }
        candidate = SpacedOrcidRegex().Replace(candidate, "$1-$2-$3-$4");
        try { return ResearcherIdentifierParser.NormalizeOrcid(candidate); }
        catch (ArgumentException) { return null; }
    }

    private static string? NormalizeWosCandidate(string value)
    {
        string candidate = TrimSafeWrappers(value);
        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
        {
            if (uri.Scheme is not ("http" or "https") ||
                !uri.Host.Equals("www.webofscience.com", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return null;
            Match match = WosPathRegex().Match(uri.AbsolutePath);
            if (!match.Success) return null;
            candidate = Uri.UnescapeDataString(match.Groups[1].Value);
        }
        try { return ResearcherIdentifierParser.NormalizeResearcherId(candidate); }
        catch (ArgumentException) { return null; }
    }

    private static string? NormalizeScholarCandidate(string value)
    {
        string candidate = TrimSafeWrappers(value);
        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
        {
            if (uri.Scheme is not ("http" or "https") || !AllowedScholarHost(uri.Host) ||
                !uri.AbsolutePath.Equals("/citations", StringComparison.OrdinalIgnoreCase)) return null;
            List<string[]> userParts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(pair => Uri.UnescapeDataString(pair[0]).Equals("user", StringComparison.OrdinalIgnoreCase)).ToList();
            if (userParts.Count != 1 || userParts[0].Length != 2) return null;
            candidate = Uri.UnescapeDataString(userParts[0][1]);
        }
        else
        {
            int suffix = candidate.IndexOf('&');
            if (suffix >= 0)
            {
                if (candidate[(suffix + 1)..].Split('&').Any(part =>
                    part.Split('=', 2)[0].Equals("user", StringComparison.OrdinalIgnoreCase))) return null;
                candidate = candidate[..suffix];
            }
        }
        try { return ResearcherIdentifierParser.NormalizeGoogleScholarId(candidate); }
        catch (ArgumentException) { return null; }
    }

    private static bool AllowedScholarHost(string host) =>
        host.Equals("scholar.google.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("scholar.google.com.tr", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("scholar.google.co.za", StringComparison.OrdinalIgnoreCase);
    private static bool IsMissingValue(string value) => value.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
        value is "0" or "." or "-" || value.StartsWith('#');
    private static string TrimSafeWrappers(string value) =>
        value.Trim(' ', '\t', '\r', '\n', '(', ')', '[', ']', '<', '>', ',', '.', ';');

    [GeneratedRegex(@"^ORCID[.:]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrcidPrefixRegex();
    [GeneratedRegex(@"^([0-9]{4})\s+([0-9]{4})\s+([0-9]{4})\s+([0-9]{3}[0-9Xx])$")]
    private static partial Regex SpacedOrcidRegex();
    [GeneratedRegex(@"^/wos/author/(?:record|rid)/([^/]+)/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WosPathRegex();
}
