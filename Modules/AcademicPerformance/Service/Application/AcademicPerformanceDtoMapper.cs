using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using System.Text.Json;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Application;

internal static class AcademicPerformanceDtoMapper
{
    public static AcademicResearcherDto? MapResearcher(
        Researcher? researcher,
        bool includeProviderDetails = false)
    {
        if (researcher is null)
        {
            return null;
        }

        return new AcademicResearcherDto
        {
            PersonelId = researcher.PersonelId,
            FirstName = researcher.FirstName,
            LastName = researcher.LastName,
            AcademicTitle = researcher.AcademicTitle,
            Department = researcher.Department,
            Orcid = researcher.Orcid,
            ScopusId = researcher.ScopusId,
            GoogleScholarId = researcher.GoogleScholarId,
            WebOfScienceResearcherId = researcher.WebOfScienceResearcherId,
            TcKimlikNo = researcher.TcKimlikNo,
            LastUpdatedAt = researcher.LastUpdatedAt,
            WosCitationCount = researcher.WosCitationCount,
            WosHIndex = researcher.WosHIndex,
            WosDocumentsCount = researcher.WosDocumentsCount,
            WosMetricsUpdatedAt = researcher.WosMetricsUpdatedAt,
            OpenAlexCitationCount = researcher.OpenAlexCitationCount,
            OpenAlexHIndex = researcher.OpenAlexHIndex,
            OpenAlexI10Index = researcher.OpenAlexI10Index,
            OpenAlexDocumentsCount = researcher.OpenAlexDocumentsCount,
            OpenAlexTwoYearMeanCitedness = researcher.OpenAlexTwoYearMeanCitedness,
            OpenAlexMetricsUpdatedAt = researcher.OpenAlexMetricsUpdatedAt,
            ScopusCitationCount = researcher.ScopusCitationCount,
            ScopusHIndex = researcher.ScopusHIndex,
            ScopusDocumentsCount = researcher.ScopusDocumentsCount,
            ScopusMetricsUpdatedAt = researcher.ScopusMetricsUpdatedAt,
            ScholarCitationCount = researcher.ScholarCitationCount,
            ScholarHIndex = researcher.ScholarHIndex,
            ScholarI10Index = researcher.ScholarI10Index,
            ScholarDocumentsCount = researcher.ScholarDocumentsCount,
            ScholarCitationCountRecent = researcher.ScholarCitationCountRecent,
            ScholarHIndexRecent = researcher.ScholarHIndexRecent,
            ScholarI10IndexRecent = researcher.ScholarI10IndexRecent,
            ScholarMetricsSinceYear = researcher.ScholarMetricsSinceYear,
            ScholarMetricsUpdatedAt = researcher.ScholarMetricsUpdatedAt,
            OrcidProfile = MapOrcidProfile(researcher.OrcidProfile),
            GoogleScholarProfile = MapGoogleScholarProfile(
                researcher.GoogleScholarProfile),
            OpenAlexProfile = MapOpenAlexProfile(researcher.OpenAlexProfile),
            ScopusProfile = MapScopusProfile(researcher.ScopusProfile),
            TrDizinProfile = researcher.TrDizinProfile is null ? null : new()
            {
                Orcid = researcher.TrDizinProfile.Orcid,
                AuthorId = researcher.TrDizinProfile.AuthorId,
                DisplayName = researcher.TrDizinProfile.DisplayName,
                PublicationCount = researcher.TrDizinProfile.PublicationCount,
                CitationCount = researcher.TrDizinProfile.CitationCount,
                ProjectCandidateCount = researcher.TrDizinProfile.ProjectCandidateCount,
                ProjectMatchedCount = researcher.TrDizinProfile.ProjectMatchedCount,
                ProjectUnmatchedCount = researcher.TrDizinProfile.ProjectUnmatchedCount,
                ProjectSearchComplete = researcher.TrDizinProfile.ProjectSearchComplete,
                Projects = (researcher.TrDizinProfile.Projects ?? []).Select(project => new TrDizinProjectDto
                {
                    Id = project.ProjectId,
                    ProjectNumber = project.ProjectNumber,
                    Title = project.Title,
                    StartedDate = project.StartedDate,
                    EndDate = project.EndDate,
                    ProjectGroup = project.ProjectGroup,
                    ResearchersJson = project.ResearchersJson,
                    Duty = project.Duty,
                    AbstractsJson = project.AbstractsJson,
                    KeywordsJson = project.KeywordsJson,
                    OutputsJson = project.OutputsJson,
                    AttachmentsJson = project.AttachmentsJson
                }).ToList(),
                LastUpdatedAt = researcher.TrDizinProfile.LastUpdatedAt
            },
            WebOfScienceProfile = MapWebOfScienceProfile(
                researcher.WebOfScienceProfile),
            ProviderDetails = includeProviderDetails
                ? MapProviderDetails(researcher)
                : null
        };
    }

