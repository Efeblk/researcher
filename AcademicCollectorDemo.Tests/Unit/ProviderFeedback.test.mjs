import assert from "node:assert/strict";
import test from "node:test";
import { describeProviderOutcome } from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/ProviderFeedback.ts";

test("provider errors cannot produce a green success outcome", () => {
    const outcome = describeProviderOutcome({
        IsSaved: true,
        Researcher: { PersonelID: "P-1" },
        Messages: ["[OK] ORCID verisi alındı.", "[HATA] OpenAlex alınamadı."]
    });

    assert.equal(outcome.kind, "warning");
    assert.equal(outcome.hasUsableResult, true);
    assert.match(outcome.message, /kısmen tamamlandı/);
});

test("a fully successful provider result has an explicit success outcome", () => {
    const outcome = describeProviderOutcome({
        IsSaved: true,
        Researcher: { PersonelID: "P-1" },
        Messages: ["[OK] ORCID verisi alındı."]
    });

    assert.equal(outcome.kind, "success");
    assert.match(outcome.message, /başarıyla tamamlandı/);
});

test("an unsaved provider result is a failure even if messages contain success", () => {
    const outcome = describeProviderOutcome({ IsSaved: false, Messages: ["[OK] Veri alındı."] });

    assert.equal(outcome.kind, "error");
    assert.equal(outcome.hasUsableResult, false);
});
