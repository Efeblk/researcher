using System.Text.Json;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Files;

public sealed class PdfSourceExtractor
{
    public List<Uri> GetCandidates(AcademicWork work)
    {
        List<Uri>? candidates = null;
        HashSet<string>? seenUrls = null;

        candidates = [];
        seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (work.Provider == AcademicWorkProvider.OpenAlex &&
            work.IsOpenAccess == true)
        {
            AddOpenAlexCandidates(work, candidates, seenUrls);
        }

        if (work.Provider == AcademicWorkProvider.GoogleScholar)
        {
            AddGoogleScholarCandidates(work, candidates, seenUrls);
        }

        return candidates;
    }

    private static void AddOpenAlexCandidates(
        AcademicWork work,
        List<Uri> candidates,
        HashSet<string> seenUrls)
    {
        JsonDocument? document = null;
        JsonElement root = default;
        JsonElement locations = default;
        JsonElement location = default;
        int index = 0;

        AddCandidate(work.FullTextUrl, candidates, seenUrls);

        if (string.IsNullOrWhiteSpace(work.ProviderPayload))
        {
            return;
        }

        try
        {
            using (document = JsonDocument.Parse(work.ProviderPayload))
            {
                root = document.RootElement;
                AddNestedString(root, "best_oa_location", "pdf_url", candidates, seenUrls);
                AddNestedString(root, "primary_location", "pdf_url", candidates, seenUrls);
                AddNestedString(root, "content_urls", "pdf", candidates, seenUrls);

                if (root.TryGetProperty("locations", out locations) &&
                    locations.ValueKind == JsonValueKind.Array)
                {
                    for (index = 0; index < locations.GetArrayLength(); index++)
                    {
                        location = locations[index];
                        AddStringProperty(location, "pdf_url", candidates, seenUrls);
                    }
                }

                AddNestedString(root, "open_access", "oa_url", candidates, seenUrls);
            }
        }
        catch (JsonException)
        {
            return;
        }
    }

    private static void AddGoogleScholarCandidates(
        AcademicWork work,
        List<Uri> candidates,
        HashSet<string> seenUrls)
    {
        JsonDocument? document = null;
        JsonElement root = default;
        JsonElement citation = default;
        JsonElement resources = default;
        JsonElement resource = default;
        string? fileFormat = null;
        string? link = null;
        int index = 0;

        if (LooksLikePdfUrl(work.Link))
        {
            AddCandidate(work.Link, candidates, seenUrls);
        }

        if (string.IsNullOrWhiteSpace(work.ProviderDetailPayload))
        {
            return;
        }

        try
        {
            using (document = JsonDocument.Parse(work.ProviderDetailPayload))
            {
                root = document.RootElement;

                if (!root.TryGetProperty("citation", out citation) ||
                    citation.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                if (citation.TryGetProperty("resources", out resources) &&
                    resources.ValueKind == JsonValueKind.Array)
                {
                    for (index = 0; index < resources.GetArrayLength(); index++)
                    {
                        resource = resources[index];
                        fileFormat = GetStringProperty(resource, "file_format");

                        if (!string.Equals(
                                fileFormat,
                                "PDF",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        link = GetStringProperty(resource, "link");
                        AddCandidate(link, candidates, seenUrls);
                    }
                }

                link = GetStringProperty(citation, "link");

                if (LooksLikePdfUrl(link))
                {
                    AddCandidate(link, candidates, seenUrls);
                }
            }
        }
        catch (JsonException)
        {
            return;
        }
    }

    private static void AddNestedString(
        JsonElement parent,
        string objectPropertyName,
        string stringPropertyName,
        List<Uri> candidates,
        HashSet<string> seenUrls)
    {
        JsonElement child = default;

        if (!parent.TryGetProperty(objectPropertyName, out child) ||
            child.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        AddStringProperty(child, stringPropertyName, candidates, seenUrls);
    }

    private static void AddStringProperty(
        JsonElement element,
        string propertyName,
        List<Uri> candidates,
        HashSet<string> seenUrls)
    {
        string? value = null;

        value = GetStringProperty(element, propertyName);
        AddCandidate(value, candidates, seenUrls);
    }

    private static string? GetStringProperty(
        JsonElement element,
        string propertyName)
    {
        JsonElement value = default;

        if (!element.TryGetProperty(propertyName, out value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static void AddCandidate(
        string? value,
        List<Uri> candidates,
        HashSet<string> seenUrls)
    {
        Uri? uri = null;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !seenUrls.Add(uri.AbsoluteUri))
        {
            return;
        }

        candidates.Add(uri);
    }

    private static bool LooksLikePdfUrl(string? value)
    {
        Uri? uri = null;

        return !string.IsNullOrWhiteSpace(value) &&
               Uri.TryCreate(value, UriKind.Absolute, out uri) &&
               uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }
}
