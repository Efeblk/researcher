import assert from "node:assert/strict";
import test from "node:test";
import { restoreProviderIdentifiers } from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/ProviderIdentifiers.ts";

test("restoring one identity clears a previous researcher's other identities", () => {
    const inputs = [
        { dataset: { providerIdentifier: "Orcid" }, value: "old-orcid" },
        { dataset: { providerIdentifier: "GoogleScholarId" }, value: "old-scholar" }
    ];
    assert.equal(restoreProviderIdentifiers(inputs, '{"Orcid":" saved-orcid "}'), 1);
    assert.deepEqual(inputs.map(input => input.value), ["saved-orcid", ""]);
});

for (const stored of ["null", "[]", "42", "broken-json", '{"Orcid":123}', '{"Orcid":{}}']) {
    test(`invalid saved identifiers are safely cleared: ${stored}`, () => {
        const input = { dataset: { providerIdentifier: "Orcid" }, value: "stale" };
        assert.equal(restoreProviderIdentifiers([input], stored), 0);
        assert.equal(input.value, "");
    });
}
