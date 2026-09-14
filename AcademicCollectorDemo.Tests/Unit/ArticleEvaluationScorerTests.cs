using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Evaluations;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ArticleEvaluationScorerTests
{
    [Fact]
    public void Catalog_AllCases_AreMechanicallyValidAndVerdictsBalanced()
    {
        IReadOnlyList<CalibrationCaseDefinition> cases = ArticleEvaluationCalibrationCatalog.Create();

        Assert.Equal("controlled-source-reading-v2", ArticleEvaluationCalibrationCatalog.DatasetVersion);
        Assert.Equal(6, cases.Count);
        Assert.All(cases, value =>
        {
            Assert.Equal(3, value.Snapshot.Claims.Count);
            Assert.Equal(3, value.References.Count);
            List<ArticlePage> pages = [new(null, string.Concat(value.Snapshot.SourceSpans.Select(span => span.Text)))];
            Assert.True(ArticleSourceCatalog.IsValid(pages, value.Snapshot.SourceSpans, "abstract"));
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(pages, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToLowerInvariant();
            Assert.Equal(hash, value.Snapshot.SourceHash);
        });
        Assert.Equal(6, cases.SelectMany(value => value.References).Count(value => value.ExpectedVerdict == "supported"));
        Assert.Equal(6, cases.SelectMany(value => value.References).Count(value => value.ExpectedVerdict == "unsupported"));
        Assert.Equal(6, cases.SelectMany(value => value.References).Count(value => value.ExpectedVerdict == "uncertain"));
        CalibrationCaseDefinition injection = Assert.Single(cases, value => value.CaseId == "en-injection");
        Assert.All(injection.Snapshot.Claims, value => Assert.Equal("data", value.Section));
        Assert.Equal(["supported", "unsupported", "uncertain"],
            injection.References.Select(value => value.ExpectedVerdict));
    }

    [Fact]
    public void Score_MissingAndInvalidPredictions_RemainInDenominator()
    {
        List<CalibrationReference> expected =
        [
            new("a", "supported", "explicit"),
            new("b", "unsupported", "contradicted"),
            new("c", "uncertain", "absent")
        ];

        CalibrationScore score = ArticleEvaluationScorer.Score(expected,
            [new("a", "supported"), new("b", "invented")]);

        Assert.Equal(3, score.ScheduledClaims);
        Assert.Equal(1, score.CorrectClaims);
        Assert.Equal(1, score.MissingClaims);
        Assert.Equal(1, score.InvalidClaims);
        Assert.Equal(1d / 3d, score.Accuracy);
        Assert.Equal(2d / 3d, score.ErrorRate);
        Assert.Contains(score.ConfusionMatrix, value => value.Expected == "uncertain" && value.Actual == "missing");
    }

    [Fact]
    public void Score_EmptyReferenceSet_ReturnsNullRates()
    {
        CalibrationScore score = ArticleEvaluationScorer.Score([], []);

        Assert.Null(score.Accuracy);
        Assert.Null(score.AbstentionRate);
        Assert.Null(score.ErrorRate);
    }
}
