using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Application;

internal static class AcademicPerformanceDtoMapper
{
    public static AcademicResearcherDto? MapResearcher(Researcher? researcher)
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
            TrDizinProfile = researcher.TrDizinProfile is null ? null : new()
            {
                Orcid = researcher.TrDizinProfile.Orcid,
                AuthorId = researcher.TrDizinProfile.AuthorId,
                DisplayName = researcher.TrDizinProfile.DisplayName,
                PublicationCount = researcher.TrDizinProfile.PublicationCount,
                CitationCount = researcher.TrDizinProfile.CitationCount,
                LastUpdatedAt = researcher.TrDizinProfile.LastUpdatedAt
            },
            WebOfScienceProfile = MapWebOfScienceProfile(
                researcher.WebOfScienceProfile)
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
            DocumentsCount = profile.DocumentsCount,
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
            TotalTimesCited = profile.TotalTimesCited,
            TotalCitingPublications = profile.TotalCitingPublications,
            PeerReviewsCount = profile.PeerReviewsCount,
            LastUpdatedAt = profile.LastUpdatedAt
        };
    }

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
