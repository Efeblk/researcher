using System.Globalization;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollector.Analysis.Contracts;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;

public static class ResearcherSnapshotBuilder
{
    public static AnalyzeResearcherRequest Build(Researcher researcher,
        List<PublicationSummary> summaries, List<AcademicWork> works, AnalysisServiceOptions options)
    {
        AnalyzeResearcherRequest snapshot = new()
        {
            ResearcherId = researcher.Id,
            ResearcherName = Limit($"{researcher.FirstName} {researcher.LastName}".Trim(), 200)
                ?? $"Researcher {researcher.Id}",
            Department = Limit(researcher.Department, 200),
            SnapshotAt = DateTimeOffset.UtcNow,
            Language = options.Language,
            TotalPublicationCount = summaries.Count
        };
        int remaining = options.MaximumTextBytes;
        foreach (PublicationSummary summary in summaries.OrderByDescending(value => value.PublicationYear).ThenBy(value => value.Id))
        {
            if (snapshot.Publications.Count == options.MaximumPublications)
                break;
            // Preserve whole source fields; never silently truncate an abstract into an apparent complete abstract.
            int titleBytes = Encoding.UTF8.GetByteCount(summary.Title);
            if (string.IsNullOrWhiteSpace(summary.Title) || titleBytes > remaining)
                continue;
            remaining -= titleBytes;
            AcademicWork? source = works.Where(work => Matches(summary, work) &&
                    summaries.Count(candidate => Matches(candidate, work)) == 1)
                .OrderByDescending(work => !string.IsNullOrWhiteSpace(work.Abstract))
                .ThenBy(work => work.Id).FirstOrDefault();
            string? abstractText = TakeWholeField(source?.Abstract, 12000, ref remaining);
            string? keywords = TakeWholeField(source?.Keywords, 2000, ref remaining);
            snapshot.Publications.Add(new AnalysisPublication
            {
                Id = "publication-" + summary.Id.ToString(CultureInfo.InvariantCulture),
                Title = summary.Title,
                Year = summary.PublicationYear is >= 1000 and <= 3000 ? summary.PublicationYear : null,
                Category = summary.Category.ToString(),
                Abstract = abstractText,
                Keywords = keywords,
                Doi = Limit(summary.Doi, 300),
                Sources = string.IsNullOrWhiteSpace(summary.Sources) ? "Unknown" : summary.Sources
            });
        }
        if (researcher.GoogleScholarProfile is { } scholar)
            snapshot.CitationMetrics.Add(Metrics("GoogleScholar", scholar.CitationCount, scholar.HIndex, scholar.LastUpdatedAt));
        if (researcher.WebOfScienceProfile is { } wos)
            snapshot.CitationMetrics.Add(Metrics("WebOfScience", wos.TotalTimesCited, wos.HIndex, wos.LastUpdatedAt));
        if (researcher.OpenAlexProfile is { } openAlex)
            snapshot.CitationMetrics.Add(Metrics("OpenAlex", openAlex.CitedByCount, openAlex.HIndex, openAlex.LastUpdatedAt));
        return snapshot;
    }

    private static ProviderMetrics Metrics(string provider, int? citations, int? hIndex, DateTime collectedAt) => new()
    {
        Provider = provider,
        CitationCount = citations is >= 0 ? citations : null,
        HIndex = hIndex is >= 0 ? hIndex : null,
        CollectedAt = collectedAt == default ? null : new DateTimeOffset(DateTime.SpecifyKind(collectedAt, DateTimeKind.Utc))
    };

    private static bool Matches(PublicationSummary summary, AcademicWork work)
    {
        if (!string.IsNullOrWhiteSpace(summary.Doi) && !string.IsNullOrWhiteSpace(work.Doi))
            return NormalizeDoi(summary.Doi) == NormalizeDoi(work.Doi);
        return !string.IsNullOrWhiteSpace(work.Title) && summary.Title == work.Title &&
            summary.PublicationYear == work.PublicationYear;
    }

    private static string NormalizeDoi(string value) => value.Trim().ToLowerInvariant()
        .Replace("https://doi.org/", "").Replace("http://doi.org/", "").Replace("doi:", "");

    private static string? TakeWholeField(string? value, int maxLength, ref int remaining)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            return null;
        int bytes = Encoding.UTF8.GetByteCount(value);
        if (bytes > remaining)
            return null;
        remaining -= bytes;
        return value;
    }

    private static string? Limit(string? value, int length) => string.IsNullOrWhiteSpace(value)
        ? null : value.Length <= length ? value : value[..length];
}
