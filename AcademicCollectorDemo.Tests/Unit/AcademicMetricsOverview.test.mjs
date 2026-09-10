import assert from "node:assert/strict";
import test from "node:test";
import { mapAcademicMetricProviders } from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/AcademicMetricsOverview.ts";

test("maps each metric to its provider without combining counts", () => {
    const providers = mapAcademicMetricProviders({
        OrcidProfile: { WorksCount: 12 },
        GoogleScholarProfile: { DocumentsCount: 9, CitationCount: 80, HIndex: 4 },
        WebOfScienceProfile: { DocumentsCount: 7, TotalTimesCited: 31, HIndex: 3 },
        OpenAlexProfile: { WorksCount: 10, CitedByCount: 44, HIndex: 5 }
    });

    assert.deepEqual(providers, [
        { provider: "ORCID", publications: 12, citations: undefined, hIndex: undefined },
        { provider: "Google Scholar", publications: 9, citations: 80, hIndex: 4 },
        { provider: "Web of Science", publications: 7, citations: 31, hIndex: 3 },
        { provider: "OpenAlex", publications: 10, citations: 44, hIndex: 5 }
    ]);
});

test("preserves zeroes and normalizes unavailable values", () => {
    const providers = mapAcademicMetricProviders({
        OrcidProfile: { WorksCount: null },
        GoogleScholarProfile: { DocumentsCount: 0, CitationCount: 0, HIndex: 0 }
    });
    const scholar = providers.find(provider => provider.provider === "Google Scholar");
    const orcid = providers.find(provider => provider.provider === "ORCID");

    assert.deepEqual(scholar, {
        provider: "Google Scholar", publications: 0, citations: 0, hIndex: 0
    });
    assert.deepEqual(orcid, {
        provider: "ORCID", publications: undefined, citations: undefined, hIndex: undefined
    });
});
