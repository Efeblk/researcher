import assert from "node:assert/strict";
import test from "node:test";
import { mapAcademicCategorySummaries, mapAcademicMetricProviders } from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/AcademicMetricsOverview.ts";

test("maps each metric to its provider without combining counts", () => {
    const providers = mapAcademicMetricProviders({
        OrcidProfile: { WorksCount: 12 },
        GoogleScholarProfile: { DocumentsCount: 9, CitationCount: 80, HIndex: 4 },
        WebOfScienceProfile: { DocumentsCount: 7, WosDocumentsCount: 5,
            WokDocumentsCount: 6, TotalTimesCited: 31, HIndex: 3 },
        OpenAlexProfile: { WorksCount: 10, CitedByCount: 44, HIndex: 5 },
        ScopusProfile: { DocumentsCount: 8, CitationCount: 22, HIndex: 3 }
    }, { IsSaved: true, SuccessfulCategoryCount: 12, YoksisPublicationCount: 6 });

    assert.deepEqual(providers, [
        { provider: "ORCID", publications: 12, citations: undefined, hIndex: undefined },
        { provider: "Google Scholar", publications: 9, citations: 80, hIndex: 4 },
        { provider: "Web of Science (birleşik)", publications: 7, citations: 31, hIndex: 3 },
        { provider: "WOS", publications: 5, citations: undefined, hIndex: undefined },
        { provider: "WOK", publications: 6, citations: undefined, hIndex: undefined },
        { provider: "OpenAlex", publications: 10, citations: 44, hIndex: 5 },
        { provider: "Scopus", publications: 8, citations: 22, hIndex: 3 },
        { provider: "YÖKSİS", publications: 6, citations: undefined, hIndex: undefined }
    ]);
});

test("maps successful base categories without combining detail records", () => {
    const categories = mapAcademicCategorySummaries(undefined, {
        IsSaved: true,
        Categories: [
            { CategoryName: "Projeler", OperationName: "getirProjeListesi", IsSuccess: true, RecordCount: 4 },
            { CategoryName: "Proje ayrıntıları", OperationName: "getirProjeListesiDetay", IsSuccess: true, RecordCount: 3 },
            { CategoryName: "Ödüller", OperationName: "getOdulListesiV1", IsSuccess: false, RecordCount: 0 },
            { CategoryName: "Kitaplar", OperationName: "getKitapBilgisiV1", IsSuccess: true, RecordCount: 2 }
        ]
    });

    assert.equal(categories.find(item => item.category === "Toplam proje")?.value, 4);
    assert.equal(categories.find(item => item.category === "Ödüller")?.value, undefined);
    assert.equal(categories.find(item => item.category === "Kitaplar")?.value, 2);
    assert.equal(categories.some(item => item.category === "Proje ayrıntıları"), false);
});

test("keeps peer-reviewed publication count unavailable without explicit evidence", () => {
    const categories = mapAcademicCategorySummaries({
        OrcidProfile: { PeerReviewsCount: 3 }
    });

    assert.equal(categories.find(item => item.category === "Hakemli yayın")?.value, undefined);
    assert.deepEqual(categories.find(item => item.category === "Hakemlik faaliyeti"), {
        category: "Hakemlik faaliyeti",
        value: 3,
        source: "ORCID",
        note: "Hakemli yayın sayısı değil, ORCID hakemlik grubu sayısıdır."
    });
});

test("maps persisted category summaries and their saved publication count", () => {
    const source = {
        YoksisPublicationCount: 7,
        CategoryMetrics: [
            { CategoryName: "Projeler", OperationName: "getirProjeListesi", RecordCount: 5 }
        ]
    };

    assert.equal(mapAcademicCategorySummaries(undefined, source)
        .find(item => item.category === "Toplam proje")?.value, 5);
    assert.equal(mapAcademicMetricProviders(undefined, source)
        .find(item => item.provider === "YÖKSİS")?.publications, 7);
});

test("rejects category counts from an unsaved live response", () => {
    const categories = mapAcademicCategorySummaries(undefined, {
        IsSaved: false,
        Categories: [
            { CategoryName: "Projeler", OperationName: "getirProjeListesi", IsSuccess: true, RecordCount: 5 }
        ]
    });

    assert.equal(categories.find(item => item.category === "Toplam proje")?.value, undefined);
});

test("rejects live category and publication counts without saved evidence", () => {
    const source = {
        Categories: [
            { CategoryName: "Projeler", OperationName: "getirProjeListesi", IsSuccess: true, RecordCount: 5 }
        ],
        YoksisPublicationCount: 7
    };

    assert.equal(mapAcademicCategorySummaries(undefined, source)
        .find(item => item.category === "Toplam proje")?.value, undefined);
    assert.equal(mapAcademicMetricProviders(undefined, source)
        .find(item => item.provider === "YÖKSİS")?.publications, undefined);
});

test("preserves an explicit zero from a successful saved category", () => {
    const categories = mapAcademicCategorySummaries(undefined, {
        IsSaved: true,
        Categories: [
            { CategoryName: "Projeler", OperationName: "getirProjeListesi", IsSuccess: true, RecordCount: 0 }
        ]
    });

    assert.equal(categories.find(item => item.category === "Toplam proje")?.value, 0);
});

test("maps a stored YOKSIS count without fabricating a collection response", () => {
    const yoksis = mapAcademicMetricProviders(undefined, 3)
        .find(provider => provider.provider === "YÖKSİS");

    assert.deepEqual(yoksis, {
        provider: "YÖKSİS", publications: 3, citations: undefined, hIndex: undefined
    });
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

test("uses the actual YOKSIS publication count without inventing citation metrics", () => {
    const yoksis = mapAcademicMetricProviders(undefined, {
        IsSaved: true, SuccessfulCategoryCount: 12, YoksisPublicationCount: 0
    })
        .find(provider => provider.provider === "YÖKSİS");

    assert.deepEqual(yoksis, {
        provider: "YÖKSİS", publications: 0, citations: undefined, hIndex: undefined
    });
});

test("does not present a failed YOKSIS query as zero publications", () => {
    const yoksis = mapAcademicMetricProviders(undefined, {
        SuccessfulCategoryCount: 0, FailedCategoryCount: 12, YoksisPublicationCount: 0
    }).find(provider => provider.provider === "YÖKSİS");

    assert.equal(yoksis?.publications, undefined);
});

test("does not present an unpersisted YOKSIS query as collected data", () => {
    const yoksis = mapAcademicMetricProviders(undefined, {
        IsSaved: false, SuccessfulCategoryCount: 12, YoksisPublicationCount: 0
    }).find(provider => provider.provider === "YÖKSİS");

    assert.equal(yoksis?.publications, undefined);
});
