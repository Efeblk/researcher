import type { ResearcherCollectResponse, YoksisCollectResponse } from "../../Contracts/AcademicPerformanceContracts";

const profileSummaryPanel = document.querySelector<HTMLElement>("#ResearcherSummary");
const googleScholarSummaryPanel = document.querySelector<HTMLElement>(
    "#GoogleScholarSummary");
const openAlexSummaryPanel = document.querySelector<HTMLElement>(
    "#OpenAlexSummary");
const webOfScienceSummaryPanel = document.querySelector<HTMLElement>(
    "#WebOfScienceSummary");
const yoksisSummaryPanel = document.querySelector<HTMLElement>("#YoksisSummary");
const providerComparisonPanel = document.querySelector<HTMLElement>(
    "#ProviderComparison");
const providerComparisonRows = document.querySelector<HTMLTableSectionElement>(
    "#ProviderComparisonRows");

export function showProfileSummary(researcher?: ResearcherCollectResponse["Researcher"]) {
    const profile = researcher?.OrcidProfile;

    if (!profileSummaryPanel || !profile) {
        if (profileSummaryPanel)
            profileSummaryPanel.hidden = true;
        return;
    }

    const displayName = [researcher?.FirstName, researcher?.LastName]
        .filter(Boolean)
        .join(" ") || profile.DisplayName || "Akademisyen";
    const values: Record<string, number | string> = {
        MetricsWorksCount: profile.WorksCount ?? 0,
        MetricsEmploymentsCount: profile.EmploymentsCount ?? 0,
        MetricsEducationsCount: profile.EducationsCount ?? 0,
        MetricsFundingsCount: profile.FundingsCount ?? 0,
        MetricsPeerReviewsCount: profile.PeerReviewsCount ?? 0,
        MetricsOrganization: profile.CurrentOrganization ?? "—",
        MetricsRecordUpdatedAt: formatDateTime(profile.RecordLastModifiedAt)
    };

    document.querySelector<HTMLElement>("#MetricsResearcherName")!.textContent = displayName;

    for (const [id, value] of Object.entries(values)) {
        const element = document.querySelector<HTMLElement>(`#${id}`);
        if (element)
            element.textContent = typeof value === "number" ? value.toLocaleString("tr-TR") : value;
    }

    profileSummaryPanel.hidden = false;
}

export function showWebOfScienceSummary(
    researcher?: ResearcherCollectResponse["Researcher"]) {
    const profile = researcher?.WebOfScienceProfile;

    if (!webOfScienceSummaryPanel || !profile) {
        if (webOfScienceSummaryPanel)
            webOfScienceSummaryPanel.hidden = true;
        return;
    }

    const displayName = [researcher?.FirstName, researcher?.LastName]
        .filter(Boolean)
        .join(" ") || profile.DisplayName || "Akademisyen";
    const values: Record<string, number | string> = {
        WebOfScienceHIndex: profile.HIndex ?? "—",
        WebOfScienceDocumentsCount: profile.DocumentsCount ?? 0,
        WebOfScienceTotalTimesCited: profile.TotalTimesCited ?? "—"
    };

    document.querySelector<HTMLElement>("#WebOfScienceResearcherName")!.textContent =
        displayName;

    for (const [id, value] of Object.entries(values)) {
        const element = document.querySelector<HTMLElement>(`#${id}`);
        if (element)
            element.textContent = typeof value === "number"
                ? value.toLocaleString("tr-TR")
                : value;
    }

    webOfScienceSummaryPanel.hidden = false;
}

interface ProviderComparisonRow {
    provider: string;
    identity: string;
    publications: number | string;
    citations: number | string;
    hIndex: number | string;
    i10Index: number | string;
    updatedAt: string;
    isOpenAlex?: boolean;
}

