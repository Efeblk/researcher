import assert from "node:assert/strict";
import test from "node:test";
import {
    createResearcherLookupRequest, describeResearcherLookupFailure,
    getTrustedLookupValidationMessage, lookupSavedResearcher
} from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/SavedResearcherLookup.ts";

test("current identifiers take precedence over remembered identifiers", () => {
    const request = createResearcherLookupRequest(
        { personelId: " P-1 ", scopusId: " 57200000001 " },
        { Orcid: "0000-0002-1825-0097", GoogleScholarId: "AbCdEfGhIjKl" });

    assert.deepEqual(request, { PersonelID: "P-1", ScopusID: "57200000001", IncludeActivities: true });
});

test("remembered non-sensitive identifiers are fallback only", () => {
    assert.deepEqual(createResearcherLookupRequest({}, {
        Orcid: " 0000-0002-1825-0097 ", ScopusId: " 57200000001 "
    }), { ORCID: "0000-0002-1825-0097", ScopusID: "57200000001", IncludeActivities: true });
});

test("empty lookup has no request", () => {
    assert.equal(createResearcherLookupRequest({}, {}), undefined);
});

test("saved lookup calls only GetResearcher", async () => {
    const calls = [];
    const expected = { IsSaved: true, Researcher: { PersonelID: "P-1" } };
    const result = await lookupSavedResearcher({ ORCID: "0000-0002-1825-0097" },
        async (action, body) => {
            calls.push({ action, body });
            return expected;
        }, () => true);

    assert.equal(result, expected);
    assert.deepEqual(calls, [{
        action: "AcademicPerformance/V1/GetResearcher",
        body: { ORCID: "0000-0002-1825-0097" }
    }]);
});

test("stale saved lookup cannot publish its response", async () => {
    const result = await lookupSavedResearcher({ PersonelID: "P-1" },
        async () => ({ IsSaved: true, Researcher: { PersonelID: "P-1" } }),
        () => false);

    assert.equal(result, undefined);
});

test("lookup errors expose only trusted validation feedback", () => {
    assert.equal(describeResearcherLookupFailure(
        new Error("Akademisyen kaydı bulunamadı.")),
    "Bu kimlik bilgileriyle kayıtlı bir akademisyen bulunamadı.");
    assert.equal(describeResearcherLookupFailure(
        new Error("ORCID biçimi geçersiz.")),
    "Kimlik bilgisi geçersiz. Lütfen kontrol edip tekrar deneyin.");
    assert.equal(describeResearcherLookupFailure(
        new Error("socket failed with private detail")),
    "Kayıtlı bilgiler alınamadı. Lütfen tekrar deneyin.");
    assert.equal(describeResearcherLookupFailure({
        Error: { Message: "Akademisyen kaydı bulunamadı." }
    }), "Bu kimlik bilgileriyle kayıtlı bir akademisyen bulunamadı.");
});

test("HTTP failures use generic feedback unless a ValidationError was captured", () => {
    const genericHttpError = new Error("Request failed with status 400");
    assert.equal(describeResearcherLookupFailure(genericHttpError),
        "Kayıtlı bilgiler alınamadı. Lütfen tekrar deneyin.");

    const trusted = getTrustedLookupValidationMessage({
        Error: { Code: "ValidationError", Message: "Akademisyen kaydı bulunamadı." }
    });
    assert.equal(describeResearcherLookupFailure(trusted),
        "Bu kimlik bilgileriyle kayıtlı bir akademisyen bulunamadı.");
    assert.equal(getTrustedLookupValidationMessage({
        Error: { Code: "Exception", Message: "private detail" }
    }), undefined);
});
