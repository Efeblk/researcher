import { addLocalText, serviceRequest } from "@serenity-is/corelib";
import type { ResearcherCollectResponse, YoksisCollectResponse } from "../../Contracts/AcademicPerformanceContracts";
import { PublicationSummaryGrid } from "../../Publications/PublicationSummaryGrid";
import { restoreProviderIdentifiers } from "./ProviderIdentifiers";
import {
    PublicationRefreshPoller, ResearchRequestCoordinator, updateSelectionTarget
} from "./ResearchRequestCoordinator";
import {
    initializeAcademicMetricsOverview, showAcademicMetricsOverview
} from "./AcademicMetricsOverview";
import {
    showProfileSummary, showWebOfScienceSummary, showProviderComparison,
    showGoogleScholarSummary, showOpenAlexSummary, showProviderCollectionFeedback,
    showYoksisSummary
} from "./ResearcherSummaryPanels";

const providerIdentifierStorageKey = "AcademicPerformance.ProviderIdentifiers.v1";

const form = document.querySelector<HTMLFormElement>("#ResearcherSearchForm");
const researchButton = document.querySelector<HTMLButtonElement>("#ResearchButton");
const myPublicationsButton = document.querySelector<HTMLButtonElement>(
    "#MyPublicationsButton");
const newResearcherButton = document.querySelector<HTMLButtonElement>(
    "#NewResearcherButton");
const researchStatus = document.querySelector<HTMLElement>("#ResearchStatus");
const saveSelectionsButton = document.querySelector<HTMLButtonElement>(
    "#SavePublicationSelections");
const selectionCount = document.querySelector<HTMLElement>("#SelectionCount");
const selectionStatus = document.querySelector<HTMLElement>("#SelectionStatus");
let researchBusy = false;
initializeAcademicMetricsOverview();
addLocalText({
    Controls: {
        Pager: {
            Page: "Sayfa",
            PageStatus: "{total} yayından {from}–{to} arası gösteriliyor",
            NoRowStatus: "Gösterilecek yayın yok",
            LoadingStatus: "Yayınlar yükleniyor...",
            DefaultLoadError: "Yayınlar yüklenemedi."
        },
        QuickSearch: {
            Placeholder: "Yayınlarda ara...",
            Hint: "Yayın başlığında ara",
            FieldSelection: "Arama alanını seç"
        }
    }
});
const grid = new PublicationSummaryGrid("#PublicationGrid", {
    onCountChanged: updateSelectionCount,
    onControlsEnabled: setSelectionControlsEnabled,
    onError: message => showSelectionStatus("error", message),
    onLoading: () => {
        if (!researchBusy && !grid.hasSelectionLoadError())
            showSelectionStatus("info", "Yayınlar güncelleniyor...");
    },
    onPublicationsChanged: showPublicationState
});
const requestCoordinator = new ResearchRequestCoordinator();
const publicationPoller = new PublicationRefreshPoller(
    () => grid.refreshPublications());

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
    let storedValue: string | null = null;
    try {
        storedValue = localStorage.getItem(providerIdentifierStorageKey);
    }
    catch {
        // Storage can be disabled.
    }
    return restoreProviderIdentifiers(getProviderIdentifierInputs(), storedValue);
}

function setResearchButtonsEnabled(enabled: boolean) {
    researchBusy = !enabled;
    for (const input of form?.querySelectorAll<HTMLInputElement>("input") ?? [])
        input.disabled = !enabled;
    if (researchButton)
        researchButton.disabled = !enabled;
    if (myPublicationsButton)
        myPublicationsButton.disabled = !enabled;
}

function showStatus(kind: "info" | "success" | "error", message: string) {
    if (!researchStatus)
        return;

    researchStatus.className = `academic-status visible ${kind}`;
    researchStatus.textContent = message;
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
        saveSelectionsButton.disabled = !enabled || researchBusy;
}

function getErrorMessage(error: unknown) {
    return error instanceof Error ? error.message : String(error);
}

function showPublicationState(count: number) {
    if (grid.hasSelectionLoadError())
        return;

    if (researchBusy) {
        showSelectionStatus(
            "info",
            count > 0
                ? `Araştırma sürüyor. Şu ana kadar ${count.toLocaleString("tr-TR")} yayın bulundu; yeni yayınlar otomatik eklenecek.`
                : "Araştırma sürüyor. Yayınlar bulundukça bu listeye otomatik eklenecek.");
        return;
    }

    showSelectionStatus(
        "info",
        count > 0
            ? grid.canSaveSelections()
                ? `${count.toLocaleString("tr-TR")} yayın listelendi. Okulda gösterilecek yayınları seçebilirsiniz.`
                : `${count.toLocaleString("tr-TR")} yayın listelendi. Kayıtlı seçimler hazırlanıyor...`
            : "Bu akademisyen için henüz yayın bulunamadı. Farklı bir kimlikle yeniden araştırabilirsiniz.");
}

