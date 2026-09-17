import type {
    AcademicCategoryMetric, ResearcherCollectResponse, YoksisCollectResponse,
    YoksisOperationResult
} from "../../Contracts/AcademicPerformanceContracts";

type Researcher = ResearcherCollectResponse["Researcher"];
type SavedMetricSource = {
    YoksisPublicationCount?: number;
    CategoryMetrics?: AcademicCategoryMetric[];
};
type YoksisMetricSource = YoksisCollectResponse | SavedMetricSource | number;

export interface AcademicMetricProvider {
    provider: string;
    publications: number | undefined;
    citations: number | undefined;
    hIndex: number | undefined;
}

export interface AcademicCategorySummary {
    category: string;
    value: number | undefined;
    source: string;
    note?: string;
}

export function mapAcademicMetricProviders(
    researcher?: Researcher,
    yoksis?: YoksisMetricSource): AcademicMetricProvider[] {
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
            provider: "Web of Science (birleşik)",
            publications: definedNumber(researcher?.WebOfScienceProfile?.DocumentsCount),
            citations: definedNumber(researcher?.WebOfScienceProfile?.TotalTimesCited),
            hIndex: definedNumber(researcher?.WebOfScienceProfile?.HIndex)
        },
        {
            provider: "WOS",
            publications: definedNumber(researcher?.WebOfScienceProfile?.WosDocumentsCount),
            citations: undefined,
            hIndex: undefined
        },
        {
            provider: "WOK",
            publications: definedNumber(researcher?.WebOfScienceProfile?.WokDocumentsCount),
            citations: undefined,
            hIndex: undefined
        },
        {
            provider: "OpenAlex",
            publications: definedNumber(researcher?.OpenAlexProfile?.WorksCount),
            citations: definedNumber(researcher?.OpenAlexProfile?.CitedByCount),
            hIndex: definedNumber(researcher?.OpenAlexProfile?.HIndex)
        },
        {
            provider: "Scopus",
            publications: definedNumber(researcher?.ScopusProfile?.DocumentsCount),
            citations: definedNumber(researcher?.ScopusProfile?.CitationCount),
            hIndex: definedNumber(researcher?.ScopusProfile?.HIndex)
        },
        {
            provider: "YÖKSİS",
            publications: getYoksisPublicationCount(yoksis),
            citations: undefined,
            hIndex: undefined
        }
    ];
}

const featuredCategories = [
    { source: "Projeler", label: "Toplam proje" },
    { source: "Ödüller", label: "Ödüller" },
    { source: "Tez danışmanlıkları", label: "Tez danışmanlıkları" },
    { source: "Makaleler", label: "Makaleler" }
];

export function mapAcademicCategorySummaries(
    researcher?: Researcher,
    yoksis?: YoksisMetricSource): AcademicCategorySummary[] {
    const categories = getCategories(yoksis);
    const byName = new Map(categories.filter(category => isSuccessful(category))
        .map(category => [category.CategoryName!, category]));
    const definitions = [...featuredCategories];
    for (const category of categories) {
        if (!definitions.some(item => item.source === category.CategoryName))
            definitions.push({ source: category.CategoryName!, label: category.CategoryName! });
    }

    const summaries: AcademicCategorySummary[] = definitions.map(definition => ({
        category: definition.label,
        value: definedNumber(byName.get(definition.source)?.RecordCount),
        source: "YÖKSİS"
    }));
    summaries.push({
        category: "Hakemli yayın",
        value: undefined,
        source: "Bilgi yok",
        note: "Mevcut kaynaklar yayınların hakem değerlendirmesinden geçtiğini açıkça belirtmiyor."
    });

    const peerReviewGroups = definedNumber(researcher?.OrcidProfile?.PeerReviewsCount);
    if (peerReviewGroups != null) {
        summaries.push({
            category: "Hakemlik faaliyeti",
            value: peerReviewGroups,
            source: "ORCID",
            note: "Hakemli yayın sayısı değil, ORCID hakemlik grubu sayısıdır."
        });
    }
    return summaries;
}

export function showAcademicMetricsOverview(
    researcher?: Researcher,
    yoksis?: YoksisMetricSource,
    root: ParentNode = document) {
    const panel = root.querySelector<HTMLDetailsElement>("#AcademicMetricsOverview");
    const grid = root.querySelector<HTMLElement>("#AcademicMetricsProviderGrid");
    const details = root.querySelector<HTMLElement>("#AcademicMetricsDetails");
    const button = root.querySelector<HTMLButtonElement>("#AcademicMetricsMoreButton");
    const categoryGrid = root.querySelector<HTMLElement>("#AcademicCategoryMetricsGrid");

    grid?.replaceChildren();
    categoryGrid?.replaceChildren();
    if (!panel || !grid || (!researcher && !yoksis)) {
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

    for (const metric of mapAcademicMetricProviders(researcher, yoksis)) {
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

    for (const metric of mapAcademicCategorySummaries(researcher, yoksis)) {
        const card = document.createElement("article");
        const heading = document.createElement("h3");
        const value = document.createElement("strong");
        const source = document.createElement("span");
        heading.textContent = metric.category;
        value.textContent = formatMetric(metric.value);
        source.textContent = metric.note ? `${metric.source} · ${metric.note}` : metric.source;
        card.className = "academic-category-metric";
        card.append(heading, value, source);
        categoryGrid?.append(card);
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

function getYoksisPublicationCount(source?: YoksisMetricSource) {
    if (typeof source === "number")
        return source;
    if (!source)
        return undefined;
    if ("IsSaved" in source && source.IsSaved !== true)
        return undefined;
    if ("Categories" in source && source.IsSaved !== true)
        return undefined;
    if (!("SuccessfulCategoryCount" in source))
        return definedNumber(source.YoksisPublicationCount);
    return source.IsSaved === true && (source.SuccessfulCategoryCount ?? 0) > 0
        ? definedNumber(source.YoksisPublicationCount) : undefined;
}

function getCategories(source?: YoksisMetricSource): Array<YoksisOperationResult | AcademicCategoryMetric> {
    if (!source || typeof source === "number")
        return [];
    if ("IsSaved" in source && source.IsSaved !== true)
        return [];
    if ("Categories" in source && source.IsSaved !== true)
        return [];
    const categories = (source as SavedMetricSource).CategoryMetrics ??
        (source as YoksisCollectResponse).Categories;
    return (categories ?? []).filter(category => Boolean(category.CategoryName) &&
        !category.CategoryName!.toLocaleLowerCase("tr-TR").includes("ayrıntı"));
}

function isSuccessful(category: YoksisOperationResult | AcademicCategoryMetric) {
    return !("IsSuccess" in category) || category.IsSuccess === true;
}
