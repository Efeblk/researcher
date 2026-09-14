using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;

public interface IPublicationMetricsComputer
{
    Task<PublicationMetricComputation> ComputeAsync(
        string personelId,
        string catalogVersion,
        DateTime computedAt,
        CancellationToken cancellationToken);
}

public sealed record PublicationMetricComputation(
    ResearcherPublicationMetricsResponse Data,
    string ResultJson);

public sealed class PublicationMetricsComputer
    : IPublicationMetricsComputer
{
    private readonly PublicationMetricSourceLoader _sourceLoader;
    private readonly AcademicWorkResearchContextSynchronizer? _researchContextSynchronizer;

    public PublicationMetricsComputer(
        PublicationMetricSourceLoader sourceLoader,
        AcademicWorkResearchContextSynchronizer? researchContextSynchronizer = null)
    {
        _sourceLoader = sourceLoader;
        _researchContextSynchronizer = researchContextSynchronizer;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PublicationMetricComputation> ComputeAsync(
        string personelId,
        string catalogVersion,
        DateTime computedAt,
        CancellationToken cancellationToken)
    {
        if (_researchContextSynchronizer is not null)
            await _researchContextSynchronizer.SynchronizeAsync(personelId, cancellationToken);
        PublicationMetricSource source = await _sourceLoader.LoadAsync(personelId, cancellationToken);
        ResearcherPublicationMetricsResponse data = PublicationMetricsCalculator.Calculate(
            personelId, source.Observations, source.UnmappedAcademicWorkCount, computedAt);
        data.CatalogVersion = catalogVersion;
        data.ProviderMetrics = PublicationProviderMetricsMapper.Map(
            source.ProviderMetrics, computedAt.Year);
        data.ContextualMetrics = PublicationContextualMetricsCalculator.Calculate(
            data.CanonicalWorkCount, source.ContextObservations, data.ValidYearUpperBound);
        data.EligibilityPolicy = PublicationEligibilityPolicyCatalog.Create();
        data.ReferencePopulation = new()
        {
            Status = "Unavailable",
            Reason = "No valid reviewed reference-population manifest is active; internal normalization and provider totals remain excluded from evaluated outputs."
        };
        data.CrossProviderComparability = CrossProviderComparabilityCalculator.Calculate(
            source.Observations, data.ValidYearUpperBound);
        return new(data, JsonSerializer.Serialize(data, JsonOptions));
    }
}
