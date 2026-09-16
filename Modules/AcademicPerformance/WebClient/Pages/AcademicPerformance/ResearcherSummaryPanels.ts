import type { ProviderCollectionFeedback, ResearcherCollectResponse, YoksisCollectResponse } from "../../Contracts/AcademicPerformanceContracts";

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

export function formatProviderCoverage(feedback: ProviderCollectionFeedback) {
    const retrieved = feedback.RetrievedCount ?? 0;
    const expected = feedback.ExpectedCount;
    const unit = feedback.Unit === "DOI" ? "DOI işlendi" : "yayın alındı";
    if (feedback.Status === "Skipped")
        return "Atlandı";
    if (feedback.Status === "Cached")
        return "Güncel önbellek kullanıldı";
    const retained = feedback.RetainedCount;
    if (retained != null)
        return `${retrieved.toLocaleString("tr-TR")} okundu; ` +
            `${retained.toLocaleString("tr-TR")} saklandı` +
            (expected == null ? "; toplam bilinmiyor" :
                ` / ${expected.toLocaleString("tr-TR")} bekleniyordu`);
    return expected == null
        ? `${retrieved.toLocaleString("tr-TR")} ${unit}; toplam bilinmiyor`
        : `${retrieved.toLocaleString("tr-TR")} / ${expected.toLocaleString("tr-TR")} ${unit}`;
}

export function formatProviderStatus(status?: string) {
    return ({ Succeeded: "Tamamlandı", Cached: "Önbellek", Skipped: "Atlandı",
        NotFound: "Bulunamadı", Partial: "Kısmi", Failed: "Başarısız",
        CachedOrNoWork: "Güncel" } as Record<string, string>)[status ?? ""] ?? "Bilinmiyor";
}

export function showProviderCollectionFeedback(feedback?: ProviderCollectionFeedback[]) {
    const panel = document.querySelector<HTMLElement>("#ProviderCollectionFeedback");
    const list = document.querySelector<HTMLElement>("#ProviderCollectionFeedbackList");
    if (!panel || !list || !feedback?.length) {
        if (panel)
            panel.hidden = true;
        list?.replaceChildren();
        return;
    }
    list.replaceChildren();
    for (const provider of feedback) {
        const item = document.createElement("div");
        item.className = provider.Status === "Failed"
            ? "academic-category-result error"
            : provider.Status === "Partial"
                ? "academic-category-result warning"
                : "academic-category-result";
        const name = document.createElement("strong");
        name.textContent = provider.Provider ?? "Sağlayıcı";
        const coverage = document.createElement("span");
        coverage.textContent = `${formatProviderStatus(provider.Status)} · ${formatProviderCoverage(provider)}`;
        item.append(name, coverage);
        for (const reason of provider.Reasons ?? []) {
            const detail = document.createElement("small");
            const count = reason.AffectedCount == null
                ? provider.Status === "Skipped" ? "" : "etkilenen sayı bilinmiyor"
                : `${reason.AffectedCount.toLocaleString("tr-TR")} ${provider.Unit === "DOI" ? "DOI" :
                    provider.Unit === "database record" ? "veritabanı satırı" : "yayın"}`;
            detail.textContent = count
                ? `${reason.Description ?? "Ayrıntı yok"} (${count})`
                : reason.Description ?? "Ayrıntı yok";
            item.append(detail);
        }
        list.append(item);
    }
    panel.hidden = false;
}

export function formatYoksisCoverage(retrieved = 0, total?: number) {
    return total == null
        ? `${retrieved.toLocaleString("tr-TR")} / toplam bilinmiyor`
        : `${retrieved.toLocaleString("tr-TR")} / ${total.toLocaleString("tr-TR")}`;
}

export function formatYoksisReason(
    description: string | undefined,
    affectedCount = 0,
    unit = "") {
    return `${description ?? "YÖKSİS verisi alınamadı"}: ` +
        `${affectedCount.toLocaleString("tr-TR")}${unit ? ` ${unit}` : ""}`;
}

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
    const coverage = document.querySelector<HTMLElement>("#YoksisPublicationCoverage");
    const failureSummary = document.querySelector<HTMLElement>("#YoksisFailureSummary");
    const publicationFailureSummary = document.querySelector<HTMLElement>("#YoksisPublicationFailureSummary");

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

    if (coverage) {
        const retrieved = response.PublicationDetailRetrievedCount ?? 0;
        const total = response.PublicationDetailTotalCount;
        coverage.textContent = formatYoksisCoverage(retrieved, total);
    }

    if (failureSummary) {
        const reasons = response.FailureReasons ?? [];
        failureSummary.replaceChildren();
        failureSummary.hidden = reasons.length === 0;
        if (reasons.length > 0) {
            const heading = document.createElement("strong");
            heading.textContent = "Eksik kalan kategori veya ayrıntı kayıtları:";
            const list = document.createElement("ul");
            for (const reason of reasons) {
                const item = document.createElement("li");
                item.textContent = formatYoksisReason(
                    reason.Description, reason.AffectedCount);
                list.append(item);
            }
            failureSummary.append(heading, list);
        }
    }

    if (publicationFailureSummary) {
        const reasons = response.PublicationFailureReasons ?? [];
        publicationFailureSummary.replaceChildren();
        publicationFailureSummary.hidden = reasons.length === 0;
        if (reasons.length > 0) {
            const heading = document.createElement("strong");
            heading.textContent = "Alınamayan yayın ayrıntıları:";
            const list = document.createElement("ul");
            for (const reason of reasons) {
                const item = document.createElement("li");
                item.textContent = formatYoksisReason(
                    reason.Description ?? "Yayın ayrıntısı alınamadı",
                    reason.AffectedCount,
                    "yayın");
                list.append(item);
            }
            publicationFailureSummary.append(heading, list);
        }
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
                ? category.ExpectedDetailCount == null
                    ? `${(category.RecordCount ?? 0).toLocaleString("tr-TR")} kayıt`
                    : `${(category.RetrievedDetailCount ?? 0).toLocaleString("tr-TR")} / ` +
                        `${category.ExpectedDetailCount.toLocaleString("tr-TR")} ayrıntı`
                : category.ExpectedDetailCount == null
                    ? "Alınamadı"
                    : `${(category.RetrievedDetailCount ?? 0).toLocaleString("tr-TR")} / ` +
                        `${category.ExpectedDetailCount.toLocaleString("tr-TR")} ayrıntı`;
            item.title = category.FailureReasons?.map(reason =>
                `${reason.Description}: ${reason.AffectedCount ?? 0}`).join("; ") ?? "";
            item.append(name, count);
            for (const reason of category.FailureReasons ?? []) {
                const detail = document.createElement("small");
                detail.textContent = formatYoksisReason(
                    reason.Description, reason.AffectedCount);
                item.append(detail);
            }
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
