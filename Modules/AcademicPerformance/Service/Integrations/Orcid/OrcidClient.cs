using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using Microsoft.Extensions.Configuration;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;

public sealed class OrcidClient
{
    private const string DefaultApiBaseUrl = "https://pub.orcid.org/v3.0";
    private const int BulkWorkLimit = 100;
    private const long DefaultMaximumResponseBytes = 8L * 1024 * 1024;

    private static readonly ActivitySection[] ActivitySections =
    [
        new("employment", "employments", "affiliation-group", null, "summaries", "employment-summary"),
        new("education", "educations", "affiliation-group", null, "summaries", "education-summary"),
        new("qualification", "qualifications", "affiliation-group", null, "summaries", "qualification-summary"),
        new("invited-position", "invited-positions", "affiliation-group", null, "summaries", "invited-position-summary"),
        new("distinction", "distinctions", "affiliation-group", null, "summaries", "distinction-summary"),
        new("membership", "memberships", "affiliation-group", null, "summaries", "membership-summary"),
        new("service", "services", "affiliation-group", null, "summaries", "service-summary"),
        new("funding", "fundings", "group", null, "funding-summary", null),
        new("peer-review", "peer-reviews", "group", "peer-review-group", "peer-review-summary", null),
        new("research-resource", "research-resources", "group", null, "research-resource-summary", null)
    ];

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public OrcidClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task FillResearcherAsync(
        Researcher researcher,
        CancellationToken cancellationToken = default)
    {
        string orcid = researcher.Orcid
            ?? throw new ArgumentException("ORCID verilmedi.");
        string recordJson = await GetJsonAsync($"{orcid}/record", cancellationToken);
        using JsonDocument recordDocument = JsonDocument.Parse(recordJson);
        JsonElement root = recordDocument.RootElement;
        string? returnedOrcid = GetString(root, "orcid-identifier", "path");

        if (!string.Equals(orcid, returnedOrcid, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ORCID yanıtındaki kimlik istekle eşleşmiyor.");
        }

        OrcidProfile profile = CreateProfile(root, recordJson);
        List<JsonElement> summaries = GetPreferredWorkSummaries(root);
        profile.Works = await GetFullWorksAsync(orcid, summaries, cancellationToken);
        profile.ActivitiesDetailsJson = await GetActivityDetailsAsync(
            orcid, root, cancellationToken);
        profile.WorksCount = profile.Works.Count;
        if (researcher.OrcidProfile is null)
        {
            researcher.OrcidProfile = profile;
        }
        else
        {
            ApplyProfile(profile, researcher.OrcidProfile);
        }
        researcher.FirstName ??= profile.GivenNames;
        researcher.LastName ??= profile.FamilyName;
        researcher.AcademicTitle ??= profile.CurrentRoleTitle;
        researcher.Department ??= profile.CurrentDepartment;
    }

    private static void ApplyProfile(OrcidProfile source, OrcidProfile target)
    {
        target.DisplayName = source.DisplayName;
        target.GivenNames = source.GivenNames;
        target.FamilyName = source.FamilyName;
        target.CreditName = source.CreditName;
        target.Biography = source.Biography;
        target.CountryCodes = source.CountryCodes;
        target.Keywords = source.Keywords;
        target.CurrentOrganization = source.CurrentOrganization;
        target.CurrentDepartment = source.CurrentDepartment;
        target.CurrentRoleTitle = source.CurrentRoleTitle;
        target.WorksCount = source.WorksCount;
        target.EmploymentsCount = source.EmploymentsCount;
        target.EducationsCount = source.EducationsCount;
        target.FundingsCount = source.FundingsCount;
        target.PeerReviewsCount = source.PeerReviewsCount;
        target.RecordLastModifiedAt = source.RecordLastModifiedAt;
        target.LastUpdatedAt = source.LastUpdatedAt;
        target.ResearcherUrlsJson = source.ResearcherUrlsJson;
        target.ExternalIdentifiersJson = source.ExternalIdentifiersJson;
        target.EmploymentsJson = source.EmploymentsJson;
        target.EducationsJson = source.EducationsJson;
        target.ActivitiesJson = source.ActivitiesJson;
        target.ActivitiesDetailsJson = source.ActivitiesDetailsJson;
        target.RawDataJson = source.RawDataJson;
        target.Works ??= [];
        target.Works.Clear();

        foreach (OrcidWork work in source.Works ?? [])
        {
            target.Works.Add(work);
        }
    }

    private async Task<List<OrcidWork>> GetFullWorksAsync(
        string orcid,
        List<JsonElement> summaries,
        CancellationToken cancellationToken)
    {
        List<OrcidWork> works = [];
        int offset = 0;

        while (offset < summaries.Count)
        {
            List<JsonElement> batch = summaries
                .Skip(offset)
                .Take(BulkWorkLimit)
                .ToList();
            string putCodes = string.Join(",", batch.Select(GetPutCode));
            string responseJson;
            try
            {
                responseJson = await GetJsonAsync(
                    $"{orcid}/works/{putCodes}", cancellationToken);
                using JsonDocument responseDocument = JsonDocument.Parse(responseJson);
                JsonElement bulk = GetProperty(responseDocument.RootElement, "bulk");
                HashSet<long> expected = batch.Select(GetPutCode).ToHashSet();
                HashSet<long> returned = [];
                foreach (JsonElement item in bulk.EnumerateArray())
                {
                    JsonElement work = GetProperty(item, "work");
                    if (work.ValueKind != JsonValueKind.Object ||
                        !returned.Add(GetPutCode(work)) || !expected.Contains(GetPutCode(work)))
                        throw new InvalidDataException("ORCID bulk work mismatch.");
                    works.Add(CreateWork(work));
                }
                if (!returned.SetEquals(expected))
                    throw new InvalidDataException("ORCID bulk work count mismatch.");
            }
            catch (Exception exception) when (exception is not ProviderCollectionException &&
                !cancellationToken.IsCancellationRequested)
            {
                throw new ProviderCollectionException("DetailFailure",
                    "ORCID eser ayrıntıları tamamlanamadı; okunan eksik veri kaydedilmedi.",
                    works.Count, summaries.Count, exception);
            }

            offset += batch.Count;
        }

        return works;
    }

    private async Task<string> GetActivityDetailsAsync(
        string orcid,
        JsonElement record,
        CancellationToken cancellationToken)
    {
        List<ActivityDetail> details = [];
        List<(ActivitySection Section, long PutCode)> items = [];

        try
        {
            items = GetActivityItems(record);
            foreach ((ActivitySection section, long putCode) in items)
            {
                string json = await GetJsonAsync(
                    $"{orcid}/{section.Endpoint}/{putCode}", cancellationToken);
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement item = document.RootElement;
                long returnedPutCode = GetPutCode(item);
                if (returnedPutCode != putCode)
                {
                    throw new InvalidDataException(
                        $"ORCID {section.Category} detail put-code mismatch.");
                }

                details.Add(new(section.Category, putCode, item.Clone()));
            }
        }
        catch (Exception exception) when (exception is not ProviderCollectionException &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new ProviderCollectionException("DetailFailure",
                "ORCID activity details could not be completed; partial details were not saved.",
                details.Count, items.Count, exception);
        }

        return JsonSerializer.Serialize(details);
    }

    private static List<(ActivitySection Section, long PutCode)> GetActivityItems(
        JsonElement record)
    {
        JsonElement activities = GetProperty(record, "activities-summary");
        List<(ActivitySection Section, long PutCode)> result = [];

        foreach (ActivitySection section in ActivitySections)
        {
            JsonElement sectionElement = GetProperty(activities, section.SectionName);
            if (sectionElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                continue;
            }
            JsonElement groups = GetProperty(sectionElement, section.GroupName);
            EnsureArray(groups, section.SectionName, section.GroupName);

            foreach (JsonElement group in groups.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException(
                        $"ORCID {section.SectionName} group is not an object.");
                }

                IEnumerable<JsonElement> summaryParents = [group];
                if (section.NestedGroupName is not null)
                {
                    JsonElement nestedGroups = GetProperty(group, section.NestedGroupName);
                    EnsureArray(nestedGroups, section.SectionName, section.NestedGroupName);
                    summaryParents = nestedGroups.EnumerateArray().Select(item => item.Clone());
                }

                foreach (JsonElement summaryParent in summaryParents)
                {
                    JsonElement summaries = GetProperty(summaryParent, section.SummariesName);
                    EnsureArray(summaries, section.SectionName, section.SummariesName);
                    foreach (JsonElement summaryContainer in summaries.EnumerateArray())
                    {
                        JsonElement summary = section.SummaryName is null
                            ? summaryContainer
                            : GetProperty(summaryContainer, section.SummaryName);
                        if (summary.ValueKind != JsonValueKind.Object)
                        {
                            throw new JsonException(
                                $"ORCID {section.SectionName} summary is not an object.");
                        }

                        result.Add((section, GetPutCode(summary)));
                    }
                }
            }
        }

        return result.DistinctBy(item => (item.Section.Category, item.PutCode)).ToList();
    }

