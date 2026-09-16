import { addLocalText, resolveUrl, serviceRequest } from "@serenity-is/corelib";
import type { ResearcherCollectResponse, ResearcherMetricsResponse, YoksisCollectResponse } from "../../Contracts/AcademicPerformanceContracts";
import { PublicationSummaryGrid } from "../../Publications/PublicationSummaryGrid";
import { readRememberedProviderIdentifiers } from "./ProviderIdentifiers";
import { recalculateAndReadResearcher } from "./ResearcherMetricsWorkflow";
import {
    PublicationRefreshPoller, ResearchRequestCoordinator, updateSelectionTarget
} from "./ResearchRequestCoordinator";
import {
    initializeAcademicMetricsOverview, showAcademicMetricsOverview
} from "./AcademicMetricsOverview";
import {
    showProfileSummary, showWebOfScienceSummary, showProviderComparison,
    showGoogleScholarSummary, showOpenAlexSummary, showYoksisSummary
} from "./ResearcherSummaryPanels";
import { describeYoksisOutcome, type ResearchOutcomeKind } from "./YoksisFeedback";
import { describeProviderOutcome } from "./ProviderFeedback";
import { collectYoksisStream, getYoksisConnectionSilenceSeconds,
    YoksisStreamError, type YoksisProgressEvent } from "./YoksisProgressStream";
import {
    createResearcherLookupRequest, describeResearcherLookupFailure,
    getTrustedLookupValidationMessage, lookupSavedResearcher
} from "./SavedResearcherLookup";

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
const yoksisProgress = document.querySelector<HTMLElement>("#YoksisProgress");
const yoksisProgressMessage = document.querySelector<HTMLElement>("#YoksisProgressMessage");
const yoksisProgressElapsed = document.querySelector<HTMLElement>("#YoksisProgressElapsed");
let researchBusy = false;
let lastYoksisProgressMessage = "";
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