function clearResearcherResults() {
    showProviderCollectionFeedback();
    grid.setResearcher("");
    showProfileSummary(undefined);
    showGoogleScholarSummary(undefined);
    showOpenAlexSummary(undefined);
    showWebOfScienceSummary(undefined);
    showAcademicMetricsOverview(undefined);
    showProviderComparison(undefined);
    showYoksisSummary(undefined);
}

form?.addEventListener("submit", async event => {
    event.preventDefault();
    if (researchBusy)
        return;

    const orcid = valueOf("Orcid");
    const googleScholarId = valueOf("GoogleScholarId");
    const webOfScienceResearcherId = valueOf("WebOfScienceResearcherId");
    const identifiers = [orcid, googleScholarId, webOfScienceResearcherId]
        .filter(Boolean);
    const tcKimlikNo = valueOf("TcKimlikNo");
    const personelId = valueOf("PersonelId");

    if (!personelId) {
        showStatus("error", "PersonelID girin.");
        return;
    }

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

    const run = requestCoordinator.begin();
    setResearchButtonsEnabled(false);

    try {
        clearResearcherResults();
        grid.setResearcher(personelId, personelId);
        publicationPoller.start();
        showStatus(
            "info",
            "Akademik sağlayıcılar araştırılıyor. Yeni bir akademisyene geçmek için “Yeni Akademisyen” düğmesini kullanabilirsiniz.");
        showSelectionStatus(
            "info",
            "Araştırma sürüyor. Yayınlar bulundukça bu listeye otomatik eklenecek.");

        const statusMessages: string[] = [];
        const errors: string[] = [];
        let hasSuccessfulResult = false;
        let linkedPersonelId = "";
        let researcherDisplayName = "";

        if (identifiers.length) {
            try {
                const response = await serviceRequest<ResearcherCollectResponse>(
                    "AcademicPerformance/V1/Collect",
                    {
                        PersonelID: personelId,
                        ORCID: orcid || undefined,
                        ScholarID: googleScholarId || undefined,
                        ResearcherID: webOfScienceResearcherId || undefined
                    },
                    undefined,
                    { blockUI: false, errorMode: "none", signal: run.signal });

                if (!requestCoordinator.isCurrent(run))
                    return;

                const savedPersonelId = response.Researcher?.PersonelID ?? "";
                const messages = (response.Messages ?? []).filter(Boolean).join("\n");
                showProviderCollectionFeedback(response.ProviderFeedback);

                if (response.IsSaved && savedPersonelId) {
                    researcherDisplayName = [
                        response.Researcher?.FirstName,
                        response.Researcher?.LastName
                    ].filter(Boolean).join(" ") ||
                        response.Researcher?.OrcidProfile?.DisplayName ||
                        response.Researcher?.GoogleScholarProfile?.DisplayName ||
                        response.Researcher?.OpenAlexProfile?.DisplayName ||
                        response.Researcher?.WebOfScienceProfile?.DisplayName;

                    linkedPersonelId = updateSelectionTarget(
                        linkedPersonelId, response.IsSaved, savedPersonelId);
                    hasSuccessfulResult = true;
                    statusMessages.push(messages || "Yayın araştırması tamamlandı.");
                    rememberProviderIdentifiers();
                    showProfileSummary(response.Researcher);
                    showGoogleScholarSummary(response.Researcher);
                    showOpenAlexSummary(response.Researcher);
                    showWebOfScienceSummary(response.Researcher);
                    showAcademicMetricsOverview(response.Researcher);
                    showProviderComparison(response.Researcher);

                    if (grid.getResearcherId() !== savedPersonelId)
                        grid.setResearcher(savedPersonelId, researcherDisplayName);
                }
                else {
                    errors.push(
                        messages ||
                        "Yayın araştırması tamamlandı ancak kayıt oluşturulamadı.");
                }
            }
            catch (error) {
                if (!requestCoordinator.isCurrent(run))
                    return;

                errors.push(
                    `Yayın araştırması tamamlanamadı: ${getErrorMessage(error)}`);
            }
        }

        if (tcKimlikNo) {
            try {
                const response = await serviceRequest<YoksisCollectResponse>(
                    "AcademicPerformance/V1/Yoksis/Collect",
                    {
                        PersonelID: linkedPersonelId || personelId,
                        TcKimlikNo: tcKimlikNo,
                        IncludeRecords: false,
                        IncludeRawResponses: false
                    },
                    undefined,
                    { blockUI: false, errorMode: "none", signal: run.signal });

                if (!requestCoordinator.isCurrent(run))
                    return;

                const successfulCount = response.SuccessfulCategoryCount ?? 0;
                const failedCount = response.FailedCategoryCount ?? 0;
                const savedPersonelId = response.PersonelID ?? "";

                showYoksisSummary(response);

                const retrievedPublicationCount = response.PublicationDetailRetrievedCount ?? 0;
                const publicationTotal = response.PublicationDetailTotalCount;
                statusMessages.push(publicationTotal == null
                    ? `YÖKSİS yayın ayrıntısı: ${retrievedPublicationCount.toLocaleString("tr-TR")} alındı; ` +
                        "toplam sayı belirlenemedi. Nedenler YÖKSİS özetinde gösteriliyor."
                    : `YÖKSİS yayın ayrıntısı: ${retrievedPublicationCount.toLocaleString("tr-TR")} / ` +
                        `${publicationTotal.toLocaleString("tr-TR")} alındı.`);

                if (response.IsSaved && savedPersonelId) {
                    linkedPersonelId = updateSelectionTarget(
                        linkedPersonelId, response.IsSaved, savedPersonelId);
                    researcherDisplayName = response.ResearcherDisplayName ?? researcherDisplayName;
                    hasSuccessfulResult = true;
                    statusMessages.push(
                        `YÖKSİS: ${(response.YoksisRecordCount ?? 0)
                            .toLocaleString("tr-TR")} kategori kaydı saklandı, ` +
                        `${(response.YoksisPublicationCount ?? 0)
                            .toLocaleString("tr-TR")} yayın kaydedildi, ` +
                        `${(response.PublicationSummaryCount ?? 0)
                            .toLocaleString("tr-TR")} ortak yayın özeti hazırlandı.`);
                    if (grid.getResearcherId() !== savedPersonelId)
                        grid.setResearcher(savedPersonelId, researcherDisplayName);
                }
                else if (successfulCount > 0) {
                    errors.push(
                        "YÖKSİS verileri alındı ancak akademisyen kaydına yazılamadı.");
                }

                if (failedCount > 0) {
                    errors.push(
                        `YÖKSİS: ${failedCount.toLocaleString("tr-TR")} kategori eksik kaldı; ` +
                        "nedenler YÖKSİS özetinde gösteriliyor.");
                }
            }
            catch (error) {
                if (!requestCoordinator.isCurrent(run))
                    return;

                errors.push(
                    `YÖKSİS sorgusu tamamlanamadı: ${getErrorMessage(error)}`);
            }
        }

        if (!requestCoordinator.isCurrent(run))
            return;

        if (hasSuccessfulResult && linkedPersonelId)
            await grid.loadSelections(linkedPersonelId, researcherDisplayName);

        if (!requestCoordinator.isCurrent(run))
            return;

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
        if (requestCoordinator.isCurrent(run))
            showStatus("error", `Araştırma tamamlanamadı: ${getErrorMessage(error)}`);
    }
    finally {
        if (!requestCoordinator.isCurrent(run))
            return;

        publicationPoller.stop();
        const tcKimlikInput = document.querySelector<HTMLInputElement>("#TcKimlikNo");
        if (tcKimlikInput)
            tcKimlikInput.value = "";
        setResearchButtonsEnabled(true);
        setSelectionControlsEnabled(grid.canSaveSelections());
        requestCoordinator.complete(run);

        try {
            grid.refreshPublications(true);
        }
        catch (error) {
            showSelectionStatus(
                "error",
                `Yayın listesi yenilenemedi: ${getErrorMessage(error)}`);
        }
    }
});