    private static ResearcherProviderDetailsDto MapProviderDetails(Researcher researcher)
    {
        return new()
        {
            Orcid = MapOrcidDetails(researcher.OrcidProfile),
            OpenAlex = MapOpenAlexDetails(researcher.OpenAlexProfile),
            Scopus = MapScopusDetails(researcher.ScopusProfile),
            GoogleScholar = MapGoogleScholarDetails(researcher.GoogleScholarProfile)
        };
    }

    private static OrcidResearcherDetailsDto? MapOrcidDetails(OrcidProfile? profile)
    {
        if (profile is null)
            return null;
        JsonElement? root = ParseRoot(profile.RawDataJson);
        JsonElement? person = Property(root, "person");
        return new()
        {
            Activities = ParseRoot(profile.ActivitiesDetailsJson),
            OtherNames = Property(person, "other-names"),
            Emails = Property(person, "emails")
        };
    }

    private static OpenAlexResearcherDetailsDto? MapOpenAlexDetails(OpenAlexProfile? profile)
    {
        if (profile is null)
            return null;
        JsonElement? root = ParseRoot(profile.RawDataJson);
        root = SelectOpenAlexAuthor(root, profile.OpenAlexAuthorId);
        return new()
        {
            ExternalIdentifiers = Property(root, "ids"),
            AlternativeNames = Property(root, "display_name_alternatives"),
            RawAuthorNames = Property(root, "raw_author_names"),
            Affiliations = Property(root, "affiliations"),
            LastKnownInstitutions = Property(root, "last_known_institutions"),
            Topics = Property(root, "topics"),
            TopicShare = Property(root, "topic_share")
        };
    }

    private static JsonElement? SelectOpenAlexAuthor(
        JsonElement? root,
        string authorId)
    {
        JsonElement? results = Property(root, "results");
        if (results is not { ValueKind: JsonValueKind.Array } values)
            return root;
        foreach (JsonElement result in values.EnumerateArray())
        {
            if (string.Equals(
                String(Property(result, "id")),
                authorId,
                StringComparison.OrdinalIgnoreCase))
            {
                return result.Clone();
            }
        }
        return null;
    }

    private static ScopusResearcherDetailsDto? MapScopusDetails(ScopusProfile? profile)
    {
        if (profile is null)
            return null;
        JsonElement? root = ParseRoot(profile.RawDataJson);
        JsonElement? responses = Property(root, "author-retrieval-response");
        JsonElement? author = FirstOrSelf(responses);
        JsonElement? authorProfile = Property(author, "author-profile");
        JsonElement? core = Property(author, "coredata");
        return new()
        {
            Orcid = String(Property(core, "orcid")) ?? String(Property(author, "orcid")),
            NameVariants = Property(authorProfile, "name-variant"),
            CurrentAffiliation = Property(authorProfile, "affiliation-current"),
            AffiliationHistory = Property(authorProfile, "affiliation-history"),
            SubjectAreas = Property(author, "subject-areas"),
            CoauthorCount = Integer(Property(author, "coauthor-count")) ??
                Integer(Property(core, "coauthor-count"))
        };
    }

    private static GoogleScholarResearcherDetailsDto? MapGoogleScholarDetails(
        GoogleScholarProfile? profile)
    {
        if (profile is null)
            return null;
        JsonElement? root = FirstOrSelf(ParseRoot(profile.RawDataJson));
        JsonElement? author = Property(root, "author");
        return new()
        {
            Interests = Property(author, "interests"),
            CoAuthors = Property(root, "co_authors"),
            PublicAccess = Property(root, "public_access"),
            Thumbnail = String(Property(author, "thumbnail"))
        };
    }

    private static JsonElement? ParseRoot(string? json)
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

    private static JsonElement? Property(JsonElement? element, string name)
    {
        return element is { ValueKind: JsonValueKind.Object } value &&
            value.TryGetProperty(name, out JsonElement property) &&
            property.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? property.Clone()
                : null;
    }

    private static JsonElement? FirstOrSelf(JsonElement? element)
    {
        if (element is not JsonElement value)
            return null;
        if (value.ValueKind != JsonValueKind.Array)
            return value;
        return value.GetArrayLength() > 0 ? value[0].Clone() : null;
    }

