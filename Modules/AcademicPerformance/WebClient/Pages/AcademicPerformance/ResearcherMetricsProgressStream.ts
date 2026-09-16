import type { ResearcherMetricsResponse } from "../../Contracts/AcademicPerformanceContracts";

export interface ResearcherMetricsProgressEvent {
    Type?: "progress" | "heartbeat" | "result" | "error";
    Stage?: string;
    Message?: string;
    ElapsedSeconds?: number;
    LastProgressElapsedSeconds?: number;
    Result?: ResearcherMetricsResponse;
}

export class ResearcherMetricsStreamError extends Error { }

export async function recalculateMetricsStream(
    personelId: string, signal: AbortSignal | undefined,
    onEvent: (event: ResearcherMetricsProgressEvent) => void,
    fetcher: typeof fetch = globalThis.fetch,
    url = "/Services/AcademicPerformance/V1/RecalculateMetrics") {
    const csrfToken = typeof document === "undefined"
        ? undefined
        : document.cookie.split(";").map(value => value.trim())
            .find(value => value.startsWith("CSRF-TOKEN="))?.slice("CSRF-TOKEN=".length);
    const response = await fetcher(url, {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            "Accept": "application/x-ndjson",
            ...(csrfToken ? { "X-CSRF-TOKEN": decodeURIComponent(csrfToken) } : {})
        },
        body: JSON.stringify({ PersonelID: personelId }), signal
    });
    if (!response.ok || !response.body)
        throw new ResearcherMetricsStreamError("Metrik ilerleme bağlantısı kurulamadı.");

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffered = "";
    let result: ResearcherMetricsResponse | undefined;
    const acceptLine = (line: string) => {
        if (!line.trim()) return;
        const event = JSON.parse(line) as ResearcherMetricsProgressEvent;
        onEvent(event);
        if (event.Type === "error")
            throw new ResearcherMetricsStreamError(
                event.Message || "Metrik güncellemesi sona erdi.");
        if (event.Type === "result") {
            if (!event.Result)
                throw new ResearcherMetricsStreamError("Metrik sonuç iletisi geçersiz.");
            result = event.Result;
        }
    };
    try {
        while (true) {
            const chunk = await reader.read();
            buffered += decoder.decode(chunk.value, { stream: !chunk.done });
            const lines = buffered.split("\n");
            buffered = lines.pop() ?? "";
            for (const line of lines) {
                acceptLine(line);
                if (result) return result;
            }
            if (chunk.done) break;
        }
        acceptLine(buffered);
        if (result) return result;
    }
    finally {
        await reader.cancel().catch(() => undefined);
    }
    throw new ResearcherMetricsStreamError(
        "Metrik ilerleme bağlantısı sonuç alınmadan kapandı.");
}
