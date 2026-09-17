using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

public sealed class AcademicWorkCategorizer
{
    public void Categorize(Researcher researcher)
    {
        List<OrcidWork>? orcidWorks = researcher.OrcidProfile?.Works;
        List<WebOfScienceWork>? webOfScienceWorks =
            researcher.WebOfScienceProfile?.Works;
        List<OpenAlexWork>? openAlexWorks = researcher.OpenAlexProfile?.Works;
        List<ScopusWork>? scopusWorks = researcher.ScopusProfile?.Works;
        int index = 0;

        if (orcidWorks is not null)
        {
            for (index = 0; index < orcidWorks.Count; index++)
            {
                orcidWorks[index].Category = GetOrcidCategory(
                    orcidWorks[index].WorkType);
                orcidWorks[index].CategorySource = AcademicWorkCategorySource.Orcid;
            }
        }

        if (webOfScienceWorks is not null)
        {
            for (index = 0; index < webOfScienceWorks.Count; index++)
            {
                webOfScienceWorks[index].Category = GetWebOfScienceCategory(
                    webOfScienceWorks[index].WorkTypes);
                webOfScienceWorks[index].CategorySource =
                    AcademicWorkCategorySource.WebOfScience;
            }
        }

        for (index = 0; index < (openAlexWorks?.Count ?? 0); index++)
        {
            openAlexWorks![index].Category = GetOpenAlexCategory(
                openAlexWorks[index].WorkType);
            openAlexWorks[index].CategorySource = AcademicWorkCategorySource.OpenAlex;
        }

        for (index = 0; index < (scopusWorks?.Count ?? 0); index++)
        {
            scopusWorks![index].Category = GetScopusCategory(scopusWorks[index].WorkType);
            scopusWorks[index].CategorySource = AcademicWorkCategorySource.Scopus;
        }
    }

    public AcademicWorkCategory GetScopusCategory(string? type)
    {
        return AcademicWorkTypeNormalizer.Normalize(type, AcademicWorkCategorySource.Scopus);
    }

    public AcademicWorkCategory GetOpenAlexCategory(string? type)
    {
        return AcademicWorkTypeNormalizer.Normalize(type, AcademicWorkCategorySource.OpenAlex);
    }

    public AcademicWorkCategory GetOrcidCategory(string? type)
    {
        return AcademicWorkTypeNormalizer.Normalize(type, AcademicWorkCategorySource.Orcid);
    }

    public AcademicWorkCategory GetWebOfScienceCategory(string? types)
    {
        return AcademicWorkTypeNormalizer.Normalize(types, AcademicWorkCategorySource.WebOfScience);
    }
}
