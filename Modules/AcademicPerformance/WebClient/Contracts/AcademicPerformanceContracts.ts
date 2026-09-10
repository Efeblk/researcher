import type { ServiceResponse } from "@serenity-is/corelib";

export interface PublicationSummaryRow {
    Id?: number;
    Title?: string;
    PublicationYear?: number;
    Category?: string;
    Authors?: string;
    Publication?: string;
    Doi?: string;
    PublicationUrl?: string;
    Sources?: string;
    IsApprovedForDisplay?: boolean;
}

export interface PublicationDisplayApprovalResponse extends ServiceResponse {
    PublicationSummaryIds?: number[];
    ApprovedCount?: number;
}

export interface ResearcherCollectResponse extends ServiceResponse {
    Researcher?: {
        FirstName?: string;
        LastName?: string;
        OrcidProfile?: {
            DisplayName?: string;
            CurrentOrganization?: string;
            WorksCount?: number;
            EmploymentsCount?: number;
            EducationsCount?: number;
            FundingsCount?: number;
            PeerReviewsCount?: number;
            RecordLastModifiedAt?: string;
            LastUpdatedAt?: string;
        };
        GoogleScholarProfile?: {
            DisplayName?: string;
            Affiliations?: string;
            University?: string;
            ProfileUrl?: string;
            CitationCount?: number;
            CitationCountRecent?: number;
            HIndex?: number;
            HIndexRecent?: number;
            I10Index?: number;
            I10IndexRecent?: number;
            MetricsSinceYear?: number;
            DocumentsCount?: number;
            LastUpdatedAt?: string;
        };
        OpenAlexProfile?: {
            OpenAlexAuthorId?: string;
            DisplayName?: string;
            LastKnownInstitution?: string;
            WorksCount?: number;
            CollectedWorksCount?: number;
            CitedByCount?: number;
            HIndex?: number;
            I10Index?: number;
            TwoYearMeanCitedness?: number;
            LastUpdatedAt?: string;
        };
        WebOfScienceProfile?: {
            DisplayName?: string;
            PrimaryOrganization?: string;
            HIndex?: number;
            DocumentsCount?: number;
            TotalTimesCited?: number;
            TotalCitingPublications?: number;
            PeerReviewsCount?: number;
            LastUpdatedAt?: string;
        };
    };
    IsSaved?: boolean;
    Messages?: string[];
}

export interface YoksisOperationResult {
    CategoryName?: string;
    IsSuccess?: boolean;
    RecordCount?: number;
    Errors?: string[];
}

export interface YoksisCollectResponse extends ServiceResponse {
    ResearcherDisplayName?: string;
    IsSaved?: boolean;
    YoksisRecordCount?: number;
    YoksisPublicationCount?: number;
    PublicationSummaryCount?: number;
    SuccessfulCategoryCount?: number;
    FailedCategoryCount?: number;
    TotalRecordCount?: number;
    Messages?: string[];
    Categories?: YoksisOperationResult[];
}