    private static string? String(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static int? Integer(JsonElement? element)
    {
        if (element is not JsonElement value)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            return number;
        return value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), out number) ? number : null;
    }

    private static ScopusProfileSummaryDto? MapScopusProfile(ScopusProfile? profile)
    {
        if (profile is null)
            return null;
        return new()
        {
            ScopusAuthorId = profile.ScopusAuthorId,
            DisplayName = profile.DisplayName,
            CurrentAffiliation = profile.CurrentAffiliation,
            DocumentsCount = profile.DocumentsCount,
            CollectedWorksCount = profile.Works?.Count ?? profile.DocumentsCount,
            CitationCount = profile.CitationCount,
            CitedByCount = profile.CitedByCount,
            HIndex = profile.HIndex,
            LastUpdatedAt = profile.LastUpdatedAt
        };
    }

    private static OpenAlexProfileSummaryDto? MapOpenAlexProfile(
        OpenAlexProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return new OpenAlexProfileSummaryDto
        {
            OpenAlexAuthorId = profile.OpenAlexAuthorId,
            DisplayName = profile.DisplayName,
            LastKnownInstitution = profile.LastKnownInstitution,
            WorksCount = profile.WorksCount,
            CollectedWorksCount = profile.Works?.Count ?? profile.WorksCount,
            CitedByCount = profile.CitedByCount,
            HIndex = profile.HIndex,
            I10Index = profile.I10Index,
            TwoYearMeanCitedness = profile.TwoYearMeanCitedness,
            LastUpdatedAt = profile.LastUpdatedAt
        };
    }

    private static GoogleScholarProfileSummaryDto? MapGoogleScholarProfile(
        GoogleScholarProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return new GoogleScholarProfileSummaryDto
        {
            DisplayName = profile.DisplayName,
            Affiliations = profile.Affiliations,
            University = profile.University,
            ProfileUrl = profile.ProfileUrl,
            CitationCount = profile.CitationCount,
            CitationCountRecent = profile.CitationCountRecent,
            HIndex = profile.HIndex,
            HIndexRecent = profile.HIndexRecent,
            I10Index = profile.I10Index,
            I10IndexRecent = profile.I10IndexRecent,
            MetricsSinceYear = profile.MetricsSinceYear,
            DocumentsCount = GoogleScholarProfile.HasKnownDocumentsCount(profile.RawDataJson)
                ? profile.DocumentsCount
                : null,
            LastUpdatedAt = profile.LastUpdatedAt
        };
    }

    private static OrcidProfileSummaryDto? MapOrcidProfile(OrcidProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return new OrcidProfileSummaryDto
        {
            DisplayName = profile.DisplayName,
            CurrentOrganization = profile.CurrentOrganization,
            CurrentDepartment = profile.CurrentDepartment,
            CurrentRoleTitle = profile.CurrentRoleTitle,
            WorksCount = profile.WorksCount,
            EmploymentsCount = profile.EmploymentsCount,
            EducationsCount = profile.EducationsCount,
            FundingsCount = profile.FundingsCount,
            PeerReviewsCount = profile.PeerReviewsCount,
            RecordLastModifiedAt = profile.RecordLastModifiedAt,
            LastUpdatedAt = profile.LastUpdatedAt
        };
    }

    private static WebOfScienceProfileSummaryDto? MapWebOfScienceProfile(
        WebOfScienceProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return new WebOfScienceProfileSummaryDto
        {
            DisplayName = profile.DisplayName,
            PrimaryOrganization = profile.PrimaryOrganization,
            HIndex = profile.HIndex,
            DocumentsCount = profile.DocumentsCount,
            WosDocumentsCount = ReadDatabaseDocumentCount(profile.DocumentPagesJson, "WOS"),
            WokDocumentsCount = ReadDatabaseDocumentCount(profile.DocumentPagesJson, "WOK"),
            TotalTimesCited = profile.TotalTimesCited,
            TotalCitingPublications = profile.TotalCitingPublications,
            PeerReviewsCount = profile.PeerReviewsCount,
            LastUpdatedAt = profile.LastUpdatedAt
        };
    }

    private static int? ReadDatabaseDocumentCount(string? pagesJson, string databaseId)
    {
        if (string.IsNullOrWhiteSpace(pagesJson))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(pagesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(databaseId, out JsonElement pages) ||
                pages.ValueKind != JsonValueKind.Array || pages.GetArrayLength() == 0)
                return null;

            JsonElement firstPage = pages[0];
            if (firstPage.ValueKind == JsonValueKind.String)
            {
                using JsonDocument page = JsonDocument.Parse(firstPage.GetString()!);
                return ReadTotal(page.RootElement);
            }

            return ReadTotal(firstPage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadTotal(JsonElement page) =>
        page.ValueKind == JsonValueKind.Object &&
        page.TryGetProperty("metadata", out JsonElement metadata) &&
        metadata.ValueKind == JsonValueKind.Object &&
        metadata.TryGetProperty("total", out JsonElement total) &&
        total.ValueKind == JsonValueKind.Number &&
        total.TryGetInt32(out int value) && value >= 0 ? value : null;

    public static AcademicPublicationDto MapPublication(
        PublicationSummary publication,
        bool isApproved)
    {
        return new AcademicPublicationDto
        {
            Id = publication.Id,
            Title = publication.Title,
            PublicationYear = publication.PublicationYear,
            Doi = publication.Doi,
            Category = publication.Category.ToString(),
            Authors = publication.Authors,
            Publication = publication.Publication,
            PublicationUrl = publication.PublicationUrl,
            Sources = publication.Sources,
            IsApprovedForDisplay = isApproved
        };
    }
}
