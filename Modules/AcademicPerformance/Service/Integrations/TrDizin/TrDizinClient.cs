using System.Net;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;

public sealed class TrDizinClient(HttpClient httpClient, IConfiguration configuration)
{
    private const int DefaultMaximumProjectPages = 100;
    private const int ProjectPageSize = 100;
    private const long MaximumResponseBytes = 8L * 1024 * 1024;

    public async Task<TrDizinProfile?> GetByOrcidAsync(
        string orcid,
        CancellationToken cancellationToken = default)
    {
        string root = (configuration["TrDizin:ApiBaseUrl"] ??
            "https://search.trdizin.gov.tr").TrimEnd('/');
        string? authorJson = await GetAsync(
            $"{root}/api/public/yazar/orcid/?orcid={Uri.EscapeDataString(orcid)}",
            true,
            cancellationToken);
        if (authorJson is null)
        {
            return null;
        }

        using JsonDocument authorDocument = JsonDocument.Parse(authorJson);
        JsonElement author = authorDocument.RootElement;
        if (author.ValueKind != JsonValueKind.Object ||
            !string.Equals(Text(author, "orcid"), orcid, StringComparison.OrdinalIgnoreCase) ||
            !Long(author, "id").HasValue)
        {
            return null;
        }

        long authorId = Long(author, "id")!.Value;
        string publicationsJson = (await GetAsync(
            $"{root}/api/authorPublicationsById/{authorId}", false, cancellationToken))!;
        List<TrDizinWork> works = await CollectWorksAsync(
            root, publicationsJson, cancellationToken);

        ProjectCollectionResult projectResult;
        try
        {
            projectResult = await CollectProjectsAsync(
                root,
                orcid,
                authorId,
                Text(author, "fullName"),
                cancellationToken);
        }
        catch (ProviderCollectionException exception)
        {
            throw new ProviderCollectionException(
                exception.Code,
                exception.SafeDescription,
                works.Count + exception.RetrievedCount,
                exception.ExpectedCount.HasValue
                    ? works.Count + exception.ExpectedCount.Value
                    : null,
                exception);
        }

        return new TrDizinProfile
        {
            Orcid = orcid,
            AuthorId = authorId,
            DisplayName = Text(author, "fullName"),
            PublicationCount = Int(author, "orderPublicationCount"),
            CitationCount = Int(author, "orderCitationCount"),
            ProjectCandidateCount = projectResult.CandidateCount,
            ProjectMatchedCount = projectResult.Projects.Count,
            ProjectUnmatchedCount = projectResult.UnmatchedCount,
            ProjectSearchComplete = true,
            LastUpdatedAt = DateTime.UtcNow,
            RawAuthorJson = authorJson,
            RawPublicationsJson = publicationsJson,
            RawProjectsJson = projectResult.RawPagesJson,
            Works = works,
            Projects = projectResult.Projects
        };
    }

    private async Task<List<TrDizinWork>> CollectWorksAsync(
        string root,
        string publicationsJson,
        CancellationToken cancellationToken)
    {
        using JsonDocument publicationsDocument = JsonDocument.Parse(publicationsJson);
        JsonElement hits = publicationsDocument.RootElement.GetProperty("hits");
        JsonElement items = hits.GetProperty("hits");
        int total = hits.GetProperty("total").GetProperty("value").GetInt32();
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != total)
        {
            throw new InvalidDataException(
                "TR Dizin author publication response was incomplete.");
        }

        List<TrDizinWork> works = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            string? publicationId = Text(item, "_id") ??
                (TryProperty(item, "fields", out JsonElement fields)
                    ? FirstText(fields, "id")
                    : null);
            if (string.IsNullOrWhiteSpace(publicationId))
            {
                throw new InvalidDataException("TR Dizin publication id was missing.");
            }

