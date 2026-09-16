import assert from "node:assert/strict";
import test from "node:test";

globalThis.document = { querySelector: () => null };
const { formatYoksisCoverage, formatYoksisReason } = await import(
    "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/ResearcherSummaryPanels.ts");

test("formats known and unknown YOKSIS publication coverage", () => {
    assert.equal(formatYoksisCoverage(10, 77), "10 / 77");
    assert.equal(formatYoksisCoverage(10), "10 / toplam bilinmiyor");
});

test("formats a visible YOKSIS category reason with affected publication count", () => {
    assert.equal(
        formatYoksisReason("YÖKSİS ayrıntı kaydı döndürmedi", 3, "yayın"),
        "YÖKSİS ayrıntı kaydı döndürmedi: 3 yayın");
});
