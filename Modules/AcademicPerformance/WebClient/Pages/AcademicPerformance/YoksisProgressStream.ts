import type { YoksisCollectResponse } from "../../Contracts/AcademicPerformanceContracts";

export interface YoksisProgressEvent {
    Type?: "progress" | "heartbeat" | "result" | "error";
    Stage?: string;
    Message?: string;
    CategoryName?: string;
    Current?: number;
    Total?: number;
    RecordCount?: number;
    ElapsedSeconds?: number;
    LastProgressElapsedSeconds?: number;
    Result?: YoksisCollectResponse;
}

export class YoksisStreamError extends Error { }

export function getYoksisConnectionSilenceSeconds(lastEventAt: number, now: number) {
    const seconds = Math.floor((now - lastEventAt) / 1000);
    return seconds >= 12 ? seconds : 0;
}

export async function collectYoksisStream(
    request: Record<string, unknown>, signal: AbortSignal,
    onEvent: (event: YoksisProgressEvent) => void,
    fetcher: typeof fetch = globalThis.fetch,
    url = "/Services/AcademicPerformance/V1/Yoksis/CollectStream") {
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
        body: JSON.stringify(request), signal
    });
    if (!response.ok || !response.body)
        throw new YoksisStreamError("YÖKSİS ilerleme bağlantısı kurulamadı.");

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffered = "";
    let result: YoksisCollectResponse | undefined;
    const acceptLine = (line: string) => {
        if (!line.trim()) return;
        const event = JSON.parse(line) as YoksisProgressEvent;
        onEvent(event);
        if (event.Type === "error")
            throw new YoksisStreamError(event.Message || "YÖKSİS toplaması sona erdi.");
        if (event.Type === "result") {
            if (!event.Result)
                throw new YoksisStreamError("YÖKSİS sonuç iletisi geçerli bir sonuç içermiyor.");
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
    if (!result)
        throw new YoksisStreamError("YÖKSİS ilerleme bağlantısı sonuç alınmadan kapandı.");
    return result;
}