            try
            {
                string detailJson = (await GetAsync(
                    $"{root}/api/publicationById/{Uri.EscapeDataString(publicationId)}",
                    false,
                    cancellationToken))!;
                JsonElement source = DetailSource(detailJson, publicationId);
                if (!IsProject(source))
                {
                    works.Add(Map(publicationId, source, detailJson));
                }
            }
            catch (Exception exception) when (
                exception is not ProviderCollectionException &&
                (exception is not OperationCanceledException ||
                    !cancellationToken.IsCancellationRequested))
            {
                throw new ProviderCollectionException(
                    "DetailFailure",
                    "TR Dizin yayın ayrıntıları tamamlanamadı; okunan eksik veri kaydedilmedi.",
                    works.Count,
                    total,
                    exception);
            }
        }

        return works;
    }

    private async Task<ProjectCollectionResult> CollectProjectsAsync(
        string root,
        string orcid,
        long authorId,
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ProviderCollectionException(
                "ProjectIdentityIncomplete",
                "TR Dizin yazarı için proje aramasında kullanılacak doğrulanmış ad bulunamadı; önceki tam proje anlık görüntüsü korundu.",
                0,
                null,
                new InvalidDataException("Resolved TR Dizin author did not contain fullName."));
        }

        int maximumPages = configuration.GetValue(
            "TrDizin:MaximumProjectPages", DefaultMaximumProjectPages);
        if (maximumPages <= 0)
        {
            maximumPages = DefaultMaximumProjectPages;
        }

        Dictionary<string, ProjectCandidate> candidates =
            new(StringComparer.OrdinalIgnoreCase);
        List<string> rawPages = [];
        int? reportedTotal = null;
        string relation = "eq";

        for (int page = 1; page <= maximumPages; page++)
        {
            try
            {
                string url = $"{root}/api/defaultSearch/publication/?q=&" +
                    $"order=publicationYear-DESC&page={page}&limit={ProjectPageSize}&" +
                    "facet-documentType=PROJECT&facet-authorName=" +
                    Uri.EscapeDataString(displayName);
                string pageJson = (await GetAsync(url, false, cancellationToken))!;
                rawPages.Add(pageJson);

                using JsonDocument pageDocument = JsonDocument.Parse(pageJson);
                JsonElement hits = RequiredObject(pageDocument.RootElement, "hits");
                JsonElement items = RequiredArray(hits, "hits");
                (int total, string totalRelation) = ParseTotal(hits);
                if (!reportedTotal.HasValue)
                {
                    reportedTotal = total;
                    relation = totalRelation;
                }
                else if (reportedTotal.Value != total ||
                    !string.Equals(relation, totalRelation, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "TR Dizin project result total changed during pagination.");
                }

                if (total < 0)
                {
                    throw new InvalidDataException("TR Dizin project total was negative.");
                }

                int pageCount = items.GetArrayLength();
                int candidatesBeforePage = candidates.Count;
                foreach (JsonElement item in items.EnumerateArray())
                {
                    string? projectId = Text(item, "_id") ??
                        (TryProperty(item, "fields", out JsonElement fields)
                            ? FirstText(fields, "id")
                            : null);
                    if (string.IsNullOrWhiteSpace(projectId))
                    {
                        throw new InvalidDataException("TR Dizin project id was missing.");
                    }

                    candidates.TryAdd(projectId, new(projectId));
                }

                if (pageCount > 0 && candidates.Count == candidatesBeforePage)
                {
                    throw new InvalidDataException(
                        "TR Dizin project pagination repeated a page without progress.");
                }

                if (string.Equals(relation, "eq", StringComparison.OrdinalIgnoreCase))
                {
                    if (candidates.Count == reportedTotal.Value)
                    {
                        break;
                    }
                    if (candidates.Count > reportedTotal.Value)
                    {
                        throw new InvalidDataException(
                            "TR Dizin project result exceeded its reported total.");
                    }
                    if (pageCount < ProjectPageSize)
                    {
                        throw new InvalidDataException(
                            "TR Dizin project response ended before its reported total.");
                    }
                }
                else if (string.Equals(relation, "gte", StringComparison.OrdinalIgnoreCase))
                {
                    if (pageCount < ProjectPageSize)
                    {
                        if (candidates.Count < reportedTotal.Value)
                        {
                            throw new InvalidDataException(
                                "TR Dizin project response ended below its reported lower bound.");
                        }
                        break;
                    }
                }
                else
                {
                    throw new InvalidDataException(
                        "TR Dizin project total relation was invalid.");
                }

                if (page == maximumPages)
                {
                    throw new InvalidDataException(
                        "TR Dizin project page safety limit was reached.");
                }
            }
            catch (Exception exception) when (
                exception is not ProviderCollectionException &&
                (exception is not OperationCanceledException ||
                    !cancellationToken.IsCancellationRequested))
            {
                throw new ProviderCollectionException(
                    "ProjectPageFailure",
                    "TR Dizin proje araması tamamlanamadı; önceki tam proje anlık görüntüsü korundu.",
                    0,
                    reportedTotal,
                    exception);
            }
        }

        List<TrDizinProject> projects = [];
        int unmatchedCount = 0;
        foreach (ProjectCandidate candidate in candidates.Values)
        {
            try
            {
                string detailJson = (await GetAsync(
                    $"{root}/api/publicationById/{Uri.EscapeDataString(candidate.Id)}",
                    false,
                    cancellationToken))!;
                JsonElement source = DetailSource(detailJson, candidate.Id);
                if (!IsProject(source))
                {
                    throw new InvalidDataException(
                        "TR Dizin project search returned a non-project detail.");
                }
                JsonElement? matchedResearcher = FindMatchedResearcher(
                    source, authorId, orcid);
                if (!matchedResearcher.HasValue)
                {
                    unmatchedCount++;
                    continue;
                }

                projects.Add(MapProject(
                    candidate.Id,
                    source,
                    matchedResearcher,
                    detailJson));
            }
            catch (Exception exception) when (
                exception is not ProviderCollectionException &&
                (exception is not OperationCanceledException ||
                    !cancellationToken.IsCancellationRequested))
            {
                throw new ProviderCollectionException(
                    "ProjectDetailFailure",
                    "TR Dizin proje ayrıntıları tamamlanamadı; önceki tam proje anlık görüntüsü korundu.",
                    projects.Count,
                    candidates.Count,
                    exception);
            }
        }

        return new(
            projects,
            candidates.Count,
            unmatchedCount,
            "[" + string.Join(',', rawPages) + "]");
    }

    private async Task<string?> GetAsync(
        string url,
        bool expectedNotFound,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        if (expectedNotFound)
        {
            request.Options.Set(ProviderRateLimitHandler.ExpectedNotFound, true);
        }
        request.Options.Set(ProviderRateLimitHandler.ResponseBufferLimit, MaximumResponseBytes);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && expectedNotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    internal static TrDizinWork Map(string id, JsonElement source, string raw) => new()
    {
        PublicationId = id,
        Title = Text(source, "orderTitle") ?? Text(source, "title") ??
            FirstObjectText(source, "abstracts", "title"),
        Doi = Text(source, "doi"),
        PublicationYear = NumberOrText(source, "publicationYear") ??
            NumberOrText(source, "year") ?? IssueYear(source),
        PublicationType = Text(source, "publicationType") ?? Text(source, "docType"),
        Authors = JoinObjectText(source, "authors", "inPublicationName"),
        Journal = Text(source, "journalTitle") ?? Text(source, "journalName") ??
            NestedText(source, "journal", "name"),
        CitationCount = Int(source, "orderCitationCount"),
        RawDataJson = raw
    };

    internal static TrDizinProject MapProject(
        string id,
        JsonElement source,
        JsonElement? matchedResearcher,
        string raw) => new()
    {
        ProjectId = id,
        ProjectNumber = ExtractText(source, "projectNumber", "projectNo", "number"),
        Title = ExtractText(source, "orderTitle", "title"),
        StartedDate = ExtractText(source, "startedDate", "startDate"),
        EndDate = ExtractText(source, "endDate", "endedDate"),
        ProjectGroup = ExtractText(source, "projectGroup", "group"),
        ResearchersJson = PropertyJson(source, "researchers", "projectResearchers", "authors"),
        Duty = matchedResearcher.HasValue
            ? ExtractText(matchedResearcher.Value, "duty", "role", "projectDuty")
            : ExtractText(source, "duty", "role", "projectDuty"),
        AbstractsJson = PropertyJson(source, "abstracts", "abstract"),
        KeywordsJson = PropertyJson(source, "keywords", "keyword"),
        OutputsJson = PropertyJson(
            source, "outputs", "projectOutputs", "publicationProduct"),
        AttachmentsJson = PropertyJson(source, "attachments", "files"),
        RawDataJson = raw
    };

    private static JsonElement DetailSource(string detailJson, string expectedId)
    {
        using JsonDocument detailDocument = JsonDocument.Parse(detailJson);
        JsonElement hits = RequiredArray(
            RequiredObject(detailDocument.RootElement, "hits"), "hits");
        if (hits.GetArrayLength() != 1 ||
            !TryProperty(hits[0], "_source", out JsonElement source) ||
            source.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("TR Dizin publication detail response was invalid.");
        }
        string? returnedId = Text(hits[0], "_id");
        if (string.IsNullOrWhiteSpace(returnedId) ||
            !string.Equals(returnedId, expectedId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "TR Dizin publication detail id did not match the requested id.");
        }
        return source.Clone();
    }

    private static (int Total, string Relation) ParseTotal(JsonElement hits)
    {
        if (!TryProperty(hits, "total", out JsonElement total))
        {
            throw new InvalidDataException("TR Dizin project total was missing.");
        }
        if (total.ValueKind == JsonValueKind.Number && total.TryGetInt32(out int numericTotal))
        {
            return (numericTotal, "eq");
        }
        if (total.ValueKind != JsonValueKind.Object ||
            !TryProperty(total, "value", out JsonElement value) ||
            !value.TryGetInt32(out int objectTotal))
        {
            throw new InvalidDataException("TR Dizin project total was invalid.");
        }
        return (objectTotal, Text(total, "relation") ?? "eq");
    }

    private static JsonElement? FindMatchedResearcher(
        JsonElement source,
        long authorId,
        string orcid)
    {
        foreach (string name in new[] { "researchers", "projectResearchers", "authors" })
        {
            if (!TryProperty(source, name, out JsonElement researchers))
            {
                continue;
            }
            IEnumerable<JsonElement> values = researchers.ValueKind == JsonValueKind.Array
                ? researchers.EnumerateArray()
                : [researchers];
            foreach (JsonElement researcher in values)
            {
                if (ResearcherMatches(researcher, authorId, orcid))
                {
                    return researcher.Clone();
                }
            }
        }
        return null;
    }

    private static bool ResearcherMatches(JsonElement researcher, long authorId, string orcid)
    {
        if (researcher.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        foreach (string name in new[] { "id", "authorId", "researcherId" })
        {
            if (TryProperty(researcher, name, out JsonElement value) && IdMatches(value, authorId))
            {
                return true;
            }
        }
        foreach (string name in new[] { "orcid", "orcidId" })
        {
            if (TryProperty(researcher, name, out JsonElement value) && OrcidMatches(value, orcid))
            {
                return true;
            }
        }
        foreach (string name in new[] { "author", "researcher" })
        {
            if (TryProperty(researcher, name, out JsonElement nested) &&
                ResearcherMatches(nested, authorId, orcid))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IdMatches(JsonElement value, long authorId) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) &&
            number == authorId ||
        value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), out long textNumber) && textNumber == authorId;

    private static bool OrcidMatches(JsonElement value, string orcid) =>
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString()?.Trim(), orcid, StringComparison.OrdinalIgnoreCase);

    private static bool IsProject(JsonElement source) =>
        string.Equals(ExtractText(source, "documentType", "docType"),
            "PROJECT", StringComparison.OrdinalIgnoreCase);

    private static string? PropertyJson(JsonElement value, params string[] names)
    {
        foreach (string name in names)
        {
            if (TryProperty(value, name, out JsonElement property) &&
                property.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                return property.GetRawText();
            }
        }
        return null;
    }

    private static string? ExtractText(JsonElement value, params string[] names)
    {
        foreach (string name in names)
        {
            if (!TryProperty(value, name, out JsonElement property))
            {
                continue;
            }
            if (property.ValueKind == JsonValueKind.Array && property.GetArrayLength() > 0)
            {
                property = property[0];
            }
            string? result = ScalarText(property);
            if (!string.IsNullOrWhiteSpace(result))
            {
                return result;
            }
            if (property.ValueKind == JsonValueKind.Object)
            {
                result = Text(property, "name") ?? Text(property, "title") ??
                    Text(property, "value");
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }
            }
        }
        return null;
    }

    private static string? ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
        _ => null
    };

    private static JsonElement RequiredObject(JsonElement value, string name)
    {
        if (!TryProperty(value, name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"TR Dizin response property '{name}' was invalid.");
        }
        return property;
    }

    private static JsonElement RequiredArray(JsonElement value, string name)
    {
        if (!TryProperty(value, name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"TR Dizin response property '{name}' was invalid.");
        }
        return property;
    }

    private static bool TryProperty(JsonElement value, string name, out JsonElement property)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty candidate in value.EnumerateObject())
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }
        property = default;
        return false;
    }

    private static string? Text(JsonElement value, string name) =>
        TryProperty(value, name, out JsonElement item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static int? Int(JsonElement value, string name) =>
        TryProperty(value, name, out JsonElement item) &&
        item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int number)
            ? number
            : null;

    private static int? NumberOrText(JsonElement value, string name) => Int(value, name) ??
        (int.TryParse(Text(value, name), out int number) ? number : null);

    private static long? Long(JsonElement value, string name) =>
        TryProperty(value, name, out JsonElement item) && item.TryGetInt64(out long number)
            ? number
            : null;

    private static string? FirstText(JsonElement value, string name) =>
        TryProperty(value, name, out JsonElement array) &&
        array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0
            ? array[0].ToString()
            : null;

    private static string? FirstObjectText(JsonElement value, string arrayName, string name) =>
        TryProperty(value, arrayName, out JsonElement array) &&
        array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0
            ? Text(array[0], name)
            : null;

    private static string? JoinObjectText(JsonElement value, string arrayName, string name) =>
        TryProperty(value, arrayName, out JsonElement array) &&
        array.ValueKind == JsonValueKind.Array
            ? string.Join("; ", array.EnumerateArray().Select(item => Text(item, name))
                .Where(item => !string.IsNullOrWhiteSpace(item)))
            : null;

    private static int? IssueYear(JsonElement value) =>
        TryProperty(value, "issue", out JsonElement issue) &&
        int.TryParse(Text(issue, "year"), out int year) ? year : null;

    private static string? NestedText(JsonElement value, string objectName, string name) =>
        TryProperty(value, objectName, out JsonElement item) ? Text(item, name) : null;

    private sealed record ProjectCandidate(string Id);

    private sealed record ProjectCollectionResult(
        List<TrDizinProject> Projects,
        int CandidateCount,
        int UnmatchedCount,
        string RawPagesJson);
}
