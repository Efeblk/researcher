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

function findFailedSources(messages: string[]) {
    const sources = [
        "ORCID", "OpenAlex", "Google Scholar", "Web of Science", "Scopus",
        "TR Dizin", "Crossref", "Semantic Scholar"
    ];

    return sources.filter(source => messages.some(message =>
        /^\[(HATA|EKSİK)\]/u.test(message.trim()) &&
        message.toLocaleLowerCase("tr-TR").includes(source.toLocaleLowerCase("tr-TR"))));
}
