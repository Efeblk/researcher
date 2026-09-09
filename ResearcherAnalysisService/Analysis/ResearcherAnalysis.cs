using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Analysis;

public sealed class ResearcherAnalysis(IResearcherReportGenerator generator)
{
    public async Task<ResearcherAnalysisReport> AnalyzeAsync(
        AnalyzeResearcherRequest request, CancellationToken cancellationToken)
    {
        GeneratedFindings generated = await generator.GenerateAsync(request, cancellationToken);
        ValidateFindings(generated.Findings, request.Publications);

        List<string> limitations = request.Language == "tr"
            ? ["Bulgular yalnızca gönderilen kayıtlara dayanır; kaynak doğruluğu bağımsız olarak doğrulanmadı.",
               "Tam metin analiz edilmedi. Yazım gözlemleri mevcut özetlerle sınırlıdır.",
               "Ortak yazarlı yayınlar tek bir araştırmacının yazım tarzını veya katkısını göstermez."]
            : ["Findings use only submitted records; source accuracy was not independently verified.",
               "Full text was not analyzed. Writing observations cover available abstracts only.",
               "Coauthored publications do not establish one researcher's individual writing style or contribution."];
        bool isPartial = request.TotalPublicationCount > request.Publications.Count;
        if (isPartial)
            limitations.Add(request.Language == "tr"
                ? "Yayınların yalnızca bir bölümü gönderildi; sayılar ve temalar bu örneklemi kapsar."
                : "Only a publication sample was submitted; counts and themes cover that sample.");

        return new ResearcherAnalysisReport
        {
            PersonelId = request.PersonelId,
            GeneratedAt = DateTimeOffset.UtcNow,
            Model = generated.Model,
            PromptVersion = generated.PromptVersion,
            Findings = generated.Findings,
            Activity = new PublicationActivity(
                request.Publications.Count,
                new SortedDictionary<int, int>(request.Publications.Where(publication => publication.Year.HasValue)
                    .GroupBy(publication => publication.Year!.Value).ToDictionary(group => group.Key, group => group.Count())),
                new SortedDictionary<string, int>(request.Publications.GroupBy(publication => publication.Category)
                    .ToDictionary(group => group.Key, group => group.Count()), StringComparer.Ordinal)),
            CitationMetrics = request.CitationMetrics,
            Coverage = new EvidenceCoverage(request.SnapshotAt, request.TotalPublicationCount,
                request.Publications.Count, request.Publications.Count(publication => !string.IsNullOrWhiteSpace(publication.Abstract)),
                request.Publications.Count(publication => !publication.Year.HasValue), isPartial, limitations)
        };
    }

    private static void ValidateFindings(AnalysisFindings findings, List<AnalysisPublication> publications)
    {
        if (findings is null || findings.ResearchFocus is null || findings.WritingObservations is null ||
            findings.ResearchFocus.Count > 6 || findings.WritingObservations.Count > 6)
            throw new InvalidAnalysisException();

        Dictionary<string, AnalysisPublication> byId = publications.ToDictionary(publication => publication.Id, StringComparer.Ordinal);
        ValidateObservations(findings.ResearchFocus, byId, writing: false);
        ValidateObservations(findings.WritingObservations, byId, writing: true);
    }

    private static void ValidateObservations(List<AnalysisObservation> observations,
        Dictionary<string, AnalysisPublication> byId, bool writing)
    {
        foreach (AnalysisObservation observation in observations)
        {
            if (observation is null || string.IsNullOrWhiteSpace(observation.Observation) ||
                observation.Observation.Length > 2000 || observation.Evidence is null ||
                observation.Evidence.Count is < 1 or > 5)
                throw new InvalidAnalysisException(AnalysisFailure.InvalidObservation);

            foreach (PublicationEvidence evidence in observation.Evidence)
            {
                if (evidence is null || string.IsNullOrWhiteSpace(evidence.PublicationId) ||
                    string.IsNullOrWhiteSpace(evidence.Quote) || evidence.Quote.Length is < 10 or > 600 ||
                    evidence.Field is not ("title" or "abstract" or "keywords"))
                    throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
                if (!byId.TryGetValue(evidence.PublicationId, out AnalysisPublication? publication))
                    throw new InvalidAnalysisException(AnalysisFailure.UnknownPublication);
                if (writing && evidence.Field != "abstract")
                    throw new InvalidAnalysisException(AnalysisFailure.WritingEvidenceNotAbstract);

                string? source = evidence.Field switch
                {
                    "title" => publication.Title,
                    "abstract" => publication.Abstract,
                    "keywords" => publication.Keywords,
                    _ => null
                };
                if (source is null || !source.Contains(evidence.Quote, StringComparison.Ordinal))
                    throw new InvalidAnalysisException(AnalysisFailure.QuoteMismatch);
            }
        }
    }
}
