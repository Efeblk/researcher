import type { ResearcherCollectResponse } from "../../Contracts/AcademicPerformanceContracts";

type Researcher = ResearcherCollectResponse["Researcher"];

export interface AcademicMetricProvider {
    provider: string;
    publications: number | undefined;
    citations: number | undefined;
    hIndex: number | undefined;
}

export function mapAcademicMetricProviders(
    researcher?: Researcher): AcademicMetricProvider[] {
    return [
        {
            provider: "ORCID",
            publications: definedNumber(researcher?.OrcidProfile?.WorksCount),
            citations: undefined,
            hIndex: undefined
        },
        {
            provider: "Google Scholar",
            publications: definedNumber(researcher?.GoogleScholarProfile?.DocumentsCount),
            citations: definedNumber(researcher?.GoogleScholarProfile?.CitationCount),
            hIndex: definedNumber(researcher?.GoogleScholarProfile?.HIndex)
        },
        {
            provider: "Web of Science",
            publications: definedNumber(researcher?.WebOfScienceProfile?.DocumentsCount),
            citations: definedNumber(researcher?.WebOfScienceProfile?.TotalTimesCited),
            hIndex: definedNumber(researcher?.WebOfScienceProfile?.HIndex)
        },
        {
            provider: "OpenAlex",
            publications: definedNumber(researcher?.OpenAlexProfile?.WorksCount),
            citations: definedNumber(researcher?.OpenAlexProfile?.CitedByCount),
            hIndex: definedNumber(researcher?.OpenAlexProfile?.HIndex)
        }
    ];
}

export function showAcademicMetricsOverview(
    researcher?: Researcher,
    root: ParentNode = document) {
    const panel = root.querySelector<HTMLDetailsElement>("#AcademicMetricsOverview");
    const grid = root.querySelector<HTMLElement>("#AcademicMetricsProviderGrid");
    const details = root.querySelector<HTMLElement>("#AcademicMetricsDetails");
    const button = root.querySelector<HTMLButtonElement>("#AcademicMetricsMoreButton");

    grid?.replaceChildren();
    if (!panel || !grid || !researcher) {
        if (panel)
            panel.hidden = true;
        if (details)
            details.hidden = true;
        if (button) {
            button.setAttribute("aria-expanded", "false");
            button.textContent = "Daha fazla metrik";
        }
        return;
    }

    for (const metric of mapAcademicMetricProviders(researcher)) {
        const card = document.createElement("article");
        const provider = document.createElement("h3");
        const publicationLabel = document.createElement("span");
        const publicationValue = document.createElement("strong");
        const secondary = document.createElement("dl");

        card.className = "academic-provider-metric";
        provider.textContent = metric.provider;
        publicationLabel.className = "academic-provider-metric-label";
        publicationLabel.textContent = "Yayın";
        publicationValue.textContent = formatMetric(metric.publications);
        secondary.append(
            createMetricDefinition("Atıf", metric.citations),
            createMetricDefinition("h-index", metric.hIndex));
        card.append(provider, publicationLabel, publicationValue, secondary);
        grid.append(card);
    }

    if (details)
        details.hidden = true;
    if (button) {
        button.setAttribute("aria-expanded", "false");
        button.textContent = "Daha fazla metrik";
    }
    panel.open = true;
    panel.hidden = false;
}

export function initializeAcademicMetricsOverview(root: ParentNode = document) {
    const button = root.querySelector<HTMLButtonElement>("#AcademicMetricsMoreButton");
    const details = root.querySelector<HTMLElement>("#AcademicMetricsDetails");
    if (!button || !details)
        return;

    button.addEventListener("click", () => {
        const expanded = button.getAttribute("aria-expanded") === "true";
        button.setAttribute("aria-expanded", String(!expanded));
        button.textContent = expanded ? "Daha fazla metrik" : "Daha az metrik";
        details.hidden = expanded;
    });
}

function createMetricDefinition(label: string, value: number | undefined) {
    const group = document.createElement("div");
    const term = document.createElement("dt");
    const description = document.createElement("dd");
    term.textContent = label;
    description.textContent = formatMetric(value);
    group.append(term, description);
    return group;
}

function formatMetric(value: number | undefined) {
    return value == null ? "—" : value.toLocaleString("tr-TR");
}

function definedNumber(value: number | null | undefined) {
    return value == null ? undefined : value;
}
