import { EntityGrid, ListRequest, serviceRequest } from "@serenity-is/corelib";
import { Column } from "@serenity-is/sleekgrid";

const providerIdentifierStorageKey =
    "AcademicPerformance.ProviderIdentifiers.v1";

interface PublicationSummaryRow {
    Id?: number;
    ResearcherId?: number;
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

interface PublicationDisplayApprovalResponse {
    ResearcherId?: number;
    PublicationSummaryIds?: number[];
    ApprovedCount?: number;
}

interface ResearcherCollectResponse {
    Researcher?: {
        Id?: number;
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

interface YoksisOperationResult {
    CategoryName?: string;
    IsSuccess?: boolean;
    RecordCount?: number;
    Errors?: string[];
}

interface YoksisCollectResponse {
    ResearcherId?: number;
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

class PublicationSummaryGrid extends EntityGrid<PublicationSummaryRow> {
    private researcherId = 0;
    private approvedPublicationIds = new Set<number>();

    protected override useAsync() { return true; }
    protected override getIdProperty() { return "Id"; }
    protected override getLocalTextPrefix() { return "AcademicPerformance.PublicationSummary"; }
    protected override getService() { return "AcademicPerformance/PublicationSummary"; }
    protected override getInitialTitle() { return "Yayınlar"; }
    protected override getButtons() { return []; }
    protected override getGridCanLoad() { return this.researcherId > 0; }

    protected override createColumns(): Column<PublicationSummaryRow>[] {
        return [
            {
                field: "IsApprovedForDisplay",
                name: "Okulda Göster",
                width: 110,
                sortable: false,
                format: context => {
                    const publicationId = context.item.Id ?? 0;
                    const checkbox = document.createElement("input");
                    checkbox.type = "checkbox";
                    checkbox.className = "academic-publication-approval";
                    checkbox.checked = this.approvedPublicationIds.has(publicationId);
                    checkbox.disabled = publicationId <= 0;
                    checkbox.setAttribute(
                        "aria-label",
                        `${context.item.Title ?? "Yayın"} okulda gösterilsin`);
                    checkbox.addEventListener("change", () => {
                        if (checkbox.checked)
                            this.approvedPublicationIds.add(publicationId);
                        else
                            this.approvedPublicationIds.delete(publicationId);

                        updateSelectionCount(this.approvedPublicationIds.size);
                    });
                    return checkbox;
                }
            },
            { field: "Title", name: "Başlık", width: 360 },
            { field: "PublicationYear", name: "Yıl", width: 70 },
            { field: "Category", name: "Tür", width: 130 },
            { field: "Authors", name: "Yazarlar", width: 230 },
            { field: "Publication", name: "Yayın Yeri", width: 180 },
            { field: "Doi", name: "DOI", width: 160 },
            {
                field: "PublicationUrl",
                name: "Yayın",
                width: 80,
                format: context => createExternalLink(context.value, "Aç")
            },
            { field: "Sources", name: "Kaynaklar", width: 120 }
        ];
    }

    protected override onViewSubmit() {
        if (!super.onViewSubmit())
            return false;

        const request = this.view.params as ListRequest;
        request.EqualityFilter = {
            ...(request.EqualityFilter ?? {}),
            ResearcherId: this.researcherId
        };
        return true;
    }

    async setResearcher(researcherId: number, displayName?: string) {
        this.researcherId = researcherId;
        this.approvedPublicationIds.clear();
        this.setTitle(displayName ? `${displayName} - Yayınlar` : "Yayınlar");
        setSelectionControlsEnabled(false);
        updateSelectionCount(0);

        try {
            const response = await serviceRequest<PublicationDisplayApprovalResponse>(
                "AcademicPerformance/PublicationDisplayApproval/Get",
                { ResearcherId: researcherId, PublicationSummaryIds: [] });

            if (this.researcherId !== researcherId)
                return;

            this.approvedPublicationIds = new Set(response.PublicationSummaryIds ?? []);
            updateSelectionCount(this.approvedPublicationIds.size);
            setSelectionControlsEnabled(true);
        }
        catch (error) {
            const message = error instanceof Error ? error.message : String(error);
            showSelectionStatus("error", `Kayıtlı yayın seçimleri okunamadı: ${message}`);
        }
        finally {
            if (this.researcherId === researcherId)
                this.refresh();
        }
    }

    async saveApprovals() {
        if (this.researcherId <= 0)
            throw new Error("Önce bir akademisyen araştırın.");

        return serviceRequest<PublicationDisplayApprovalResponse>(
            "AcademicPerformance/PublicationDisplayApproval/Save",
            {
                ResearcherId: this.researcherId,
                PublicationSummaryIds: [...this.approvedPublicationIds]
            });
    }

    getApprovedCount() {
        return this.approvedPublicationIds.size;
    }
}

function createExternalLink(value: unknown, text: string) {
    if (typeof value !== "string")
        return "";

    let url: URL;
    try {
        url = new URL(value);
    }
    catch {
        return "";
    }

    if (url.protocol !== "http:" && url.protocol !== "https:")
        return "";

    const link = document.createElement("a");
    link.href = url.href;
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    link.textContent = text;
    return link;
}

const form = document.querySelector<HTMLFormElement>("#ResearcherSearchForm");
const button = document.querySelector<HTMLButtonElement>("#ResearchButton");
const myPublicationsButton = document.querySelector<HTMLButtonElement>(
    "#MyPublicationsButton");
const status = document.querySelector<HTMLElement>("#ResearchStatus");
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
const saveSelectionsButton = document.querySelector<HTMLButtonElement>(
    "#SavePublicationSelections");
const selectionCount = document.querySelector<HTMLElement>("#SelectionCount");
const selectionStatus = document.querySelector<HTMLElement>("#SelectionStatus");
const grid = new PublicationSummaryGrid({ element: "#PublicationGrid" });
document.querySelector<HTMLInputElement>(".s-QuickSearchInput")
    ?.setAttribute("placeholder", "Yayınlarda ara...");

function valueOf(id: string) {
    return document.querySelector<HTMLInputElement>(`#${id}`)?.value.trim() ?? "";
}

function getProviderIdentifierInputs() {
    return [...document.querySelectorAll<HTMLInputElement>(
        "[data-provider-identifier]")];
}

function rememberProviderIdentifiers() {
    const identifiers: Record<string, string> = {};

    for (const input of getProviderIdentifierInputs()) {
        const providerName = input.dataset.providerIdentifier;
        const value = input.value.trim();

        if (providerName && value)
            identifiers[providerName] = value;
    }

    try {
        localStorage.setItem(
            providerIdentifierStorageKey,
            JSON.stringify(identifiers));
    }
    catch {
        // Storage can be disabled; normal manual search remains available.
    }
}

function fillRememberedProviderIdentifiers() {
    let identifiers: Record<string, string> = {};

    try {
        const storedValue = localStorage.getItem(providerIdentifierStorageKey);
        identifiers = storedValue
            ? JSON.parse(storedValue) as Record<string, string>
            : {};
    }
    catch {
        identifiers = {};
    }

    let filledCount = 0;

    for (const input of getProviderIdentifierInputs()) {
        const providerName = input.dataset.providerIdentifier;
        const value = providerName ? identifiers[providerName] : undefined;

        if (!value)
            continue;

        input.value = value;
        filledCount++;
    }

    return filledCount;
}

function setResearchButtonsEnabled(enabled: boolean) {
    if (button)
        button.disabled = !enabled;
    if (myPublicationsButton)
        myPublicationsButton.disabled = !enabled;
}

function showStatus(kind: "info" | "success" | "error", message: string) {
    if (!status)
        return;

    status.className = `academic-status visible ${kind}`;
    status.textContent = message;
}

function showSelectionStatus(
    kind: "info" | "success" | "error",
    message: string) {
    if (!selectionStatus)
        return;

    selectionStatus.className = `academic-status visible ${kind}`;
    selectionStatus.textContent = message;
}

function updateSelectionCount(count: number) {
    if (selectionCount)
        selectionCount.textContent = `${count.toLocaleString("tr-TR")} yayın seçildi`;
}

function setSelectionControlsEnabled(enabled: boolean) {
    if (saveSelectionsButton)
        saveSelectionsButton.disabled = !enabled;
}

function showProfileSummary(researcher?: ResearcherCollectResponse["Researcher"]) {
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

function showWebOfScienceSummary(
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

function showProviderComparison(
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

function showGoogleScholarSummary(
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

function showOpenAlexSummary(
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

function showYoksisSummary(response?: YoksisCollectResponse) {
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

function getErrorMessage(error: unknown) {
    return error instanceof Error ? error.message : String(error);
}

form?.addEventListener("submit", async event => {
    event.preventDefault();

    const identifiers = [
        valueOf("Orcid"),
        valueOf("GoogleScholarId"),
        valueOf("WebOfScienceResearcherId")
    ].filter(Boolean);
    const tcKimlikNo = valueOf("TcKimlikNo");

    if (!identifiers.length && !tcKimlikNo) {
        showStatus(
            "error",
            "ORCID, Google Scholar ID, Web of Science ResearcherID veya " +
            "T.C. kimlik no girin.");
        return;
    }

    if (tcKimlikNo && !/^[1-9][0-9]{10}$/.test(tcKimlikNo)) {
        showStatus("error", "T.C. kimlik numarası 11 haneli olmalıdır.");
        return;
    }

    setResearchButtonsEnabled(false);
    showProfileSummary(undefined);
    showGoogleScholarSummary(undefined);
    showOpenAlexSummary(undefined);
    showWebOfScienceSummary(undefined);
    showProviderComparison(undefined);
    showYoksisSummary(undefined);
    showStatus("info", "Akademik sağlayıcılar araştırılıyor. Bu işlem biraz sürebilir...");

    try {
        const statusMessages: string[] = [];
        const errors: string[] = [];
        let hasSuccessfulResult = false;
        let linkedResearcherId = 0;

        if (identifiers.length) {
            try {
                const response = await serviceRequest<ResearcherCollectResponse>(
                    "AcademicPerformance/V1/Collect",
                    {
                        Orcid: valueOf("Orcid") || undefined,
                        GoogleScholarId: valueOf("GoogleScholarId") || undefined,
                        WebOfScienceResearcherId:
                            valueOf("WebOfScienceResearcherId") || undefined
                    });
                const researcherId = response.Researcher?.Id ?? 0;
                const messages = (response.Messages ?? []).filter(Boolean).join("\n");

                if (response.IsSaved && researcherId) {
                    const displayName = [
                        response.Researcher?.FirstName,
                        response.Researcher?.LastName
                    ].filter(Boolean).join(" ") ||
                        response.Researcher?.OrcidProfile?.DisplayName ||
                        response.Researcher?.GoogleScholarProfile?.DisplayName ||
                        response.Researcher?.OpenAlexProfile?.DisplayName ||
                        response.Researcher?.WebOfScienceProfile?.DisplayName;

                    linkedResearcherId = researcherId;
                    hasSuccessfulResult = true;
                    statusMessages.push(messages || "Yayın araştırması tamamlandı.");
                    rememberProviderIdentifiers();
                    showProfileSummary(response.Researcher);
                    showGoogleScholarSummary(response.Researcher);
                    showOpenAlexSummary(response.Researcher);
                    showWebOfScienceSummary(response.Researcher);
                    showProviderComparison(response.Researcher);
                    await grid.setResearcher(researcherId, displayName);
                }
                else {
                    errors.push(
                        messages ||
                        "Yayın araştırması tamamlandı ancak kayıt oluşturulamadı.");
                }
            }
            catch (error) {
                errors.push(
                    `Yayın araştırması tamamlanamadı: ${getErrorMessage(error)}`);
            }
        }

        if (tcKimlikNo) {
            try {
                const response = await serviceRequest<YoksisCollectResponse>(
                    "AcademicPerformance/V1/Yoksis/Collect",
                    {
                        ResearcherId: linkedResearcherId || undefined,
                        TcKimlikNo: tcKimlikNo,
                        IncludeRecords: false,
                        IncludeRawResponses: false
                    });
                const successfulCount = response.SuccessfulCategoryCount ?? 0;
                const failedCount = response.FailedCategoryCount ?? 0;
                const researcherId = response.ResearcherId ?? 0;

                showYoksisSummary(response);

                if (response.IsSaved && researcherId) {
                    linkedResearcherId = researcherId;
                    hasSuccessfulResult = true;
                    statusMessages.push(
                        `YÖKSİS: ${(response.YoksisRecordCount ?? 0)
                            .toLocaleString("tr-TR")} kategori kaydı saklandı, ` +
                        `${(response.YoksisPublicationCount ?? 0)
                            .toLocaleString("tr-TR")} yayın kaydedildi, ` +
                        `${(response.PublicationSummaryCount ?? 0)
                            .toLocaleString("tr-TR")} ortak yayın özeti hazırlandı.`);
                    await grid.setResearcher(
                        researcherId,
                        response.ResearcherDisplayName);
                }
                else if (successfulCount > 0) {
                    errors.push(
                        "YÖKSİS verileri alındı ancak akademisyen kaydına yazılamadı.");
                }

                if (failedCount > 0) {
                    errors.push(
                        `YÖKSİS: ${failedCount.toLocaleString("tr-TR")} kategori alınamadı.`);
                }
            }
            catch (error) {
                errors.push(
                    `YÖKSİS sorgusu tamamlanamadı: ${getErrorMessage(error)}`);
            }
        }

        const combinedMessage = [...statusMessages, ...errors]
            .filter(Boolean)
            .join("\n");

        if (hasSuccessfulResult && errors.length > 0)
            showStatus("info", combinedMessage);
        else if (hasSuccessfulResult)
            showStatus("success", combinedMessage || "Araştırma tamamlandı.");
        else
            showStatus("error", combinedMessage || "Araştırma tamamlanamadı.");
    }
    catch (error) {
        showStatus("error", `Araştırma tamamlanamadı: ${getErrorMessage(error)}`);
    }
    finally {
        const tcKimlikInput = document.querySelector<HTMLInputElement>("#TcKimlikNo");
        if (tcKimlikInput)
            tcKimlikInput.value = "";
        setResearchButtonsEnabled(true);
    }
});

myPublicationsButton?.addEventListener("click", () => {
    const filledCount = fillRememberedProviderIdentifiers();

    if (filledCount === 0) {
        showStatus(
            "error",
            "Daha önce başarıyla kullanılan bir sağlayıcı kimliği bulunamadı. " +
            "Önce bir sağlayıcı kimliğiyle başarılı araştırma yapın.");
        return;
    }

    form?.requestSubmit();
});

saveSelectionsButton?.addEventListener("click", async () => {
    setSelectionControlsEnabled(false);
    showSelectionStatus("info", "Yayın tercihleri kaydediliyor...");

    try {
        const response = await grid.saveApprovals();
        const approvedCount = response.ApprovedCount ?? grid.getApprovedCount();
        updateSelectionCount(approvedCount);
        showSelectionStatus(
            "success",
            `${approvedCount.toLocaleString("tr-TR")} yayın okulda gösterilmek üzere kaydedildi.`);
    }
    catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        showSelectionStatus("error", `Yayın tercihleri kaydedilemedi: ${message}`);
    }
    finally {
        setSelectionControlsEnabled(true);
    }
});

document.querySelector(".academic-menu-toggle")?.addEventListener("click", () => {
    document.body.classList.toggle("academic-sidebar-open");
});
