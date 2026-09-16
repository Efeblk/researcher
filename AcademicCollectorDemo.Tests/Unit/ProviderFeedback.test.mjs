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
    assert.equal(outcome.message,
        "OpenAlex bilgilerinin bir kısmı alınamadı. Mevcut sonuçları inceleyebilirsiniz.");
    assert.doesNotMatch(outcome.message, /\[HATA\]|\[OK\]/);
});

test("a fully successful provider result has an explicit success outcome", () => {
    const outcome = describeProviderOutcome({
        IsSaved: true,
        Researcher: { PersonelID: "P-1" },
        Messages: ["[OK] ORCID verisi alındı."]
    });

    assert.equal(outcome.kind, "success");
    assert.equal(outcome.message, "Araştırma tamamlandı.");
});

test("an unsaved provider result is a failure even if messages contain success", () => {
    const outcome = describeProviderOutcome({ IsSaved: false, Messages: ["[OK] Veri alındı."] });

    assert.equal(outcome.kind, "error");
    assert.equal(outcome.hasUsableResult, false);
    assert.equal(outcome.message, "Araştırma tamamlanamadı. Lütfen tekrar deneyin.");
});

test("summarizes multiple provider failures without exposing backend logs", () => {
    const outcome = describeProviderOutcome({
        IsSaved: true,
        Researcher: { PersonelID: "P-1" },
        Messages: [
            "[HATA] ORCID API'ye bağlanılamadı: internal detail",
            "[EKSİK] OpenAlex verisi alınamadı.",
            "[OK] Veritabanı: SQL Server kaydı tamamlandı."
        ]
    });

    assert.equal(outcome.message,
        "Bazı bilgiler alınamadı. Mevcut sonuçları inceleyebilirsiniz.");
    assert.doesNotMatch(outcome.message, /internal|SQL|\[/);
});

test("cache messages do not claim that data was freshly collected", () => {
    const outcome = describeProviderOutcome({
        IsSaved: true,
        Researcher: { PersonelID: "P-1" },
        Messages: ["[ÖNBELLEK] ORCID verisi mevcut kayıttan kullanıldı."]
    });

    assert.equal(outcome.message, "Araştırma tamamlandı.");
});
