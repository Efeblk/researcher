import assert from "node:assert/strict";
import test from "node:test";
import { recalculateMetricsStream, ResearcherMetricsStreamError } from
    "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/ResearcherMetricsProgressStream.ts";

const response = lines => new Response(lines.join("\n") + "\n", {
    headers: { "Content-Type": "application/x-ndjson" }
});

test("metrics stream returns result and preserves heartbeat stall fields", async () => {
    const events = [];
    const result = await recalculateMetricsStream("P-1", undefined, event => events.push(event),
        async () => response([
            JSON.stringify({ Type: "progress", Stage: "waiting-global-gate", ElapsedSeconds: 0 }),
            JSON.stringify({ Type: "heartbeat", Stage: "waiting-global-gate",
                ElapsedSeconds: 5, LastProgressElapsedSeconds: 5 }),
            JSON.stringify({ Type: "result", Stage: "completed", Result: { PersonelID: "P-1" } })
        ]));
    assert.equal(result.PersonelID, "P-1");
    assert.equal(events[1].LastProgressElapsedSeconds, 5);
});

test("metrics stream surfaces terminal safe error without retry", async () => {
    let calls = 0;
    await assert.rejects(() => recalculateMetricsStream("P-1", undefined, () => undefined,
        async () => {
            calls++;
            return response([JSON.stringify({ Type: "error", Stage: "loading",
                Message: "Akademisyen kaydı bulunamadı." })]);
        }), ResearcherMetricsStreamError);
    assert.equal(calls, 1);
});

test("metrics stream rejects a premature EOF", async () => {
    await assert.rejects(() => recalculateMetricsStream("P-1", undefined, () => undefined,
        async () => response([JSON.stringify({ Type: "progress", Stage: "loading" })])),
    /sonuç alınmadan/);
});

test("metrics stream forwards caller cancellation to fetch", async () => {
    const controller = new AbortController();
    controller.abort();
    let receivedSignal;
    await assert.rejects(() => recalculateMetricsStream("P-1", controller.signal, () => undefined,
        async (_url, init) => {
            receivedSignal = init.signal;
            throw new DOMException("cancelled", "AbortError");
        }), /cancelled/);
    assert.equal(receivedSignal, controller.signal);
});
