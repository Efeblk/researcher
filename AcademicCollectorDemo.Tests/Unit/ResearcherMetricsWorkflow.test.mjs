import assert from "node:assert/strict";
import test from "node:test";
import { recalculateAndReadResearcher } from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/ResearcherMetricsWorkflow.ts";

test("successful collection follow-up recalculates before reading refreshed researcher", async () => {
    const actions = [];
    const bodies = [];
    const response = { Researcher: { PersonelID: "P-1" } };
    const result = await recalculateAndReadResearcher("P-1", async (action, body) => {
        actions.push(action);
        bodies.push(body);
        return action.endsWith("GetResearcher") ? response : {};
    }, () => true, () => undefined, async () => ({}));
    assert.deepEqual(actions, [
        "AcademicPerformance/V1/GetResearcher"
    ]);
    assert.deepEqual(bodies, [{ PersonelID: "P-1", IncludeActivities: true }]);
    assert.equal(result, response);
});

test("recalculation failure stops the refreshed read", async () => {
    const actions = [];
    await assert.rejects(() => recalculateAndReadResearcher("P-1", async action => {
        actions.push(action);
        return {};
    }, () => true, () => undefined, async () => { throw new Error("recalculation failed"); }));
    assert.deepEqual(actions, []);
});

test("stale request generation cannot publish a refreshed read", async () => {
    const actions = [];
    const result = await recalculateAndReadResearcher("P-1", async action => {
        actions.push(action);
        return {};
    }, () => false, () => undefined, async () => ({}));
    assert.equal(result, undefined);
    assert.deepEqual(actions, []);
});

test("profile refresh is reported only after streamed recalculation completes", async () => {
    const stages = [];
    await recalculateAndReadResearcher("P-1", async () => ({ Researcher: {} }), () => true,
        event => stages.push(event.Stage), async (_id, _signal, onEvent) => {
            onEvent({ Type: "progress", Stage: "waiting-global-gate" });
            onEvent({ Type: "result", Stage: "completed", Result: {} });
            return {};
        });
    assert.deepEqual(stages, ["waiting-global-gate", "completed", "refreshing"]);
});
