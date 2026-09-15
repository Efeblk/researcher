import {
    EntityGrid, ListRequest, ListResponse, QuickSearchInput, serviceRequest,
    tryGetWidget
} from "@serenity-is/corelib";
import type { Column } from "@serenity-is/sleekgrid";
import type { PublicationSummaryRow, PublicationDisplayApprovalResponse } from "../Contracts/AcademicPerformanceContracts";
import { LatestRequestGuard } from "./LatestRequestGuard";

interface PublicationSelectionCallbacks {
    onCountChanged(count: number): void;
    onControlsEnabled(enabled: boolean): void;
    onError(message: string): void;
    onLoading(): void;
    onPublicationsChanged(count: number): void;
}

export class PublicationSummaryGrid extends EntityGrid<PublicationSummaryRow> {
    private personelId = "";
    private approvedPublicationIds = new Set<number>();
    private contextVersion = 0;
    private readonly viewRequestGuard = new LatestRequestGuard();
    private selectionLoadError = false;
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
    protected override getGridCanLoad() { return this.personelId.length > 0; }

    protected override createColumns(): Column<PublicationSummaryRow>[] {
        return [
            {
                field: "IsApprovedForDisplay",
                name: "Okulda Göster",
                width: 110,
                sortable: false,
                format: context => {
                    const contextVersion = this.contextVersion;
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
                        if (!this.isContextCurrent(contextVersion))
                            return;

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

    protected override onViewSubmit() {
        if (!super.onViewSubmit())
            return false;

        const request = this.view.params as ListRequest;
        request.EqualityFilter = {
            ...(request.EqualityFilter ?? {}),
            PersonelID: this.personelId
        };
        return true;
    }

    protected override getViewOptions() {
        const options = super.getViewOptions();
        const baseAjaxCall = options.onAjaxCall;

        options.onAjaxCall = (view, requestOptions) => {
            if (baseAjaxCall?.(view, requestOptions) === false)
                return false;

            const contextVersion = this.contextVersion;
            const personelId = this.personelId;
            const requestNumber = this.viewRequestGuard.begin();
            const baseSuccess = requestOptions.onSuccess;
            const baseCleanup = requestOptions.onCleanup;

            requestOptions.blockUI = false;
            requestOptions.errorMode = "none";
            requestOptions.onSuccess = response => {
                if (this.viewRequestGuard.isCurrent(requestNumber) &&
                    this.contextVersion === contextVersion &&
                    this.personelId === personelId)
                    baseSuccess?.(response);
            };
            requestOptions.onError = response => {
                if (this.viewRequestGuard.isCurrent(requestNumber) &&
                    this.contextVersion === contextVersion &&
                    this.personelId === personelId) {
                    this.callbacks.onError(
                        response?.Error?.Message ?? "Yayınlar yüklenemedi. Lütfen yeniden deneyin.");
                }
                return true;
            };
            requestOptions.onCleanup = () => {
                if (this.viewRequestGuard.isCurrent(requestNumber))
                    baseCleanup?.();
            };

            this.callbacks.onLoading();
        };

        return options;
    }

    protected override onViewProcessData(response: ListResponse<PublicationSummaryRow>) {
        const processed = super.onViewProcessData(response);
        this.callbacks.onPublicationsChanged(
            processed.TotalCount ?? processed.Entities?.length ?? 0);
        return processed;
    }

    setResearcher(personelId: string, displayName?: string) {
        this.contextVersion++;
        this.personelId = personelId;
        this.selectionLoadError = false;
        this.selectionsLoaded = false;
        this.approvedPublicationIds.clear();
        this.view.addData({ Entities: [], TotalCount: 0, Skip: 0 });
        this.setTitle(displayName ? `${displayName} - Yayınlar` : "Yayınlar");
        this.callbacks.onControlsEnabled(false);
        this.callbacks.onCountChanged(0);
        this.view.seekToPage = 1;
        delete this.view.params.ContainsText;
        delete this.view.params.ContainsField;

        const quickSearchInput = this.domNode.querySelector<HTMLInputElement>(
            ".s-QuickSearchInput");
        if (quickSearchInput) {
            const quickSearch = tryGetWidget(quickSearchInput, QuickSearchInput);
            quickSearch?.restoreState("", quickSearch.get_field());
        }

        // A direct refresh aborts a request belonging to the previous researcher.
        // The normal polling path avoids aborting a request that is still useful.
        this.refresh();
    }

    async loadSelections(personelId: string, displayName?: string) {
        if (this.personelId !== personelId)
            this.setResearcher(personelId, displayName);
        else if (displayName)
            this.setTitle(`${displayName} - Yayınlar`);

        const contextVersion = this.contextVersion;

        try {
            const response = await serviceRequest<PublicationDisplayApprovalResponse>(
                "AcademicPerformance/PublicationDisplayApproval/Get",
                { PersonelID: personelId, PublicationSummaryIds: [] },
                undefined,
                { blockUI: false, errorMode: "none" });

            if (!this.isContextCurrent(contextVersion) || this.personelId !== personelId)
                return false;

            this.approvedPublicationIds = new Set(response.PublicationSummaryIds ?? []);
            this.selectionLoadError = false;
            this.selectionsLoaded = true;
            this.callbacks.onCountChanged(this.approvedPublicationIds.size);
            this.callbacks.onControlsEnabled(true);
            this.slickGrid.invalidateAllRows();
            this.slickGrid.render();
            return true;
        }
        catch (error) {
            if (!this.isContextCurrent(contextVersion) || this.personelId !== personelId)
                return false;

            this.selectionLoadError = true;
            const message = error instanceof Error ? error.message : String(error);
            this.callbacks.onError(`Kayıtlı yayın seçimleri okunamadı: ${message}`);
            return false;
        }
    }

    refreshPublications(force = false) {
        if (!this.personelId || (!force && this.view.getPagingInfo().loading))
            return;

        this.refresh();
    }

    async saveApprovals() {
        if (!this.personelId || !this.selectionsLoaded)
            throw new Error("Önce bir akademisyen araştırın.");

        return serviceRequest<PublicationDisplayApprovalResponse>(
            "AcademicPerformance/PublicationDisplayApproval/Save",
            {
                PersonelID: this.personelId,
                PublicationSummaryIds: [...this.approvedPublicationIds]
            },
            undefined,
            { blockUI: false, errorMode: "none" });
    }

    getApprovedCount() {
        return this.approvedPublicationIds.size;
    }

    canSaveSelections() {
        return this.selectionsLoaded;
    }

    hasSelectionLoadError() {
        return this.selectionLoadError;
    }

    getResearcherId() {
        return this.personelId;
    }

    getContextVersion() {
        return this.contextVersion;
    }

    isContextCurrent(contextVersion: number) {
        return contextVersion === this.contextVersion;
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