export function showProviderComparison(
    researcher?: ResearcherCollectResponse["Researcher"]) {
    if (!providerComparisonPanel || !providerComparisonRows || !researcher) {
        if (providerComparisonPanel)
            providerComparisonPanel.hidden = true;
        providerComparisonRows?.replaceChildren();
        return;
    }

    const orcid = researcher.OrcidProfile;
    const scholar = researcher.GoogleScholarProfile;
    const openAlex = researcher.OpenAlexProfile;
    const webOfScience = researcher.WebOfScienceProfile;
    const rows: ProviderComparisonRow[] = [
        {
            provider: "ORCID",
            identity: orcid
                ? [orcid.DisplayName, orcid.CurrentOrganization]
                    .filter(Boolean).join(" · ") || "—"
                : "Henüz veri yok",
            publications: orcid?.WorksCount ?? "—",
            citations: "—",
            hIndex: "—",
            i10Index: "—",
            updatedAt: formatDateTime(orcid?.LastUpdatedAt)
        },
        {
            provider: "Google Scholar",
            identity: scholar
                ? [scholar.DisplayName, scholar.University ?? scholar.Affiliations]
                    .filter(Boolean).join(" · ") || "—"
                : "Henüz veri yok",
            publications: scholar?.DocumentsCount ?? "—",
            citations: scholar?.CitationCount ?? "—",
            hIndex: scholar?.HIndex ?? "—",
            i10Index: scholar?.I10Index ?? "—",
            updatedAt: formatDateTime(scholar?.LastUpdatedAt)
        },
        {
            provider: "OpenAlex",
            identity: openAlex
                ? [openAlex.DisplayName, openAlex.LastKnownInstitution]
                    .filter(Boolean).join(" · ") || "—"
                : "Henüz veri yok",
            publications: openAlex
                ? `${(openAlex.WorksCount ?? 0).toLocaleString("tr-TR")} ` +
                    `(${(openAlex.CollectedWorksCount ?? 0).toLocaleString("tr-TR")} çekildi)`
                : "—",
            citations: openAlex?.CitedByCount ?? "—",
            hIndex: openAlex?.HIndex ?? "—",
            i10Index: openAlex?.I10Index ?? "—",
            updatedAt: formatDateTime(openAlex?.LastUpdatedAt),
            isOpenAlex: true
        },
        {
            provider: "Web of Science",
            identity: webOfScience
                ? [webOfScience.DisplayName, webOfScience.PrimaryOrganization]
                    .filter(Boolean).join(" · ") || "—"
                : "Henüz veri yok",
            publications: webOfScience?.DocumentsCount ?? "—",
            citations: webOfScience?.TotalTimesCited ?? "—",
            hIndex: webOfScience?.HIndex ?? "—",
            i10Index: "—",
            updatedAt: formatDateTime(webOfScience?.LastUpdatedAt)
        }
    ];

    providerComparisonRows.replaceChildren();

    for (const row of rows) {
        const tableRow = document.createElement("tr");
        if (row.isOpenAlex)
            tableRow.className = "openalex-comparison-row";

        const values = [
            row.provider,
            row.identity,
            row.publications,
            row.citations,
            row.hIndex,
            row.i10Index,
            row.updatedAt
        ];

        values.forEach((value, index) => {
            const cell = document.createElement(index === 0 ? "th" : "td");
            if (index === 0)
                cell.setAttribute("scope", "row");
            cell.textContent = typeof value === "number"
                ? value.toLocaleString("tr-TR")
                : value;
            tableRow.append(cell);
        });

        providerComparisonRows.append(tableRow);
    }

    providerComparisonPanel.hidden = false;
}

