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
            EmploymentsCount?: number;
            EducationsCount?: number;
        };
        Metrics?: {
            WorksCount?: number;
            CitedByCount?: number;
            HIndex?: number;
            I10Index?: number;
            Source?: string;
        };
    };
    IsSaved?: boolean;
    Messages?: string[];
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
const metricsPanel = document.querySelector<HTMLElement>("#ResearcherMetrics");
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

function showMetrics(researcher?: ResearcherCollectResponse["Researcher"]) {
    const metrics = researcher?.Metrics;

    if (!metricsPanel || !metrics) {
        if (metricsPanel)
            metricsPanel.hidden = true;
        return;
    }

    const displayName = [researcher?.FirstName, researcher?.LastName]
        .filter(Boolean)
        .join(" ") || researcher?.OrcidProfile?.DisplayName || "Akademisyen";
    const values: Record<string, number | string> = {
        MetricsWorksCount: metrics.WorksCount ?? 0,
        MetricsEmploymentsCount: researcher?.OrcidProfile?.EmploymentsCount ?? 0,
        MetricsEducationsCount: researcher?.OrcidProfile?.EducationsCount ?? 0,
        MetricsOrganization: researcher?.OrcidProfile?.CurrentOrganization ?? "—",
        MetricsSource: metrics.Source ?? "ORCID"
    };

    document.querySelector<HTMLElement>("#MetricsResearcherName")!.textContent = displayName;

    for (const [id, value] of Object.entries(values)) {
        const element = document.querySelector<HTMLElement>(`#${id}`);
        if (element)
            element.textContent = typeof value === "number" ? value.toLocaleString("tr-TR") : value;
    }

    metricsPanel.hidden = false;
}

form?.addEventListener("submit", async event => {
    event.preventDefault();

    const identifiers = [valueOf("Orcid")].filter(Boolean);

    if (!identifiers.length) {
        showStatus("error", "ORCID numarasını girin.");
        return;
    }

    setResearchButtonsEnabled(false);
    showMetrics(undefined);
    showStatus("info", "Resmî ORCID kaydı araştırılıyor. Bu işlem biraz sürebilir...");

    try {
        const response = await serviceRequest<ResearcherCollectResponse>(
            "AcademicPerformance/Researcher/Collect",
            { Identifiers: identifiers, UseTestIdentifiers: false });

        const researcherId = response.Researcher?.Id ?? 0;
        const messages = (response.Messages ?? []).filter(Boolean).join("\n");

        if (!response.IsSaved || !researcherId) {
            showStatus("error", messages || "Araştırma tamamlandı ancak kayıt oluşturulamadı.");
            return;
        }

        showStatus("success", messages || "Araştırma tamamlandı.");
        rememberProviderIdentifiers();
        showMetrics(response.Researcher);
        const displayName = [response.Researcher?.FirstName, response.Researcher?.LastName]
            .filter(Boolean)
            .join(" ") || response.Researcher?.OrcidProfile?.DisplayName;
        grid.setResearcher(researcherId, displayName);
    }
    catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        showStatus("error", `Araştırma tamamlanamadı: ${message}`);
    }
    finally {
        setResearchButtonsEnabled(true);
    }
});

myPublicationsButton?.addEventListener("click", () => {
    const filledCount = fillRememberedProviderIdentifiers();

    if (filledCount === 0) {
        showStatus(
            "error",
            "Daha önce başarıyla kullanılan bir sağlayıcı kimliği bulunamadı. " +
            "Önce ORCID numaranızla bir kez araştırma yapın.");
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
