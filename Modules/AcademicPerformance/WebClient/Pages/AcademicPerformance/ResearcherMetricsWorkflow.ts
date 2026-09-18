import type { ResearcherCollectResponse } from "../../Contracts/AcademicPerformanceContracts";
import type { ResearcherMetricsProgressEvent } from "./ResearcherMetricsProgressStream";

export async function recalculateAndReadResearcher(
    personelId: string,
    request: (action: string, body: { PersonelID: string; IncludeActivities?: boolean }) => PromiseLike<unknown>,
    isCurrent: () => boolean,
    onProgress: (event: ResearcherMetricsProgressEvent) => void,
    recalculate: (personelId: string, signal: AbortSignal | undefined,
        onEvent: (event: ResearcherMetricsProgressEvent) => void) => PromiseLike<unknown>,
    signal?: AbortSignal): Promise<ResearcherCollectResponse | undefined> {
    await recalculate(personelId, signal, onProgress);
    if (!isCurrent())
        return undefined;
    onProgress({ Type: "progress", Stage: "refreshing",
        Message: "Güncellenen akademisyen profili getiriliyor." });
    return await request("AcademicPerformance/V1/GetResearcher", {
        PersonelID: personelId,
        IncludeActivities: true
    }) as ResearcherCollectResponse;
}
