import assert from "node:assert/strict";
import test from "node:test";

const { createDoiUrl } = await import(
    "../../Modules/AcademicPerformance/WebClient/Publications/PublicationDoiUrl.ts");

test("creates canonical encoded doi.org links from supported DOI forms", () => {
    assert.equal(
        createDoiUrl("doi: https://DX.DOI.org/10.1234/Article(First)/Part"),
        "https://doi.org/10.1234/article(first)/part");
    assert.equal(
        createDoiUrl("doi.org/10.5555/S2.0-ABC_(2025)"),
        "https://doi.org/10.5555/s2.0-abc_(2025)");
    assert.equal(
        createDoiUrl("https://doi.org/10.1234/a%3Fb%23c"),
        "https://doi.org/10.1234/a%3Fb%23c");
});

test("rejects missing and invalid DOI values without using another URL", () => {
    assert.equal(createDoiUrl(undefined), null);
    assert.equal(createDoiUrl(""), null);
    assert.equal(createDoiUrl("javascript:alert(1)"), null);
    assert.equal(createDoiUrl("https://example.com/10.1234/article"), null);
    assert.equal(createDoiUrl("10.1234/has whitespace"), null);
    assert.equal(createDoiUrl("https://doi.org/10.1234/%E0%A4%A"), null);
    assert.equal(createDoiUrl("10.1234/\uD800"), null);
});
