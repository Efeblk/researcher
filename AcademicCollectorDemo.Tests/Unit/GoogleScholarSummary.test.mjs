import assert from "node:assert/strict";
import test from "node:test";

globalThis.document = { querySelector: () => null };
const { mapGoogleScholarMetricTable } = await import(
    "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/ResearcherSummaryPanels.ts");

test("maps all and recent Scholar metrics with the provider year", () => {
    const table = mapGoogleScholarMetricTable({
        CitationCount: 648,
        CitationCountRecent: 521,
        HIndex: 9,
        HIndexRecent: 9,
        I10Index: 9,
        I10IndexRecent: 9,
        MetricsSinceYear: 2021
    });

    assert.equal(table.recentHeader, "2021 yılından bugüne");
    assert.deepEqual(table.values, {
        GoogleScholarCitationCount: "648",
        GoogleScholarCitationCountRecent: "521",
        GoogleScholarHIndex: "9",
        GoogleScholarHIndexRecent: "9",
        GoogleScholarI10Index: "9",
        GoogleScholarI10IndexRecent: "9"
    });
});

test("preserves real zeroes and does not invent a recent year", () => {
    const table = mapGoogleScholarMetricTable({
        CitationCount: 0,
        CitationCountRecent: null,
        HIndex: undefined,
        HIndexRecent: 0,
        I10Index: null,
        I10IndexRecent: 0
    });

    assert.equal(table.recentHeader, "Yakın dönem");
    assert.deepEqual(table.values, {
        GoogleScholarCitationCount: "0",
        GoogleScholarCitationCountRecent: "—",
        GoogleScholarHIndex: "—",
        GoogleScholarHIndexRecent: "0",
        GoogleScholarI10Index: "—",
        GoogleScholarI10IndexRecent: "0"
    });
});
