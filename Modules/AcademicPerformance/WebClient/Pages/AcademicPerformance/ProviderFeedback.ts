import type { ResearcherCollectResponse } from "../../Contracts/AcademicPerformanceContracts";
import type { ResearchOutcomeKind } from "./YoksisFeedback";

export interface ProviderOutcome {
    kind: ResearchOutcomeKind;
    message: string;
    hasUsableResult: boolean;
}

export function describeProviderOutcome(response: ResearcherCollectResponse): ProviderOutcome {
    const messages = (response.Messages ?? []).filter(Boolean);
    const saved = response.IsSaved === true && Boolean(response.Researcher?.PersonelID);
    const incomplete = messages.some(message =>
        /^\[(HATA|EKSİK|UYARI)\]/u.test(message.trim()));
    const failedSources = findFailedSources(messages);

    if (!saved) {
        return {
            kind: "error",
            message: failedSources.length === 1
                ? `${failedSources[0]} verileri alınamadı. Lütfen tekrar deneyin.`
                : "Araştırma tamamlanamadı. Lütfen tekrar deneyin.",
            hasUsableResult: false
        };
    }

    return {
        kind: incomplete ? "warning" : "success",
        message: incomplete
            ? failedSources.length === 1
                ? `${failedSources[0]} bilgilerinin bir kısmı alınamadı. Mevcut sonuçları inceleyebilirsiniz.`
                : "Bazı bilgiler alınamadı. Mevcut sonuçları inceleyebilirsiniz."
            : "Araştırma tamamlandı.",
        hasUsableResult: true
    };
}

export function getTrustedProviderValidationMessage(error: unknown): string | null {
    if (!error || typeof error !== "object")
        return null;

    const serviceError = (error as {
        kind?: unknown;
        response?: { Error?: { Code?: unknown; Message?: unknown } };
    });
    const code = serviceError.response?.Error?.Code;
    const message = serviceError.response?.Error?.Message;

    if (serviceError.kind !== "service-error" || code !== "ValidationError" ||
        typeof message !== "string")
        return null;

    const normalized = message.replace(/\s+/gu, " ").trim();
    return normalized.length > 0 && normalized.length <= 500 ? normalized : null;
}

function findFailedSources(messages: string[]) {
    const sources = [
        "ORCID", "OpenAlex", "Google Scholar", "Web of Science", "Scopus",
        "TR Dizin", "Crossref", "Semantic Scholar"
    ];

    return sources.filter(source => messages.some(message =>
        /^\[(HATA|EKSİK)\]/u.test(message.trim()) &&
        message.toLocaleLowerCase("tr-TR").includes(source.toLocaleLowerCase("tr-TR"))));
}
