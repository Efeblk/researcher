namespace ResearcherAnalysisService.Analysis;

public sealed class InvalidAnalysisException : Exception
{
    public InvalidAnalysisException(AnalysisFailure reason = AnalysisFailure.InvalidReport)
        : base(MessageFor(reason))
    {
        Reason = reason;
    }

    public AnalysisFailure Reason { get; }
    public string? Stage { get; private set; } = null;
    public string? Role { get; private set; } = null;

    public void AddContext(string stage, string? role)
    {
        Stage ??= stage;
        Role ??= role;
    }

    public static string CodeFor(AnalysisFailure reason) => reason switch
    {
        AnalysisFailure.OutputLimit => "output_limit",
        AnalysisFailure.IncompleteOutput => "incomplete_output",
        AnalysisFailure.InvalidJson => "invalid_json",
        AnalysisFailure.InvalidObservation => "invalid_observation",
        AnalysisFailure.InvalidEvidence => "invalid_evidence",
        AnalysisFailure.UnknownPublication => "unknown_publication",
        AnalysisFailure.WritingEvidenceNotAbstract => "writing_evidence_not_abstract",
        AnalysisFailure.QuoteMismatch => "quote_mismatch",
        _ => "invalid_report"
    };

    private static string MessageFor(AnalysisFailure reason) => reason switch
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
    };
}