function getRememberedProviderIdentifiers() {
    let storedValue: string | null = null;
    try {
        storedValue = localStorage.getItem(providerIdentifierStorageKey);
    }
    catch {
        // Storage can be disabled.
    }
    return readRememberedProviderIdentifiers(storedValue);
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

function showStatus(kind: "info" | ResearchOutcomeKind, message: string) {
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

function showYoksisProgress(event?: YoksisProgressEvent) {
    if (!yoksisProgress || !yoksisProgressMessage || !yoksisProgressElapsed)
        return;
    if (!event) {
        yoksisProgress.hidden = true;
        lastYoksisProgressMessage = "";
        return;
    }
    yoksisProgress.hidden = false;
    const staleSeconds = event.LastProgressElapsedSeconds ?? 0;
    const connectionSilent = event.Stage === "connection-silent";
    const stalled = connectionSilent || (event.Type === "heartbeat" && staleSeconds >= 30);
    yoksisProgress.classList.toggle("stalled", stalled);
    const currentPhase = lastYoksisProgressMessage || "YÖKSİS işlemi sürüyor.";
    if (event.Type !== "heartbeat" && event.Message)
        lastYoksisProgressMessage = event.Message;
    yoksisProgressMessage.textContent = connectionSilent
        ? `${currentPhase} Sunucudan ${Math.round(staleSeconds)} saniyedir durum bilgisi alınamıyor.`
        : stalled
        ? `${currentPhase} Son işlem ilerlemesi ${Math.round(staleSeconds)} saniye önceydi.`
        : event.Type === "heartbeat"
            ? `${currentPhase} Sunucu bağlantısı etkin.`
            : event.Message ?? "YÖKSİS işlemi sürüyor.";
    yoksisProgressElapsed.textContent = event.ElapsedSeconds == null
        ? ""
        : `Geçen süre: ${Math.round(event.ElapsedSeconds)} saniye`;
}

function setSelectionControlsEnabled(enabled: boolean) {
    if (saveSelectionsButton)
        saveSelectionsButton.disabled = !enabled || researchBusy;
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
    const scopusId = valueOf("ScopusId");
    const identifiers = [orcid, googleScholarId, webOfScienceResearcherId, scopusId]
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
            "ORCID, Google Scholar ID, Web of Science ResearcherID, Scopus ID veya " +
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
        showYoksisProgress(undefined);
        grid.setResearcher(personelId, personelId);
        if (identifiers.length)
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
        let latestResearcher: ResearcherCollectResponse["Researcher"];
        let yoksisResponse: YoksisCollectResponse | undefined;

        if (identifiers.length) {
            try {
                const providerNames = [
                    orcid && "ORCID",
                    googleScholarId && "Google Scholar",
                    webOfScienceResearcherId && "Web of Science",
                    scopusId && "Scopus"
                ].filter(Boolean).join(", ");
                showStatus("info", `${providerNames} verileri araştırılıyor...`);
                const response = await serviceRequest<ResearcherCollectResponse>(
                    "AcademicPerformance/V1/Collect",
                    {
                        PersonelID: personelId,
                        ORCID: orcid || undefined,
                        ScholarID: googleScholarId || undefined,
                        ResearcherID: webOfScienceResearcherId || undefined,
                        ScopusID: scopusId || undefined
                    },
                    undefined,
                    { blockUI: false, errorMode: "none", signal: run.signal });

                if (!requestCoordinator.isCurrent(run))
                    return;

                const savedPersonelId = response.Researcher?.PersonelID ?? "";
                const outcome = describeProviderOutcome(response);

                if (outcome.hasUsableResult && savedPersonelId) {
                    latestResearcher = response.Researcher;
                    researcherDisplayName = [
                        response.Researcher?.FirstName,
                        response.Researcher?.LastName
                    ].filter(Boolean).join(" ") ||
                        response.Researcher?.OrcidProfile?.DisplayName ||
                        response.Researcher?.GoogleScholarProfile?.DisplayName ||
                        response.Researcher?.OpenAlexProfile?.DisplayName ||
                        response.Researcher?.ScopusProfile?.DisplayName ||
                        response.Researcher?.WebOfScienceProfile?.DisplayName;

                    linkedPersonelId = updateSelectionTarget(
                        linkedPersonelId, response.IsSaved, savedPersonelId);
                    hasSuccessfulResult = true;
                    if (outcome.kind === "success")
                        statusMessages.push(outcome.message);
                    else
                        errors.push(outcome.message);
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
                    errors.push(outcome.message);
                }
            }
            catch {
                if (!requestCoordinator.isCurrent(run))
                    return;

                errors.push("Akademik veriler alınamadı. Lütfen tekrar deneyin.");
            }
        }

        if (tcKimlikNo) {
            try {
                publicationPoller.stop();
                showStatus("info", "YÖKSİS verileri araştırılıyor...");
                showSelectionStatus("info",
                    "YÖKSİS kayıtları toplama tamamlanınca veritabanına yazılacak.");
                let lastStreamEventAt = Date.now();
                const streamStartedAt = lastStreamEventAt;
                const connectionTimer = globalThis.setInterval(() => {
                    if (!requestCoordinator.isCurrent(run))
                        return;
                    const silentSeconds = getYoksisConnectionSilenceSeconds(
                        lastStreamEventAt, Date.now());
                    if (silentSeconds)
                        showYoksisProgress({
                            Type: "heartbeat",
                            Stage: "connection-silent",
                            ElapsedSeconds: (Date.now() - streamStartedAt) / 1000,
                            LastProgressElapsedSeconds: silentSeconds
                        });
                }, 1000);
                const response = await collectYoksisStream(
                    {
                        PersonelID: linkedPersonelId || personelId,
                        TcKimlikNo: tcKimlikNo,
                        IncludeRecords: false,
                        IncludeRawResponses: false
                    },
                    run.signal,
                    event => {
                        if (!requestCoordinator.isCurrent(run))
                            return;
                        lastStreamEventAt = Date.now();
                        showYoksisProgress(event);
                        if (event.Type === "result" || event.Type === "error")
                            globalThis.clearInterval(connectionTimer);
                    }, globalThis.fetch, resolveUrl("~/Services/AcademicPerformance/V1/Yoksis/CollectStream"))
                    .finally(() => globalThis.clearInterval(connectionTimer));

                if (!requestCoordinator.isCurrent(run))
                    return;

                const savedPersonelId = response.PersonelID ?? "";
                const outcome = describeYoksisOutcome(response);
                yoksisResponse = response;

                showYoksisSummary(response);
                showAcademicMetricsOverview(latestResearcher, response);

                if (outcome.hasUsableResult && savedPersonelId) {
                    linkedPersonelId = updateSelectionTarget(
                        linkedPersonelId, response.IsSaved, savedPersonelId);
                    researcherDisplayName = response.ResearcherDisplayName ?? researcherDisplayName;
                    hasSuccessfulResult = true;
                    if (outcome.kind === "success")
                        statusMessages.push(outcome.message);
                    if (grid.getResearcherId() !== savedPersonelId)
                        grid.setResearcher(savedPersonelId, researcherDisplayName);
                }
                if (outcome.kind === "warning")
                    errors.push(outcome.message);
                else if (outcome.kind === "error")
                    errors.push(outcome.message);
            }
            catch (error) {
                if (!requestCoordinator.isCurrent(run))
                    return;

                const safeMessage = error instanceof YoksisStreamError
                    ? error.message
                    : "YÖKSİS verileri alınamadı. Lütfen tekrar deneyin.";
                showYoksisProgress({ Type: "error", Stage: "failed", Message: safeMessage });
                errors.push(safeMessage);
            }
        }

        if (!requestCoordinator.isCurrent(run))
            return;

        if (hasSuccessfulResult && linkedPersonelId) {
            try {
                showStatus("info", "Sonuçlar hazırlanıyor...");
                const refreshed = await recalculateAndReadResearcher(
                    linkedPersonelId,
                    (action, body) => serviceRequest<ResearcherMetricsResponse | ResearcherCollectResponse>(
                        action, body, undefined,
                        { blockUI: false, errorMode: "none", signal: run.signal }),
                    () => requestCoordinator.isCurrent(run));
                if (!requestCoordinator.isCurrent(run))
                    return;
                showProfileSummary(refreshed.Researcher);
                latestResearcher = refreshed.Researcher;
                showGoogleScholarSummary(refreshed.Researcher);
                showOpenAlexSummary(refreshed.Researcher);
                showWebOfScienceSummary(refreshed.Researcher);
                showAcademicMetricsOverview(latestResearcher, yoksisResponse);
                showProviderComparison(refreshed.Researcher);
            }
            catch {
                if (!requestCoordinator.isCurrent(run))
                    return;
                errors.push(
                    "Akademik metrikler güncellenemedi. Mevcut sonuçları inceleyebilirsiniz.");
            }
        }

        if (hasSuccessfulResult && linkedPersonelId)
            await grid.loadSelections(linkedPersonelId, researcherDisplayName);

        if (!requestCoordinator.isCurrent(run))
            return;

        const combinedMessage = [...statusMessages, ...errors]
            .filter(Boolean)
            .join("\n");

        if (hasSuccessfulResult && errors.length > 0)
            showStatus("warning", combinedMessage);
        else if (hasSuccessfulResult)
            showStatus("success", combinedMessage || "Araştırma tamamlandı.");
        else
            showStatus("error", combinedMessage || "Araştırma tamamlanamadı.");
    }
    catch {
        if (requestCoordinator.isCurrent(run))
            showStatus("error", "Araştırma tamamlanamadı. Lütfen tekrar deneyin.");
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
        catch {
            showSelectionStatus(
                "error",
                "Yayın listesi yenilenemedi. Lütfen tekrar deneyin.");
        }
    }
});