    private static void EnsureArray(
        JsonElement value,
        string sectionName,
        string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                $"ORCID {sectionName}.{propertyName} is not an array.");
        }
    }

    private async Task<string> GetJsonAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        string baseUrl = _configuration["Orcid:ApiBaseUrl"]
            ?? DefaultApiBaseUrl;
        string? accessToken = _configuration["Orcid:AccessToken"];
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"{baseUrl.TrimEnd('/')}/{relativePath}");
        request.Options.Set(
            ProviderRateLimitHandler.ResponseBufferLimit,
            GetMaximumResponseBytes());

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.orcid+json"));

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                accessToken.Trim());
        }

        long maximumResponseBytes = GetMaximumResponseBytes();
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await response.Content.LoadIntoBufferAsync(
            maximumResponseBytes, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new ArgumentException("ORCID kaydı bulunamadı.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"ORCID API {(int)response.StatusCode} ({response.ReasonPhrase}) döndürdü.",
                null, response.StatusCode);
        }

        return body;
    }

    private long GetMaximumResponseBytes()
    {
        return long.TryParse(
                _configuration["Orcid:MaximumResponseBytes"] ??
                _configuration["ProviderResponses:MaximumResponseBytes"],
                out long value) && value is >= 1024 and <= 64L * 1024 * 1024
            ? value
            : DefaultMaximumResponseBytes;
    }

    private static OrcidProfile CreateProfile(JsonElement root, string rawJson)
    {
        JsonElement person = GetProperty(root, "person");
        JsonElement activities = GetProperty(root, "activities-summary");
        JsonElement employments = GetProperty(activities, "employments");
        JsonElement educations = GetProperty(activities, "educations");
        JsonElement currentEmployment = GetCurrentEmployment(employments);
        string? givenNames = GetString(person, "name", "given-names", "value");
        string? familyName = GetString(person, "name", "family-name", "value");
        string? creditName = GetString(person, "name", "credit-name", "value");

        return new OrcidProfile
        {
            GivenNames = givenNames,
            FamilyName = familyName,
            CreditName = creditName,
            DisplayName = creditName ?? JoinNonEmpty(givenNames, familyName),
            Biography = GetString(person, "biography", "content"),
            CountryCodes = JoinArrayValues(person, "addresses", "address", "country", "value"),
            Keywords = JoinArrayValues(person, "keywords", "keyword", "content"),
            CurrentOrganization = GetString(currentEmployment, "organization", "name"),
            CurrentDepartment = GetString(currentEmployment, "department-name"),
            CurrentRoleTitle = GetString(currentEmployment, "role-title"),
            EmploymentsCount = CountAffiliations(employments),
            EducationsCount = CountAffiliations(educations),
            FundingsCount = CountGroups(activities, "fundings"),
            PeerReviewsCount = CountGroups(activities, "peer-reviews"),
            RecordLastModifiedAt = FromUnixMilliseconds(
                GetLong(root, "history", "last-modified-date", "value")),
            LastUpdatedAt = DateTime.UtcNow,
            ResearcherUrlsJson = GetRawText(person, "researcher-urls"),
            ExternalIdentifiersJson = GetRawText(person, "external-identifiers"),
            EmploymentsJson = GetRawText(activities, "employments"),
            EducationsJson = GetRawText(activities, "educations"),
            ActivitiesJson = GetRawText(root, "activities-summary"),
            RawDataJson = rawJson
        };
    }

    private static OrcidWork CreateWork(JsonElement work)
    {
        JsonElement externalIds = GetProperty(work, "external-ids");
        JsonElement contributors = GetProperty(work, "contributors");
        int? year = GetInteger(work, "publication-date", "year", "value");
        int? month = GetInteger(work, "publication-date", "month", "value");
        int? day = GetInteger(work, "publication-date", "day", "value");

        return new OrcidWork
        {
            PutCode = GetPutCode(work),
            Title = GetString(work, "title", "title", "value"),
            Subtitle = GetString(work, "title", "subtitle", "value"),
            TranslatedTitle = GetString(work, "title", "translated-title", "value"),
            WorkType = GetString(work, "type"),
            PublicationYear = year,
            PublicationDate = CreateDate(year, month, day),
            JournalTitle = GetString(work, "journal-title", "value"),
            Doi = ProviderWorkRelationParser.FindOrcidSelfDoi(work),
            Url = GetString(work, "url", "value"),
            Authors = JoinContributors(contributors),
            LanguageCode = GetString(work, "language-code"),
            CountryCode = GetString(work, "country", "value"),
            ShortDescription = GetString(work, "short-description"),
            Citation = GetString(work, "citation", "citation-value"),
            SourceName = GetString(work, "source", "source-name", "value"),
            Visibility = GetString(work, "visibility"),
            RecordLastModifiedAt = FromUnixMilliseconds(
                GetLong(work, "last-modified-date", "value")),
            ExternalIdentifiersJson = externalIds.ValueKind == JsonValueKind.Undefined
                ? null
                : externalIds.GetRawText(),
            ContributorsJson = contributors.ValueKind == JsonValueKind.Undefined
                ? null
                : contributors.GetRawText(),
            RawDataJson = work.GetRawText()
        };
    }

    private static List<JsonElement> GetPreferredWorkSummaries(JsonElement root)
    {
        List<JsonElement> summaries = [];
        JsonElement groups = GetProperty(root, "activities-summary", "works", "group");

        if (groups.ValueKind != JsonValueKind.Array)
        {
            return summaries;
        }

        foreach (JsonElement group in groups.EnumerateArray())
        {
            JsonElement workSummaries = GetProperty(group, "work-summary");

            if (workSummaries.ValueKind != JsonValueKind.Array ||
                workSummaries.GetArrayLength() == 0)
            {
                continue;
            }

            JsonElement preferred = workSummaries
                .EnumerateArray()
                .OrderByDescending(summary =>
                    GetInteger(summary, "display-index") ?? 0)
                .First();
            summaries.Add(preferred.Clone());
        }

        return summaries;
    }

    private static JsonElement GetCurrentEmployment(JsonElement employments)
    {
        List<JsonElement> summaries = [];
        JsonElement groups = GetProperty(employments, "affiliation-group");

        if (groups.ValueKind != JsonValueKind.Array)
        {
            return default;
        }

        foreach (JsonElement group in groups.EnumerateArray())
        {
            JsonElement groupSummaries = GetProperty(group, "summaries");

            if (groupSummaries.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            summaries.AddRange(groupSummaries
                .EnumerateArray()
                .Select(item => GetProperty(item, "employment-summary"))
                .Where(item => item.ValueKind == JsonValueKind.Object));
        }

        return summaries
            .OrderBy(summary => GetProperty(summary, "end-date").ValueKind != JsonValueKind.Null)
            .ThenByDescending(summary => GetInteger(summary, "start-date", "year", "value") ?? 0)
            .FirstOrDefault();
    }

    private static int CountAffiliations(JsonElement section)
    {
        JsonElement groups = GetProperty(section, "affiliation-group");
        return groups.ValueKind == JsonValueKind.Array
            ? groups.EnumerateArray().Sum(group =>
                GetProperty(group, "summaries") is JsonElement summaries &&
                summaries.ValueKind == JsonValueKind.Array
                    ? summaries.GetArrayLength()
                    : 0)
            : 0;
    }

    private static int CountGroups(JsonElement activities, string sectionName)
    {
        JsonElement groups = GetProperty(activities, sectionName, "group");
        return groups.ValueKind == JsonValueKind.Array ? groups.GetArrayLength() : 0;
    }

    private static string? JoinArrayValues(
        JsonElement root,
        string containerName,
        string arrayName,
        params string[] valuePath)
    {
        JsonElement values = GetProperty(root, containerName, arrayName);

        if (values.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string result = string.Join(", ", values
            .EnumerateArray()
            .Select(item => GetString(item, valuePath))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string? JoinContributors(JsonElement contributors)
    {
        JsonElement values = GetProperty(contributors, "contributor");

        if (values.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string result = string.Join(", ", values
            .EnumerateArray()
            .Select(item => GetString(item, "credit-name", "value"))
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static long GetPutCode(JsonElement element)
    {
        return GetLong(element, "put-code")
            ?? throw new JsonException("ORCID eserinde put-code alanı bulunamadı.");
    }

    private static string? GetRawText(JsonElement element, params string[] path)
    {
        JsonElement value = GetProperty(element, path);
        return value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? null
            : value.GetRawText();
    }

    private static string? GetString(JsonElement element, params string[] path)
    {
        JsonElement value = GetProperty(element, path);

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : null;
    }

    private static int? GetInteger(JsonElement element, params string[] path)
    {
        string? value = GetString(element, path);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : null;
    }

    private static long? GetLong(JsonElement element, params string[] path)
    {
        JsonElement value = GetProperty(element, path);

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }

        return long.TryParse(GetString(element, path), out number) ? number : null;
    }

    private static JsonElement GetProperty(JsonElement element, params string[] path)
    {
        JsonElement current = element;

        foreach (string name in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(name, out current))
            {
                return default;
            }
        }

        return current;
    }

    private static DateTime? CreateDate(int? year, int? month, int? day)
    {
        if (!year.HasValue || year.Value is < 1 or > 9999)
        {
            return null;
        }

        try
        {
            return new DateTime(year.Value, month ?? 1, day ?? 1, 0, 0, 0, DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
    }

    private static DateTime? FromUnixMilliseconds(long? value)
    {
        return value.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value).UtcDateTime
            : null;
    }

    private static string? JoinNonEmpty(params string?[] values)
    {
        string result = string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private sealed record ActivitySection(
        string Endpoint,
        string SectionName,
        string GroupName,
        string? NestedGroupName,
        string SummariesName,
        string? SummaryName)
    {
        public string Category => Endpoint;
    }

    private sealed record ActivityDetail(
        string Category,
        long PutCode,
        JsonElement Item);
}
