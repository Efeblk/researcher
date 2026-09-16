import assert from "node:assert/strict";
import test from "node:test";

globalThis.document = { querySelector: () => null };
const { formatProviderCoverage, formatProviderStatus } = await import(
    "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/ResearcherSummaryPanels.ts");

test("formats publication coverage without inventing an unknown total", () => {
    assert.equal(formatProviderCoverage({
        Unit: "publication", RetrievedCount: 7, ExpectedCount: null
    }), "7 yayın alındı; toplam bilinmiyor");
});

test("localizes visible provider statuses", () => {
    assert.equal(formatProviderStatus("Partial"), "Kısmi");
    assert.equal(formatProviderStatus("Failed"), "Başarısız");
    assert.equal(formatProviderStatus("Skipped"), "Atlandı");
});

test("labels enrichment coverage as DOI work rather than publications", () => {
    assert.equal(formatProviderCoverage({
        Unit: "DOI", RetrievedCount: 3, ExpectedCount: 5
    }), "3 / 5 DOI işlendi");
});
