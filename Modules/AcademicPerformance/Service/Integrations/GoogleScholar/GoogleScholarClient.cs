using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;

public sealed class GoogleScholarClient(HttpClient httpClient, IConfiguration configuration)
{
    private const string DefaultProfileBaseUrl = "https://scholar.google.com/citations";
    private const int DefaultMaximumResponseBytes = 2 * 1024 * 1024;

    private static readonly Regex YearPattern = new(@"\b(19|20)\d{2}\b", RegexOptions.CultureInvariant);

    public async Task FillResearcherAsync(Researcher researcher, string googleScholarId)
    {
        string html = await GetProfileHtmlAsync(googleScholarId);
        ParsedProfile parsed = ParseProfile(html);
        GoogleScholarProfile? existing = researcher.GoogleScholarProfile;
        bool documentsCountKnown = existing is not null &&
            GoogleScholarProfile.HasKnownDocumentsCount(existing.RawDataJson);
        GoogleScholarProfile profile = existing ?? new()
        {
            Works = [],
            DocumentsCount = 0
        };

        profile.DisplayName = parsed.DisplayName ?? profile.DisplayName;
        profile.Affiliations = parsed.Affiliations ?? profile.Affiliations;
        profile.VerifiedEmail = parsed.VerifiedEmail ?? profile.VerifiedEmail;
        profile.ProfileUrl = CreateProfileUrl(googleScholarId);
        profile.CitationCount = parsed.CitationCount;
        profile.CitationCountRecent = parsed.CitationCountRecent;
        profile.HIndex = parsed.HIndex;
        profile.HIndexRecent = parsed.HIndexRecent;
        profile.I10Index = parsed.I10Index;
        profile.I10IndexRecent = parsed.I10IndexRecent;
        profile.MetricsSinceYear = parsed.MetricsSinceYear;
        profile.RawDataJson = GoogleScholarProfile.CreateScrapeSnapshot(html, documentsCountKnown);
        profile.LastUpdatedAt = DateTime.UtcNow;

        researcher.GoogleScholarProfile = profile;
        ApplyNameWhenMissing(researcher, profile.DisplayName);
    }