newResearcherButton?.addEventListener("click", () => {
    requestCoordinator.cancel();
    publicationPoller.stop();
    setResearchButtonsEnabled(true);
    form?.reset();
    clearResearcherResults();
    showStatus(
        "info",
        "Yeni akademisyen için PersonelID ve ona ait sağlayıcı kimliklerini girin.");
    showSelectionStatus(
        "info",
        "Bir akademisyen araştırdığınızda bulunan yayınlar burada görünecek.");
    document.querySelector<HTMLInputElement>("#PersonelId")?.focus();
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
    const contextVersion = grid.getContextVersion();
    setSelectionControlsEnabled(false);
    showSelectionStatus("info", "Yayın tercihleri kaydediliyor...");

    try {
        const response = await grid.saveApprovals();
        if (!grid.isContextCurrent(contextVersion))
            return;

        const approvedCount = response.ApprovedCount ?? grid.getApprovedCount();
        updateSelectionCount(approvedCount);
        showSelectionStatus(
            "success",
            `${approvedCount.toLocaleString("tr-TR")} yayın okulda gösterilmek üzere kaydedildi.`);
    }
    catch (error) {
        if (!grid.isContextCurrent(contextVersion))
            return;

        const message = error instanceof Error ? error.message : String(error);
        showSelectionStatus("error", `Yayın tercihleri kaydedilemedi: ${message}`);
    }
    finally {
        if (grid.isContextCurrent(contextVersion))
            setSelectionControlsEnabled(true);
    }
});

showSelectionStatus(
    "info",
    "Bir akademisyen araştırdığınızda bulunan yayınlar burada görünecek.");

document.querySelector(".academic-menu-toggle")?.addEventListener("click", () => {
    document.body.classList.toggle("academic-sidebar-open");
});
