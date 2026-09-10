import { EntityGrid, serviceRequest } from "@serenity-is/corelib";
import type { Column } from "@serenity-is/sleekgrid";
import type { PublicationSummaryRow, PublicationDisplayApprovalResponse } from "../Contracts/AcademicPerformanceContracts";

interface PublicationSelectionCallbacks {
    onCountChanged(count: number): void;
    onControlsEnabled(enabled: boolean): void;
    onError(message: string): void;
}

export class PublicationSummaryGrid extends EntityGrid<PublicationSummaryRow> {
    private researcherReady = false;
    private loadGeneration = 0;
    private approvedPublicationIds = new Set<number>();
    private selectionsLoaded = false;

    constructor(element: string, private readonly callbacks: PublicationSelectionCallbacks) {
        super({ element });
    }

    protected override useAsync() { return true; }
    protected override getIdProperty() { return "Id"; }
    protected override getLocalTextPrefix() { return "AcademicPerformance.PublicationSummary"; }
    protected override getService() { return "AcademicPerformance/PublicationSummary"; }
    protected override getInitialTitle() { return "Yayınlar"; }
    protected override getButtons() { return []; }
    protected override getGridCanLoad() { return this.researcherReady; }

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
                    checkbox.disabled = publicationId <= 0 || !this.selectionsLoaded;
                    checkbox.setAttribute(
                        "aria-label",
                        `${context.item.Title ?? "Yayın"} okulda gösterilsin`);
                    checkbox.addEventListener("change", () => {
                        if (checkbox.checked)
                            this.approvedPublicationIds.add(publicationId);
                        else
                            this.approvedPublicationIds.delete(publicationId);

                        this.callbacks.onCountChanged(this.approvedPublicationIds.size);
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

    async setResearcher(displayName?: string) {
        const generation = ++this.loadGeneration;
        this.researcherReady = true;
        this.selectionsLoaded = false;
        this.approvedPublicationIds.clear();
        this.view.setItems([]);
        this.setTitle(displayName ? `${displayName} - Yayınlar` : "Yayınlar");
        this.callbacks.onControlsEnabled(false);
        this.callbacks.onCountChanged(0);

        try {
            const response = await serviceRequest<PublicationDisplayApprovalResponse>(
                "AcademicPerformance/PublicationDisplayApproval/Get",
                { PublicationSummaryIds: [] });

            if (this.loadGeneration !== generation)
                return;

            this.approvedPublicationIds = new Set(response.PublicationSummaryIds ?? []);
            this.selectionsLoaded = true;
            this.callbacks.onCountChanged(this.approvedPublicationIds.size);
            this.callbacks.onControlsEnabled(true);
        }
        catch (error) {
            if (this.loadGeneration !== generation)
                return;

            const message = error instanceof Error ? error.message : String(error);
            this.callbacks.onError(`Kayıtlı yayın seçimleri okunamadı: ${message}`);
        }
        finally {
            if (this.loadGeneration === generation)
                this.refresh();
        }
    }

    clearResearcher() {
        ++this.loadGeneration;
        this.researcherReady = false;
        this.selectionsLoaded = false;
        this.approvedPublicationIds.clear();
        this.view.setItems([]);
        this.setTitle("Yayınlar");
        this.callbacks.onControlsEnabled(false);
        this.callbacks.onCountChanged(0);
    }

    async saveApprovals() {
        if (!this.researcherReady || !this.selectionsLoaded)
            throw new Error("Önce bir akademisyen araştırın.");

        return serviceRequest<PublicationDisplayApprovalResponse>(
            "AcademicPerformance/PublicationDisplayApproval/Save",
            {
                PublicationSummaryIds: [...this.approvedPublicationIds]
            });
    }

    getApprovedCount() {
        return this.approvedPublicationIds.size;
    }

    canSaveSelections() {
        return this.selectionsLoaded;
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
