namespace ResearcherAnalysisService.Api.V1.Contracts;

public sealed class AnalysisFindings
{
    public required List<AnalysisObservation> ResearchFocus { get; init; }
    public required List<AnalysisObservation> WritingObservations { get; init; }
}

public sealed class AnalysisObservation
{
    public required string Observation { get; init; }
    public required List<PublicationEvidence> Evidence { get; init; }
}

public sealed class PublicationEvidence
{
    public required string PublicationId { get; init; }
    public required string Field { get; init; }
    public required string Quote { get; init; }
}
