import assert from "node:assert/strict";
import test from "node:test";
import { recalculateAndReadResearcher } from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/ResearcherMetricsWorkflow.ts";

test("successful collection follow-up recalculates before reading refreshed researcher", async () => {
    const actions = [];
    const response = { Researcher: { PersonelID: "P-1" } };
    const result = await recalculateAndReadResearcher("P-1", async action => {
        actions.push(action);
        return action.endsWith("GetResearcher") ? response : {};
    }, () => true);
    assert.deepEqual(actions, [
        "AcademicPerformance/V1/RecalculateMetrics",
        "AcademicPerformance/V1/GetResearcher"
    ]);
    assert.equal(result, response);
});

test("recalculation failure stops the refreshed read", async () => {
    const actions = [];
    await assert.rejects(() => recalculateAndReadResearcher("P-1", async action => {
        actions.push(action);
        throw new Error("recalculation failed");
    }, () => true));
    assert.deepEqual(actions, ["AcademicPerformance/V1/RecalculateMetrics"]);
});

test("stale request generation cannot publish a refreshed read", async () => {
    const actions = [];
    const result = await recalculateAndReadResearcher("P-1", async action => {
        actions.push(action);
        return {};
    }, () => false);
    assert.equal(result, undefined);
    assert.deepEqual(actions, ["AcademicPerformance/V1/RecalculateMetrics"]);
});
