import { addLocalText, serviceRequest } from "@serenity-is/corelib";
import type { ResearcherCollectResponse, YoksisCollectResponse } from "../../Contracts/AcademicPerformanceContracts";
import { PublicationSummaryGrid } from "../../Publications/PublicationSummaryGrid";
import { restoreProviderIdentifiers } from "./ProviderIdentifiers";
import {
    showProfileSummary, showWebOfScienceSummary, showProviderComparison,
    showGoogleScholarSummary, showOpenAlexSummary, showYoksisSummary
} from "./ResearcherSummaryPanels";

const providerIdentifierStorageKey = "AcademicPerformance.ProviderIdentifiers.v1";

const form = document.querySelector<HTMLFormElement>("#ResearcherSearchForm");
const researchButton = document.querySelector<HTMLButtonElement>("#ResearchButton");
const myPublicationsButton = document.querySelector<HTMLButtonElement>(
    "#MyPublicationsButton");
const researchStatus = document.querySelector<HTMLElement>("#ResearchStatus");
const saveSelectionsButton = document.querySelector<HTMLButtonElement>(
    "#SavePublicationSelections");
const selectionCount = document.querySelector<HTMLElement>("#SelectionCount");
const selectionStatus = document.querySelector<HTMLElement>("#SelectionStatus");
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
    onError: message => showSelectionStatus("error", message)
});

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

let researchBusy = false;

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

form?.addEventListener("submit", async event => {
    event.preventDefault();
    if (researchBusy)
        return;

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
    await grid.setResearcher(0);
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
        setSelectionControlsEnabled(grid.canSaveSelections());
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
