import type { ResearcherCollectResponse } from "../../Contracts/AcademicPerformanceContracts";

export interface ResearcherLookupRequest {
    PersonelID?: string;
    ORCID?: string;
    ScholarID?: string;
    ResearcherID?: string;
    ScopusID?: string;
    TcKimlikNo?: string;
}

export interface CurrentResearcherIdentifiers {
    personelId?: string;
    orcid?: string;
    googleScholarId?: string;
    webOfScienceResearcherId?: string;
    scopusId?: string;
    tcKimlikNo?: string;
}

export function createResearcherLookupRequest(
    current: CurrentResearcherIdentifiers,
    remembered: Record<string, string> = {}): ResearcherLookupRequest | undefined {
    const currentRequest = compact({
        PersonelID: current.personelId,
        ORCID: current.orcid,
        ScholarID: current.googleScholarId,
        ResearcherID: current.webOfScienceResearcherId,
        ScopusID: current.scopusId,
        TcKimlikNo: current.tcKimlikNo
    });
    if (Object.keys(currentRequest).length > 0)
        return currentRequest;

    const rememberedRequest = compact({
        ORCID: remembered.Orcid,
        ScholarID: remembered.GoogleScholarId,
        ResearcherID: remembered.WebOfScienceResearcherId,
        ScopusID: remembered.ScopusId
    });
    return Object.keys(rememberedRequest).length > 0 ? rememberedRequest : undefined;
}

export async function lookupSavedResearcher(
    request: ResearcherLookupRequest,
    send: (action: string, body: ResearcherLookupRequest) => PromiseLike<ResearcherCollectResponse>,
    isCurrent: () => boolean): Promise<ResearcherCollectResponse | undefined> {
    const response = await send("AcademicPerformance/V1/GetResearcher", request);
    return isCurrent() ? response : undefined;
}

export function describeResearcherLookupFailure(error: unknown) {
    const message = getErrorMessage(error);
    const normalized = message.toLocaleLowerCase("tr-TR");
    if (message === "not-found" || normalized.includes("akademisyen kaydı bulunamadı"))
        return "Bu kimlik bilgileriyle kayıtlı bir akademisyen bulunamadı.";
    if (normalized.includes("birden fazla akademisyen"))
        return "Kimlik bilgileri tek bir akademisyenle eşleşmiyor.";
    if (normalized.includes("biçimi geçersiz") ||
        normalized.includes("karakter olmalıdır") ||
        normalized.includes("geçerli biçimde") ||
        normalized.includes("must contain"))
        return "Kimlik bilgisi geçersiz. Lütfen kontrol edip tekrar deneyin.";
    return "Kayıtlı bilgiler alınamadı. Lütfen tekrar deneyin.";
}

export function getTrustedLookupValidationMessage(
    response?: { Error?: { Code?: string; Message?: string } }) {
    return response?.Error?.Code === "ValidationError"
        ? response.Error.Message?.trim() || undefined
        : undefined;
}

function getErrorMessage(error: unknown) {
    if (error instanceof Error)
        return error.message;
    if (!error || typeof error !== "object")
        return String(error);

    const value = error as {
        message?: unknown;
        Message?: unknown;
        Error?: { Message?: unknown };
    };
    for (const candidate of [value.message, value.Message, value.Error?.Message]) {
        if (typeof candidate === "string")
            return candidate;
    }
    return "";
}

function compact(values: ResearcherLookupRequest) {
    const result: ResearcherLookupRequest = {};
    for (const [key, value] of Object.entries(values)) {
        const normalized = value?.trim();
        if (normalized)
            Object.assign(result, { [key]: normalized });
    }
    return result;
}
