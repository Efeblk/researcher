import assert from "node:assert/strict";
import test from "node:test";
import {
    buildActivityRowsHtml, filterAcademicActivities
} from "../../Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/AcademicActivities.ts";

const activities = [
    { Category: "Proje", Provider: "YÖKSİS", Title: "Bir proje" },
    { Category: "Ödül", Provider: "ORCID", Title: "Bir ödül" },
    { Category: "Proje", Provider: "TR Dizin", Title: "Diğer proje" }
];

test("activity filters combine category and provider without changing source rows", () => {
    assert.deepEqual(filterAcademicActivities(activities, "Proje", "TR Dizin"), [activities[2]]);
    assert.equal(filterAcademicActivities(activities, "", "").length, 3);
});

test("activity rows escape saved provider text and show an honest missing title", () => {
    const html = buildActivityRowsHtml([{
        Category: "Ödül", Provider: "ORCID", Title: "<img src=x onerror=alert(1)>",
        SourceId: "A&B"
    }, { Category: "Fonlama", Provider: "ORCID" }]);

    assert.doesNotMatch(html, /<img/);
    assert.match(html, /&lt;img src=x onerror=alert\(1\)&gt;/);
    assert.match(html, /A&amp;B/);
    assert.match(html, /<td>—<\/td>/);
});

test("empty activity result renders a clear table state", () => {
    assert.match(buildActivityRowsHtml([]), /Faaliyet kaydı bulunamadı/);
});
