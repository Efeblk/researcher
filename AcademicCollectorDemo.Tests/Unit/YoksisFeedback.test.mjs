import assert from "node:assert/strict";
import test from "node:test";
import { describeYoksisOutcome } from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/YoksisFeedback.ts";

test("reports a successful empty YOKSIS query clearly", () => {
    assert.deepEqual(describeYoksisOutcome({
        IsSaved: true, PersonelID: "P-1", SuccessfulCategoryCount: 12,
        FailedCategoryCount: 0, YoksisPublicationCount: 0
    }), {
        kind: "success",
        message: "YÖKSİS araştırması başarıyla tamamlandı: Yayın kaydı bulunamadı.",
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
    assert.match(outcome.message, /4 yayın kaydedildi/);
    assert.match(outcome.message, /2 kategori alınamadı/);
});

test("does not treat persistence as success when every YOKSIS category fails", () => {
    assert.deepEqual(describeYoksisOutcome({
        IsSaved: true, PersonelID: "P-1", SuccessfulCategoryCount: 0,
        FailedCategoryCount: 12, YoksisPublicationCount: 0
    }), {
        kind: "error",
        message: "YÖKSİS araştırması başarısız: 12 kategorinin hiçbiri alınamadı.",
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
});
