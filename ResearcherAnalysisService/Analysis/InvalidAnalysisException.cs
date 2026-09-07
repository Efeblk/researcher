namespace ResearcherAnalysisService.Analysis;

public sealed class InvalidAnalysisException(AnalysisFailure reason = AnalysisFailure.InvalidReport)
    : Exception(reason switch
    {
        AnalysisFailure.OutputLimit => "The model reached its output token limit before finishing the report. Try a smaller publication sample.",
        AnalysisFailure.IncompleteOutput => "The model did not finish generating the report.",
        AnalysisFailure.InvalidJson => "The model response did not match the required JSON structure.",
        AnalysisFailure.InvalidObservation => "The model returned an observation with missing or out-of-range fields.",
        AnalysisFailure.InvalidEvidence => "The model returned missing or out-of-range evidence fields.",
        AnalysisFailure.UnknownPublication => "The model cited a publication ID that was not supplied.",
        AnalysisFailure.WritingEvidenceNotAbstract => "The model cited a field other than an abstract for a writing observation.",
        AnalysisFailure.QuoteMismatch => "The model's evidence quote was not found verbatim in the cited source field.",
        _ => "The AI provider returned an incomplete or unsupported report."
    })
{
    public AnalysisFailure Reason { get; } = reason;
}
