import type { AcademicActivity } from "../../Contracts/AcademicPerformanceContracts";

let activities: AcademicActivity[] = [];

export function initializeAcademicActivities() {
    document.querySelector<HTMLSelectElement>("#ActivityCategoryFilter")
        ?.addEventListener("change", render);
    document.querySelector<HTMLSelectElement>("#ActivityProviderFilter")
        ?.addEventListener("change", render);
}

export function showAcademicActivities(values: AcademicActivity[] | undefined) {
    activities = values ?? [];
    populateFilter("#ActivityCategoryFilter", activities.map(value => value.Category));
    populateFilter("#ActivityProviderFilter", activities.map(value => value.Provider));
    const card = document.querySelector<HTMLElement>("#AcademicActivities");
    if (card)
        card.hidden = false;
    render();
}

export function clearAcademicActivities() {
    activities = [];
    const rows = document.querySelector<HTMLElement>("#AcademicActivityRows");
    if (rows)
        rows.replaceChildren();
    const card = document.querySelector<HTMLElement>("#AcademicActivities");
    if (card)
        card.hidden = true;
}

export function filterAcademicActivities(
    values: AcademicActivity[], category: string, provider: string) {
    return values.filter(value =>
        (!category || value.Category === category) &&
        (!provider || value.Provider === provider));
}

export function buildActivityRowsHtml(values: AcademicActivity[]) {
    if (values.length === 0)
        return '<tr><td colspan="6" class="academic-activity-empty">Faaliyet kaydı bulunamadı.</td></tr>';
    return values.map(value => `<tr>
        <td>${escapeHtml(value.Category) || "Diğer faaliyet"}</td>
        <td>${escapeHtml(value.Title) || "—"}</td>
        <td>${escapeHtml(value.Date) || "—"}</td>
        <td>${escapeHtml(value.Organization) || "—"}</td>
        <td>${escapeHtml(value.Role) || "—"}</td>
        <td><strong>${escapeHtml(value.Provider) || "—"}</strong>${value.SourceId
            ? `<small>${escapeHtml(value.SourceId)}</small>` : ""}</td>
    </tr>`).join("");
}

function render() {
    const category = document.querySelector<HTMLSelectElement>("#ActivityCategoryFilter")?.value ?? "";
    const provider = document.querySelector<HTMLSelectElement>("#ActivityProviderFilter")?.value ?? "";
    const rows = document.querySelector<HTMLElement>("#AcademicActivityRows");
    if (rows)
        rows.innerHTML = buildActivityRowsHtml(filterAcademicActivities(activities, category, provider));
}

function populateFilter(selector: string, values: Array<string | undefined>) {
    const select = document.querySelector<HTMLSelectElement>(selector);
    if (!select)
        return;
    const label = select.options[0]?.textContent ?? "Tümü";
    select.replaceChildren(new Option(label, ""));
    for (const value of [...new Set(values.filter(Boolean) as string[])].sort((a, b) =>
        a.localeCompare(b, "tr")))
        select.add(new Option(value, value));
}

function escapeHtml(value: string | null | undefined) {
    return (value ?? "").replace(/[&<>"']/g, character => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    })[character]!);
}
