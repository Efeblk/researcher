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
            message: failedCount > 0
                ? `YÖKSİS araştırması başarısız: ${format(failedCount)} kategorinin hiçbiri alınamadı.`
                : "YÖKSİS araştırması tamamlanamadı: sağlayıcıdan başarılı bir kategori sonucu alınamadı.",
            hasUsableResult: false
        };
    }

    if (!saved) {
        return {
            kind: "error",
            message: "YÖKSİS verileri alındı ancak akademisyen kaydına yazılamadı.",
            hasUsableResult: false
        };
    }

    const result = publicationCount > 0
        ? `${format(publicationCount)} yayın kaydedildi.`
        : failedCount > 0
            ? "Başarıyla alınan verilerde yayın kaydı bulunamadı."
            : "Yayın kaydı bulunamadı.";

    if (failedCount > 0) {
        return {
            kind: "warning",
            message: `YÖKSİS araştırması kısmen tamamlandı: ${result} ${format(failedCount)} kategori alınamadı.`,
            hasUsableResult: true
        };
    }

    return {
        kind: "success",
        message: `YÖKSİS araştırması başarıyla tamamlandı: ${result}`,
        hasUsableResult: true
    };
}

function format(value: number) {
    return value.toLocaleString("tr-TR");
}
