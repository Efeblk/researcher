import type { ResearcherCollectResponse } from "../../Contracts/AcademicPerformanceContracts";
import type { ResearchOutcomeKind } from "./YoksisFeedback";

export interface ProviderOutcome {
    kind: ResearchOutcomeKind;
    message: string;
    hasUsableResult: boolean;
}

export function describeProviderOutcome(response: ResearcherCollectResponse): ProviderOutcome {
    const messages = (response.Messages ?? []).filter(Boolean);
    const details = messages.join("\n");
    const saved = response.IsSaved === true && Boolean(response.Researcher?.PersonelID);

    if (!saved) {
        return {
            kind: "error",
            message: details || "Akademik sağlayıcı araştırması tamamlanamadı.",
            hasUsableResult: false
        };
    }

    const incomplete = messages.some(message =>
        /^\[(HATA|EKSİK|UYARI)\]/u.test(message.trim()));
    const summary = incomplete
        ? "Akademik sağlayıcı araştırması kısmen tamamlandı."
        : "Akademik sağlayıcı araştırması başarıyla tamamlandı.";

    return {
        kind: incomplete ? "warning" : "success",
        message: details ? `${summary}\n${details}` : summary,
        hasUsableResult: true
    };
}
