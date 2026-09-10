using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using System.Text.RegularExpressions;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed record ArticleSourceCandidate(string Origin, string Url);

public static class ArticleSourceCandidateCatalog
{
    public const int MaximumCandidates = 16;

    public static IReadOnlyList<ArticleSourceCandidate> GetCandidates(IEnumerable<AcademicWork> works)
    {
        List<ArticleSourceCandidate> pdf = [];
        List<ArticleSourceCandidate> other = [];
        foreach (AcademicWork work in works)
        {
            Add(pdf, "FullTextUrl", work.FullTextUrl);
            foreach (AcademicWorkSource source in work.Sources.Where(source => source.Kind == "Pdf")) Add(pdf, source.Origin, source.Url);
            foreach (AcademicWorkSource source in AcademicWorkSourceDiscovery.FromPayload(work.ProviderPayload, $"{work.Provider}.Payload"))
                Add(source.Kind == "Pdf" ? pdf : other, source.Origin, source.Url);
            foreach (AcademicWorkSource source in work.Sources.Where(source => source.Kind != "Pdf")) Add(other, source.Origin, source.Url);
            Add(other, "Link", work.Link);
            string doi = CrossrefClient.NormalizeDoi(work.Doi);
            if (Regex.IsMatch(doi, @"^10\.\d{4,9}/\S+$", RegexOptions.CultureInvariant))
                Add(other, "Doi", "https://doi.org/" + Uri.EscapeDataString(doi).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase));
        }
        HashSet<string> seen = new(StringComparer.Ordinal);
        return pdf.Concat(other).Where(candidate => seen.Add(candidate.Url)).Take(MaximumCandidates).ToList();

        static void Add(List<ArticleSourceCandidate> target, string origin, string? value)
        {
            string url = value?.Trim() ?? "";
            if (url.Length > 2000 || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) return;
            target.Add(new(origin, url));
        }
    }
}
