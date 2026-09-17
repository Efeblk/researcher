import type { ServiceResponse } from "@serenity-is/corelib";

export interface PublicationSummaryRow {
    Id?: number;
    PersonelID?: string;
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
    PersonelID?: string;
    PublicationSummaryIds?: number[];
    ApprovedCount?: number;
}

export interface ResearcherCollectResponse extends ServiceResponse {
    Researcher?: {
        PersonelID?: string;
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
        ScopusProfile?: {
            ScopusAuthorId?: string;
            DisplayName?: string;
            CurrentAffiliation?: string;
            DocumentsCount?: number;
            CollectedWorksCount?: number;
            CitationCount?: number;
            CitedByCount?: number;
            HIndex?: number;
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
        ProviderDetails?: ResearcherProviderDetails;
    };
    IsSaved?: boolean;
    FailureCode?: string;
    PublicationCount?: number;
    YoksisPublicationCount?: number;
    Messages?: string[];
    ProviderFeedback?: ProviderCollectionFeedback[];
}

export interface ResearcherProviderDetails {
    Orcid?: {
        Activities?: unknown;
        OtherNames?: unknown;
        Emails?: unknown;
    };
    OpenAlex?: {
        ExternalIdentifiers?: unknown;
        AlternativeNames?: unknown;
        RawAuthorNames?: unknown;
        Affiliations?: unknown;
        LastKnownInstitutions?: unknown;
        Topics?: unknown;
        TopicShare?: unknown;
    };
    Scopus?: {
        Orcid?: string;
        NameVariants?: unknown;
        CurrentAffiliation?: unknown;
        AffiliationHistory?: unknown;
        SubjectAreas?: unknown;
        CoauthorCount?: number;
    };
    GoogleScholar?: {
        Interests?: unknown;
        CoAuthors?: unknown;
        PublicAccess?: unknown;
        Thumbnail?: string;
    };
}

export interface ProviderCollectionReason {
    Code?: string;
    Description?: string;
    AffectedCount?: number | null;
}

export interface ProviderCollectionFeedback {
    Provider?: string;
    Status?: string;
    Unit?: string;
    RetrievedCount?: number;
    RetainedCount?: number | null;
    ExpectedCount?: number | null;
    Reasons?: ProviderCollectionReason[];
}

export interface ResearcherMetricsResponse extends ServiceResponse {
    PersonelID?: string;
    RecalculatedAt?: string;
}

export interface YoksisOperationResult {
    CategoryName?: string;
    IsSuccess?: boolean;
    RecordCount?: number;
    ExpectedDetailCount?: number;
    RetrievedDetailCount?: number;
    FailedDetailCount?: number;
    FailureReasons?: YoksisFailureSummary[];
    Errors?: string[];
}

export interface YoksisFailureSummary {
    Code?: string;
    Description?: string;
    AffectedCount?: number;
}

export interface YoksisCollectResponse extends ServiceResponse {
    PersonelID?: string;
    ResearcherDisplayName?: string;
    IsSaved?: boolean;
    IsDisabled?: boolean;
    IsCached?: boolean;
    YoksisRecordCount?: number;
    YoksisPublicationCount?: number;
    PublicationSummaryCount?: number;
    SuccessfulCategoryCount?: number;
    FailedCategoryCount?: number;
    TotalRecordCount?: number;
    PublicationDetailTotalCount?: number | null;
    PublicationDetailRetrievedCount?: number;
    PublicationDetailFailedCount?: number;
    PublicationFailureReasons?: YoksisFailureSummary[];
    FailureReasons?: YoksisFailureSummary[];
    StopReason?: string;
    Messages?: string[];
    Categories?: YoksisOperationResult[];
}