    private async Task<string> GetProfileHtmlAsync(string googleScholarId)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, CreateProfileUrl(googleScholarId));
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
        request.Options.Set(ProviderRateLimitHandler.ResponseBufferLimit,
            configuration.GetValue("GoogleScholar:MaximumResponseBytes", DefaultMaximumResponseBytes));
        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ProviderCallScope.Cancellation);
        string content = await response.Content.ReadAsStringAsync(ProviderCallScope.Cancellation);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Google Scholar HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
                null,
                response.StatusCode);
        }

        return content;
    }

    private string CreateProfileUrl(string googleScholarId)
    {
        string baseUrl = configuration["GoogleScholar:ProfileBaseUrl"] ?? DefaultProfileBaseUrl;
        string separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return baseUrl + separator + "user=" + Uri.EscapeDataString(googleScholarId.Trim()) + "&hl=en";
    }

    private static ParsedProfile ParseProfile(string html)
    {
        IDocument document = new HtmlParser().ParseDocument(html);
        IElement? metricsTable = document.QuerySelector("#gsc_rsb_st");
        if (IsBlocked(document, metricsTable is not null))
        {
            ProviderCallScope.Record("GoogleScholar", true);
            throw new HttpRequestException(
                "Google Scholar erişimi CAPTCHA veya otomatik istek engeliyle durduruldu.",
                null,
                HttpStatusCode.TooManyRequests);
        }

        IElement table = metricsTable ?? throw Malformed(
            "Google Scholar profil metrik tablosu bulunamadı.");
        List<IElement> headers = table.QuerySelectorAll("thead th").ToList();
        Match yearMatch = headers.Select(header => YearPattern.Match(header.TextContent))
            .FirstOrDefault(match => match.Success) ?? Match.Empty;
        if (!yearMatch.Success || !int.TryParse(yearMatch.Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out int sinceYear))
        {
            throw Malformed("Google Scholar son dönem başlangıç yılı okunamadı.");
        }

        Dictionary<MetricKind, (int All, int Recent)> metrics = [];
        foreach (IElement row in table.QuerySelectorAll("tbody tr"))
        {
            List<IElement> cells = row.QuerySelectorAll("th,td").ToList();
            if (cells.Count < 3 || !TryGetMetricKind(cells[0].TextContent, out MetricKind kind))
                continue;
            if (metrics.ContainsKey(kind))
                throw Malformed("Google Scholar metrik tablosunda yinelenen satır bulundu.");
            if (!TryParseCount(cells[1].TextContent, out int all) ||
                !TryParseCount(cells[2].TextContent, out int recent))
            {
                throw Malformed("Google Scholar metrik tablosunda eksik veya geçersiz değer bulundu.");
            }
            metrics.Add(kind, (all, recent));
        }

        if (metrics.Count != 3 || !metrics.TryGetValue(MetricKind.Citations, out var citations) ||
            !metrics.TryGetValue(MetricKind.HIndex, out var hIndex) ||
            !metrics.TryGetValue(MetricKind.I10Index, out var i10Index))
        {
            throw Malformed("Google Scholar metrik tablosu üç zorunlu satırı içermiyor.");
        }

        return new(
            Text(document.QuerySelector("#gsc_prf_in")),
            Text(document.QuerySelector(".gsc_prf_il")),
            Text(document.QuerySelector("#gsc_prf_ivh")),
            citations.All,
            citations.Recent,
            hIndex.All,
            hIndex.Recent,
            i10Index.All,
            i10Index.Recent,
            sinceYear);
    }

    private static bool IsBlocked(IDocument document, bool hasMetricsTable)
    {
        if (document.QuerySelector("form#captcha-form, #recaptcha, .g-recaptcha") is not null)
            return true;
        if (hasMetricsTable)
            return false;
        string text = document.Body?.TextContent ?? document.DocumentElement.TextContent;
        return text.Contains("unusual traffic", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("not a robot", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("automated queries", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("olağandışı trafik", StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidDataException Malformed(string message) => new(message);

    private static bool TryGetMetricKind(string label, out MetricKind kind)
    {
        string normalized = Normalize(label);
        if (normalized.Contains("citation", StringComparison.Ordinal) ||
            normalized.Contains("alinti", StringComparison.Ordinal) ||
            normalized.Contains("atif", StringComparison.Ordinal))
        {
            kind = MetricKind.Citations;
            return true;
        }
        if (normalized.Contains("i10index", StringComparison.Ordinal) ||
            normalized.Contains("i10endeks", StringComparison.Ordinal))
        {
            kind = MetricKind.I10Index;
            return true;
        }
        if (normalized.Contains("hindex", StringComparison.Ordinal) ||
            normalized.Contains("hendeks", StringComparison.Ordinal))
        {
            kind = MetricKind.HIndex;
            return true;
        }
        kind = default;
        return false;
    }

    private static string Normalize(string value)
    {
        string decomposed = value.ToLowerInvariant().Replace('ı', 'i').Normalize(NormalizationForm.FormD);
        StringBuilder result = new();
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark &&
                char.IsLetterOrDigit(character))
            {
                result.Append(character);
            }
        }
        return result.ToString();
    }

    private static bool TryParseCount(string value, out int result)
    {
        result = 0;
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
            return false;
        if (trimmed.All(character => character is >= '0' and <= '9'))
        {
            return int.TryParse(
                trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        }

        string normalized = new(trimmed.Select(character =>
            char.IsWhiteSpace(character) ? ' ' : character).ToArray());
        if (normalized.Any(character => character is not (>= '0' and <= '9') &&
                character is not (',' or '.' or ' ')))
        {
            return false;
        }
        char[] separators = normalized.Where(character => character is ',' or '.' or ' ')
            .Distinct().ToArray();
        if (separators.Length != 1)
            return false;
        string[] groups = normalized.Split(separators[0]);
        if (groups.Length < 2 || groups[0].Length is < 1 or > 3 ||
            groups.Skip(1).Any(group => group.Length != 3) ||
            groups.Any(group => group.Any(character => character is < '0' or > '9')))
        {
            return false;
        }
        return int.TryParse(
            string.Concat(groups), NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    private static string? Text(IElement? element)
    {
        string? value = element?.TextContent.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void ApplyNameWhenMissing(Researcher researcher, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) ||
            !string.IsNullOrWhiteSpace(researcher.FirstName) ||
            !string.IsNullOrWhiteSpace(researcher.LastName))
        {
            return;
        }

        string[] parts = displayName.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        researcher.FirstName = parts.FirstOrDefault();
        researcher.LastName = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : null;
    }

    private enum MetricKind
    {
        Citations,
        HIndex,
        I10Index
    }

    private sealed record ParsedProfile(
        string? DisplayName,
        string? Affiliations,
        string? VerifiedEmail,
        int CitationCount,
        int CitationCountRecent,
        int HIndex,
        int HIndexRecent,
        int I10Index,
        int I10IndexRecent,
        int MetricsSinceYear);
}