export function showGoogleScholarSummary(
    researcher?: ResearcherCollectResponse["Researcher"]) {
    const profile = researcher?.GoogleScholarProfile;

    if (!googleScholarSummaryPanel || !profile) {
        if (googleScholarSummaryPanel)
            googleScholarSummaryPanel.hidden = true;
        return;
    }

    const displayName = [researcher?.FirstName, researcher?.LastName]
        .filter(Boolean)
        .join(" ") || profile.DisplayName || "Akademisyen";
    const recentIndexes = profile.HIndexRecent !== undefined ||
        profile.I10IndexRecent !== undefined
        ? `${profile.HIndexRecent ?? "—"} / ${profile.I10IndexRecent ?? "—"}`
        : "—";
    const values: Record<string, number | string> = {
        GoogleScholarCitationCount: profile.CitationCount ?? "—",
        GoogleScholarHIndex: profile.HIndex ?? "—",
        GoogleScholarI10Index: profile.I10Index ?? "—",
        GoogleScholarDocumentsCount: profile.DocumentsCount ?? 0,
        GoogleScholarCitationCountRecent: profile.CitationCountRecent ?? "—",
        GoogleScholarRecentIndexes: recentIndexes
    };

    document.querySelector<HTMLElement>("#GoogleScholarResearcherName")!.textContent =
        displayName;

    const recentLabel = document.querySelector<HTMLElement>(
        "#GoogleScholarRecentLabel");
    if (recentLabel)
        recentLabel.textContent = profile.MetricsSinceYear
            ? `${profile.MetricsSinceYear} sonrası atıf`
            : "Yakın dönem atıf";

    for (const [id, value] of Object.entries(values)) {
        const element = document.querySelector<HTMLElement>(`#${id}`);
        if (element)
            element.textContent = typeof value === "number"
                ? value.toLocaleString("tr-TR")
                : value;
    }

    googleScholarSummaryPanel.hidden = false;
}

export function showOpenAlexSummary(
    researcher?: ResearcherCollectResponse["Researcher"]) {
    const profile = researcher?.OpenAlexProfile;

    if (!openAlexSummaryPanel || !profile) {
        if (openAlexSummaryPanel)
            openAlexSummaryPanel.hidden = true;
        return;
    }

    const displayName = [researcher?.FirstName, researcher?.LastName]
        .filter(Boolean)
        .join(" ") || profile.DisplayName || "Akademisyen";
    const values: Record<string, number | string> = {
        OpenAlexCitedByCount: profile.CitedByCount ?? "—",
        OpenAlexHIndex: profile.HIndex ?? "—",
        OpenAlexI10Index: profile.I10Index ?? "—",
        OpenAlexWorksCount: profile.WorksCount ?? 0,
        OpenAlexCollectedWorksCount: profile.CollectedWorksCount ?? 0,
        OpenAlexInstitution: profile.LastKnownInstitution ?? "—"
    };

    document.querySelector<HTMLElement>("#OpenAlexResearcherName")!.textContent =
        displayName;

    for (const [id, value] of Object.entries(values)) {
        const element = document.querySelector<HTMLElement>(`#${id}`);
        if (element)
            element.textContent = typeof value === "number"
                ? value.toLocaleString("tr-TR")
                : value;
    }

    openAlexSummaryPanel.hidden = false;
}

export function showYoksisSummary(response?: YoksisCollectResponse) {
    const categoryList = document.querySelector<HTMLElement>("#YoksisCategoryList");

    if (!yoksisSummaryPanel || !response) {
        if (yoksisSummaryPanel)
            yoksisSummaryPanel.hidden = true;
        return;
    }

    const values: Record<string, number> = {
        YoksisTotalRecordCount: response.TotalRecordCount ?? 0,
        YoksisSuccessfulCategoryCount: response.SuccessfulCategoryCount ?? 0,
        YoksisFailedCategoryCount: response.FailedCategoryCount ?? 0
    };

    for (const [id, value] of Object.entries(values)) {
        const element = document.querySelector<HTMLElement>(`#${id}`);
        if (element)
            element.textContent = value.toLocaleString("tr-TR");
    }

    if (categoryList) {
        categoryList.replaceChildren();

        for (const category of response.Categories ?? []) {
            const item = document.createElement("div");
            const name = document.createElement("span");
            const count = document.createElement("strong");

            item.className = category.IsSuccess
                ? "academic-category-result"
                : "academic-category-result error";
            name.textContent = category.CategoryName ?? "YÖKSİS kategorisi";
            count.textContent = category.IsSuccess
                ? `${(category.RecordCount ?? 0).toLocaleString("tr-TR")} kayıt`
                : "Alınamadı";
            item.title = category.Errors?.[0] ?? "";
            item.append(name, count);
            categoryList.append(item);
        }
    }

    yoksisSummaryPanel.hidden = false;
}

function formatDateTime(value?: string) {
    if (!value)
        return "—";

    const date = new Date(value);
    return Number.isNaN(date.getTime())
        ? "—"
        : date.toLocaleString("tr-TR");
}