newResearcherButton?.addEventListener("click", () => {
    requestCoordinator.cancel();
    publicationPoller.stop();
    showYoksisProgress(undefined);
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

myPublicationsButton?.addEventListener("click", async () => {
    if (researchBusy)
        return;

    const request = createResearcherLookupRequest({
        personelId: valueOf("PersonelId"),
        orcid: valueOf("Orcid"),
        googleScholarId: valueOf("GoogleScholarId"),
        webOfScienceResearcherId: valueOf("WebOfScienceResearcherId"),
        scopusId: valueOf("ScopusId"),
        tcKimlikNo: valueOf("TcKimlikNo")
    }, getRememberedProviderIdentifiers());

    if (!request) {
        showStatus(
            "error",
            "Kayıtlı yayınları bulmak için en az bir kimlik bilgisi girin.");
        return;
    }

    const run = requestCoordinator.begin();
    publicationPoller.stop();
    showYoksisProgress(undefined);
    setResearchButtonsEnabled(false);
    clearResearcherResults();
    showStatus("info", "Kayıtlı yayınlar aranıyor...");
    showSelectionStatus("info", "Kayıtlı yayınlar aranıyor...");
    let validationMessage: string | undefined;

    try {
        const response = await lookupSavedResearcher(
            request,
            (action, body) => serviceRequest<ResearcherCollectResponse>(
                action, body, undefined,
                {
                    blockUI: false,
                    errorMode: "none",
                    signal: run.signal,
                    onError: response => {
                        validationMessage = getTrustedLookupValidationMessage(response);
                        return true;
                    }
                }),
            () => requestCoordinator.isCurrent(run));
        if (!response || !requestCoordinator.isCurrent(run))
            return;

        const researcher = response.Researcher;
        const savedPersonelId = researcher?.PersonelID ?? "";
        if (!response.IsSaved || !researcher || !savedPersonelId)
            throw new Error("not-found");

        const displayName = [researcher.FirstName, researcher.LastName]
            .filter(Boolean).join(" ") ||
            researcher.OrcidProfile?.DisplayName ||
            researcher.GoogleScholarProfile?.DisplayName ||
            researcher.OpenAlexProfile?.DisplayName ||
            researcher.ScopusProfile?.DisplayName ||
            researcher.WebOfScienceProfile?.DisplayName || savedPersonelId;

        const personelIdInput = document.querySelector<HTMLInputElement>("#PersonelId");
        if (personelIdInput)
            personelIdInput.value = savedPersonelId;

        showProfileSummary(researcher);
        showGoogleScholarSummary(researcher);
        showOpenAlexSummary(researcher);
        showWebOfScienceSummary(researcher);
        showAcademicMetricsOverview(researcher, response.YoksisPublicationCount ?? 0);
        showProviderComparison(researcher);
        grid.setResearcher(savedPersonelId, displayName);
        const selectionsLoaded = await grid.loadSelections(savedPersonelId, displayName);
        if (!requestCoordinator.isCurrent(run))
            return;
        if (!selectionsLoaded) {
            showStatus("warning",
                "Akademisyen bulundu ancak kayıtlı yayın seçimleri yüklenemedi.");
            return;
        }

        showStatus("success", response.PublicationCount
            ? "Kayıtlı yayınlar bulundu."
            : "Akademisyen bulundu; kayıtlı yayını yok.");
        showSelectionStatus("info", response.PublicationCount
            ? `${response.PublicationCount.toLocaleString("tr-TR")} kayıtlı yayın yükleniyor...`
            : "Bu akademisyen için kayıtlı yayın bulunamadı.");
    }
    catch (error) {
        if (!requestCoordinator.isCurrent(run))
            return;
        clearResearcherResults();
        showStatus("error", describeResearcherLookupFailure(validationMessage ?? error));
        showSelectionStatus("error", "Yayınlar yüklenemedi.");
    }
    finally {
        if (!requestCoordinator.isCurrent(run))
            return;
        const tcKimlikInput = document.querySelector<HTMLInputElement>("#TcKimlikNo");
        if (tcKimlikInput)
            tcKimlikInput.value = "";
        setResearchButtonsEnabled(true);
        setSelectionControlsEnabled(grid.canSaveSelections());
        requestCoordinator.complete(run);
        if (grid.getResearcherId())
            grid.refreshPublications(true);
    }
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
