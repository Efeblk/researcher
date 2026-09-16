import type { ResearcherCollectResponse } from "../../Contracts/AcademicPerformanceContracts";

export async function recalculateAndReadResearcher(
    personelId: string,
    request: (action: string, body: { PersonelID: string }) => PromiseLike<unknown>,
    isCurrent: () => boolean): Promise<ResearcherCollectResponse | undefined> {
    await request("AcademicPerformance/V1/RecalculateMetrics", { PersonelID: personelId });
    if (!isCurrent())
        return undefined;
    return await request("AcademicPerformance/V1/GetResearcher", {
        PersonelID: personelId
    }) as ResearcherCollectResponse;
}
