namespace AcademicCollectorDemo.Modules.AcademicPerformance.Evaluations;

public sealed record EvaluationVerdict(string ClaimId, string Verdict);

public sealed record ConfusionCell(string Expected, string Actual, int Count);

public sealed record CalibrationScore(
    int ScheduledClaims,
    int ScoredClaims,
    int CorrectClaims,
    int MissingClaims,
    int InvalidClaims,
    double? Accuracy,
    double? AbstentionRate,
    double? ErrorRate,
    IReadOnlyList<ConfusionCell> ConfusionMatrix);

public static class ArticleEvaluationScorer
{
    private static readonly string[] Classes = ["supported", "unsupported", "uncertain"];

    public static CalibrationScore Score(
        IReadOnlyList<CalibrationReference> references,
        IReadOnlyList<EvaluationVerdict>? returned)
    {
        Dictionary<string, EvaluationVerdict> actual = (returned ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value.ClaimId))
            .GroupBy(value => value.ClaimId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        Dictionary<(string Expected, string Actual), int> counts = [];
        int correct = 0;
        int missing = 0;
        int invalid = 0;
        int uncertain = 0;
        foreach (CalibrationReference reference in references)
        {
            string value;
            if (!actual.TryGetValue(reference.ClaimId, out EvaluationVerdict? verdict))
            {
                value = "missing";
                missing++;
            }
            else if (!Classes.Contains(verdict.Verdict, StringComparer.Ordinal))
            {
                value = "invalid";
                invalid++;
            }
            else
            {
                value = verdict.Verdict;
                if (value == "uncertain") uncertain++;
                if (value == reference.ExpectedVerdict) correct++;
            }
            counts[(reference.ExpectedVerdict, value)] = counts.GetValueOrDefault((reference.ExpectedVerdict, value)) + 1;
        }
        int total = references.Count;
        return new(total, total - missing - invalid, correct, missing, invalid,
            total == 0 ? null : (double)correct / total,
            total == 0 ? null : (double)uncertain / total,
            total == 0 ? null : (double)(total - correct) / total,
            counts.OrderBy(value => value.Key.Expected, StringComparer.Ordinal)
                .ThenBy(value => value.Key.Actual, StringComparer.Ordinal)
                .Select(value => new ConfusionCell(value.Key.Expected, value.Key.Actual, value.Value)).ToList());
    }
}
