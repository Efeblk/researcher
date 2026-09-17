import assert from "node:assert/strict";
import test from "node:test";
import { describeYoksisOutcome } from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/YoksisFeedback.ts";

test("reports disabled YOKSIS without suggesting a retry", () => {
    assert.deepEqual(describeYoksisOutcome({ IsDisabled: true, IsSaved: false }), {
        kind: "warning",
        message: "YÖKSİS yerel yapılandırmada devre dışı; sorgu yapılmadı.",
        hasUsableResult: false
    });
});

test("reports a successful empty YOKSIS query clearly", () => {
    assert.deepEqual(describeYoksisOutcome({
        IsSaved: true, PersonelID: "P-1", SuccessfulCategoryCount: 12,
        FailedCategoryCount: 0, YoksisPublicationCount: 0
    }), {
        kind: "success",
        message: "YÖKSİS araştırması tamamlandı. Yayın bulunamadı.",
        hasUsableResult: true
    });
});

test("reports a cached YOKSIS result clearly", () => {
    assert.deepEqual(describeYoksisOutcome({
        IsSaved: true, IsCached: true, PersonelID: "P-1",
        SuccessfulCategoryCount: 12, FailedCategoryCount: 0,
        YoksisPublicationCount: 3
    }), {
        kind: "success",
        message: "YÖKSİS: 3 yayın önbellekten yüklendi.",
        hasUsableResult: true
    });
});

test("reports partial YOKSIS category failures as a warning", () => {
    const outcome = describeYoksisOutcome({
        IsSaved: true, PersonelID: "P-1", SuccessfulCategoryCount: 10,
        FailedCategoryCount: 2, YoksisPublicationCount: 4
    });

    assert.equal(outcome.kind, "warning");
    assert.equal(outcome.hasUsableResult, true);
    assert.equal(outcome.message,
        "YÖKSİS: 4 yayın bulundu. Bazı YÖKSİS verileri alınamadı.");
});

test("does not treat persistence as success when every YOKSIS category fails", () => {
    assert.deepEqual(describeYoksisOutcome({
        IsSaved: true, PersonelID: "P-1", SuccessfulCategoryCount: 0,
        FailedCategoryCount: 12, YoksisPublicationCount: 0
    }), {
        kind: "error",
        message: "YÖKSİS verileri alınamadı. Lütfen tekrar deneyin.",
        hasUsableResult: false
    });
});

test("reports successful provider data that could not be persisted as failure", () => {
    const outcome = describeYoksisOutcome({
        IsSaved: false, SuccessfulCategoryCount: 12,
        FailedCategoryCount: 0, YoksisPublicationCount: 3
    });

    assert.equal(outcome.kind, "error");
    assert.equal(outcome.hasUsableResult, false);
    assert.equal(outcome.message, "YÖKSİS verileri kaydedilemedi. Lütfen tekrar deneyin.");
});

test("describes a partial empty YOKSIS result without claiming no publications exist", () => {
    const outcome = describeYoksisOutcome({
        IsSaved: true, PersonelID: "P-1", SuccessfulCategoryCount: 10,
        FailedCategoryCount: 2, YoksisPublicationCount: 0
    });

    assert.equal(outcome.kind, "warning");
    assert.equal(outcome.message,
        "YÖKSİS verilerinin bir kısmı alınamadı; alınan verilerde yayın bulunamadı.");
});
