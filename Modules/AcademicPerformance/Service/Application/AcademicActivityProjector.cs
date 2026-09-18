using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Application;

internal static class AcademicActivityProjector
{
    private static readonly string[] PublicationOperations =
        ["getBildiriBilgisiV1", "getBildiriBilgisiDetayV1", "getMakaleBilgisiV1",
         "getMakaleBilgisiDetayV1", "getKitapBilgisiV1", "getKitapBilgisiDetayV1",
         "getPatentBilgisiV1", "getPatentBilgisiDetayV1"];
    private static readonly string[] IdentityMarkers =
        ["kimlik", "personel", "ozluk", "nufus", "identity"];

    public static List<AcademicActivityDto> Project(
        IEnumerable<YoksisRecord> yoksisRecords,
        OrcidProfile? orcidProfile,
        IEnumerable<TrDizinProject> trDizinProjects)
    {
        List<AcademicActivityDto> result = [];
        List<YoksisRecord> records = yoksisRecords.ToList();
        foreach (IGrouping<string, YoksisRecord> projectGroup in records
            .Where(record => record.OperationName.Contains("ProjeListesi", StringComparison.OrdinalIgnoreCase))
            .GroupBy(record => record.ExternalRecordId ?? "record:" + record.Id))
        {
            var selected = projectGroup
                .OrderByDescending(value => value.OperationName.Contains("Detay", StringComparison.OrdinalIgnoreCase))
                .Select(record => new { Record = record, Item = Parse(record.RecordJson) })
                .FirstOrDefault(value => value.Item is not null);
            if (selected?.Item is JsonElement item)
                result.Add(MapYoksis("Proje", item, selected.Record.ExternalRecordId));
        }

        foreach (YoksisRecord record in records)
        {
            string discriminator = record.CategoryName + " " + record.OperationName;
            if (record.OperationName.Contains("ProjeListesi", StringComparison.OrdinalIgnoreCase))
                continue;
            if (PublicationOperations.Contains(record.OperationName, StringComparer.OrdinalIgnoreCase) ||
                Contains(discriminator, IdentityMarkers))
                continue;
            JsonElement? item = Parse(record.RecordJson);
            if (item is null)
                continue;
            result.Add(MapYoksis(YoksisCategory(record), item.Value, record.ExternalRecordId));
        }

        foreach (var activity in ReadOrcid(orcidProfile?.ActivitiesDetailsJson))
            result.Add(activity);

        foreach (TrDizinProject project in trDizinProjects)
        {
            result.Add(new()
            {
                Category = "Proje",
                Provider = "TR Dizin",
                Title = Clean(project.Title),
                Date = JoinDates(project.StartedDate, project.EndDate),
                Organization = Clean(project.ProjectGroup),
                Role = Clean(project.Duty),
                SourceId = Clean(project.ProjectNumber) ?? Clean(project.ProjectId)
            });
        }

        return result
            .OrderBy(value => value.Category, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(value => value.Date, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<AcademicActivityDto> ReadOrcid(string? json)
    {
        JsonElement? root = Parse(json);
        if (root is not { ValueKind: JsonValueKind.Array } array)
            yield break;
        foreach (JsonElement wrapper in array.EnumerateArray())
        {
            string? category = DirectString(wrapper, "Category");
            if (string.IsNullOrWhiteSpace(category) || category.Equals("work", StringComparison.OrdinalIgnoreCase))
                continue;
            JsonElement item = Direct(wrapper, "Item") ?? wrapper;
            yield return MapOrcid(CategoryLabel(category), category, item,
                DirectString(wrapper, "PutCode") ?? FindString(item, "put-code"));
        }
    }

    private static AcademicActivityDto MapYoksis(string category, JsonElement item, string? sourceId)
    {
        string? start = DirectStrings(item, "BAS_TAR", "BASTAR", "BASTAR1", "BAS_TARIH");
        string? end = DirectStrings(item, "BIT_TAR", "BITTAR", "BITTAR1", "BIT_TARIH");
        return new()
        {
            Category = category,
            Provider = "YÖKSİS",
            Title = DirectStrings(item, "PROJE_AD", "ODUL_ADI", "DERS_ADI", "ADI", "PROGRAM_ADI",
                "GOREV_ADI", "YAYIN_YERI", "TEZ_ADI", "ETKINLIK_ADI", "YAYIN_ADI", "DIL_AD",
                "TEMEL_ALAN_AD", "KADRO_UNVAN_ADI"),
            Date = JoinDates(start, end) ?? DirectStrings(item, "ODUL_TARIH", "AKADEMIK_YIL", "YIL"),
            Organization = DirectStrings(item, "KURUM_AD", "KURUM_ADI", "KURULUS_ADI",
                "UNV_BIRIM_ADI", "UNIV_BIRIM_ADI", "UNIVERSITE_AD", "ETKINLIK_YERI"),
            Role = DirectStrings(item, "PROJE_KONUMU_AD", "KADRO_UNVAN_ADI", "HAKEMLIK_TURU_AD",
                "UYELIK_DURUMU_AD", "TUR_ADI", "GOREV_ADI"),
            SourceId = Clean(sourceId) ?? DirectStrings(item, "PROJE_ID", "PROJE_NO")
        };
    }

    private static AcademicActivityDto MapOrcid(
        string category, string categoryCode, JsonElement item, string? sourceId)
    {
        string? title = categoryCode switch
        {
            "funding" => PathString(item, "title", "title", "value"),
            "peer-review" => DirectString(item, "subject-name"),
            "research-resource" => PathString(item, "proposal", "title", "title", "value"),
            _ => DirectString(item, "role-title")
        };
        string? organization = categoryCode == "peer-review"
            ? PathString(item, "convening-organization", "name")
            : categoryCode == "research-resource"
                ? ResearchResourceOrganization(item)
                : PathString(item, "organization", "name");
        return new()
        {
            Category = category,
            Provider = "ORCID",
            Title = title,
            Date = categoryCode == "peer-review"
                ? ReadOrcidDate(item, "review-completion-date")
                : JoinDates(ReadOrcidDate(item, "start-date"), ReadOrcidDate(item, "end-date")),
            Organization = organization,
            Role = categoryCode == "peer-review" ? DirectString(item, "reviewer-role") : null,
            SourceId = Clean(sourceId)
        };
    }

    private static string? ReadOrcidDate(JsonElement item, string name)
    {
        JsonElement? value = Direct(item, name);
        if (value is not JsonElement date)
            return null;
        string? scalar = Scalar(date);
        if (scalar is not null)
            return scalar;
        string? year = PathString(date, "year", "value") ?? DirectString(date, "year");
        string? month = PathString(date, "month", "value") ?? DirectString(date, "month");
        string? day = PathString(date, "day", "value") ?? DirectString(date, "day");
        return string.Join("-", new[] { year, month, day }.Where(part => part is not null));
    }

    private static string? FindString(JsonElement root, string name) => DirectString(root, name);

    private static string? PathString(JsonElement root, params string[] path)
    {
        JsonElement current = root;
        foreach (string segment in path)
        {
            JsonElement? next = Direct(current, segment);
            if (next is not JsonElement value)
                return null;
            current = value;
        }
        return Scalar(current);
    }

    private static string? ResearchResourceOrganization(JsonElement item)
    {
        JsonElement? proposal = Direct(item, "proposal");
        JsonElement? host = proposal is JsonElement proposalValue
            ? Direct(proposalValue, "host") : null;
        JsonElement? organizations = host is JsonElement hostValue
            ? Direct(hostValue, "organization") : null;
        if (organizations is not { ValueKind: JsonValueKind.Array } values)
            return null;
        foreach (JsonElement organization in values.EnumerateArray())
        {
            string? name = DirectString(organization, "name");
            if (name is not null)
                return name;
        }
        return null;
    }

    private static string? DirectStrings(JsonElement root, params string[] names) =>
        names.Select(name => DirectString(root, name)).FirstOrDefault(value => value is not null);

    private static JsonElement? Direct(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        foreach (JsonProperty property in root.EnumerateObject())
            if (KeyEquals(property.Name, name))
                return property.Value;
        return null;
    }

    private static string? DirectString(JsonElement root, string name) =>
        Direct(root, name) is JsonElement value ? Scalar(value) : null;

    private static string? Scalar(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            return Clean(value.GetString());
        if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True ||
            value.ValueKind == JsonValueKind.False)
            return Clean(value.ToString());
        if (value.ValueKind == JsonValueKind.Object)
        {
            JsonElement? wrapped = Direct(value, "value") ?? Direct(value, "content") ??
                Direct(value, "title") ?? Direct(value, "name");
            return wrapped is JsonElement item ? Scalar(item) : null;
        }
        return null;
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CategoryLabel(string category) => category.ToLowerInvariant() switch
    {
        "employment" => "İstihdam",
        "education" => "Eğitim",
        "qualification" => "Yeterlilik",
        "invited-position" => "Davetli görev",
        "distinction" => "Ödül",
        "membership" => "Üyelik",
        "service" => "Hizmet",
        "funding" => "Fonlama",
        "peer-review" => "Hakemlik",
        "research-resource" => "Araştırma kaynağı",
        _ => category
    };

    private static string YoksisCategory(YoksisRecord record) => record.OperationName switch
    {
        "getirProjeListesi" or "getirProjeListesiDetay" => "Proje",
        "getOdulListesiV1" => "Ödül",
        "getirDersListesi" => "Ders",
        "getHakemlikBilgisiV1" => "Hakemlik",
        "getirAkademikGorevListesi" => "Akademik görev",
        "getirIdariGorevListesi" => "İdari görev",
        "getirOgrenimBilgisiListesi" => "Eğitim",
        "getirUyelikListesi" => "Üyelik",
        "getirTezDanismanListesi" => "Tez danışmanlığı",
        "getSanatsalFaalV1" => "Sanatsal faaliyet",
        "getArastirmaSertifkaBilgisiV1" => "Araştırma / sertifika",
        "getirUnvDisiDeneyimListesi" => "Üniversite dışı deneyim",
        "getEditorlukBilgisiV1" => "Editörlük",
        "getirYabanciDilListesi" => "Yabancı dil",
        "getTemelAlanBilgisiV1" => "Temel alan",
        _ => string.IsNullOrWhiteSpace(record.CategoryName) ? "Diğer faaliyet" : record.CategoryName
    };

    private static string? JoinDates(string? start, string? end)
    {
        start = Clean(start);
        end = Clean(end);
        return (start, end) switch
        {
            (null, null) => null,
            (not null, null) => start,
            (null, not null) => end,
            _ => start + " - " + end
        };
    }

    private static bool Contains(string value, IEnumerable<string> markers)
    {
        string normalized = Normalize(value);
        return markers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static bool KeyEquals(string first, string second) => Normalize(first) == Normalize(second);
    private static string Normalize(string value) => string.Concat(value.ToLowerInvariant()
        .Normalize(System.Text.NormalizationForm.FormD)
        .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) !=
            System.Globalization.UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character)));
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
