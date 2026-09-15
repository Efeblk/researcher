import type { YoksisCollectResponse } from "../../Contracts/AcademicPerformanceContracts";

export type ResearchOutcomeKind = "success" | "warning" | "error";

export interface YoksisOutcome {
    kind: ResearchOutcomeKind;
    message: string;
    hasUsableResult: boolean;
}

export function describeYoksisOutcome(response: YoksisCollectResponse): YoksisOutcome {
    const successfulCount = response.SuccessfulCategoryCount ?? 0;
    const failedCount = response.FailedCategoryCount ?? 0;
    const publicationCount = response.YoksisPublicationCount ?? 0;
    const saved = response.IsSaved === true && Boolean(response.PersonelID);

    if (successfulCount === 0) {
        return {
            kind: "error",
            message: response.StopReason || "YÖKSİS verileri alınamadı. Lütfen tekrar deneyin.",
            hasUsableResult: false
        };
    }

    if (!saved) {
        return {
            kind: "error",
            message: "YÖKSİS verileri kaydedilemedi. Lütfen tekrar deneyin.",
            hasUsableResult: false
        };
    }

    if (failedCount > 0) {
        const stopReason = response.StopReason ? ` ${response.StopReason}` : "";
        return {
            kind: "warning",
            message: publicationCount > 0
                ? `YÖKSİS: ${format(publicationCount)} yayın bulundu. Bazı YÖKSİS verileri alınamadı.${stopReason}`
                : `YÖKSİS verilerinin bir kısmı alınamadı; alınan verilerde yayın bulunamadı.${stopReason}`,
            hasUsableResult: true
        };
    }

    return {
        kind: "success",
        message: publicationCount > 0
            ? `YÖKSİS: ${format(publicationCount)} yayın bulundu.`
            : "YÖKSİS araştırması tamamlandı. Yayın bulunamadı.",
        hasUsableResult: true
    };
}

function format(value: number) {
    return value.toLocaleString("tr-TR");
}
