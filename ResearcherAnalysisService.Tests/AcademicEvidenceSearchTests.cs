using ResearcherAnalysisService.Products.Knowledge;

namespace ResearcherAnalysisService.Tests;

public sealed class AcademicEvidenceSearchTests
{
    [Fact]
    public void TokenizeForRanking_NaturalQuestion_IgnoresFunctionWordsAndRetainsContent()
    {
        string[] tokens = AcademicEvidenceSearchService.TokenizeForRanking(
            "Bu makaledeki graph networks yöntemini ders için nasıl açıklayabilirim?");

        Assert.Contains("graph", tokens);
        Assert.Contains("networks", tokens);
        Assert.DoesNotContain("bu", tokens);
        Assert.DoesNotContain("icin", tokens);
        Assert.True(AcademicEvidenceSearchService.ScoreForRanking(
            "The method uses graph networks for trajectory imputation.",
            AcademicEvidenceSearchService.NormalizeForRanking(
                "Bu makaledeki graph networks yöntemini ders için nasıl açıklayabilirim?"),
            tokens) > 0);
    }

    [Theory]
    [InlineData("ISI", "ısı")]
    [InlineData("İŞI", "işı")]
    [InlineData("YÖNTEM", "yöntem")]
    public void NormalizeForRanking_TurkishIAndDiacritics_UsesOneCanonicalForm(
        string left, string right)
    {
        Assert.Equal(AcademicEvidenceSearchService.NormalizeForRanking(left),
            AcademicEvidenceSearchService.NormalizeForRanking(right));
    }

    [Fact]
    public void ScoreForRanking_UsesWholeTokensAndPhraseBoundaries()
    {
        Assert.Equal(0, AcademicEvidenceSearchService.ScoreForRanking(
            "Automatic verification is complete.", "veri", ["veri"]));
        Assert.True(AcademicEvidenceSearchService.ScoreForRanking(
            "The exact graph network method is reproducible.", "graph network", ["graph", "network"]) >= 100);
    }

    [Fact]
    public void Evaluate_LabelledCases_ReportsRecallMrrAndMissingSourceDenominators()
    {
        var evaluation = AcademicEvidenceSearchService.Evaluate(
        [
            (new[] { "e:wrong", "e:one" }, (IReadOnlyCollection<string>)new[] { "e:one" }, true),
            (new[] { "e:two" }, (IReadOnlyCollection<string>)new[] { "e:two", "e:three" }, true),
            (Array.Empty<string>(), (IReadOnlyCollection<string>)Array.Empty<string>(), false)
        ], 2);

        Assert.Equal(3, evaluation.CaseCount);
        Assert.Equal(2, evaluation.EligibleCaseCount);
        Assert.Equal(1, evaluation.MissingSourceCaseCount);
        Assert.Equal(3, evaluation.RelevantEvidenceCount);
        Assert.Equal(2, evaluation.RetrievedRelevantAtK);
        Assert.Equal(2m / 3m, evaluation.RecallAtK);
        Assert.Equal(0.75m, evaluation.MeanReciprocalRank);
    }

    [Theory]
    [InlineData("Bu makalenin yöntem ve sınırlılıklarını kaynaklarıyla açıkla; kendi çalışmamda hangi koşulları kontrol etmeliyim?")]
    [InlineData("Yöntemlerini, sınırlılıklarını ve koşulları denetle")]
    public void DetectIntentNames_TurkishInflections_ExpandsMethodsScopeConditionsAndFindings(string query)
    {
        string[] intents = AcademicEvidenceSearchService.DetectIntentNames(query);

        Assert.Contains("methods", intents);
        Assert.Contains("limitations", intents);
        Assert.Contains("conditions", intents);
        Assert.Contains("findings", intents);

        string[] terms = AcademicEvidenceSearchService.ExpandedIntentTerms(intents);
        Assert.Contains("method", terms);
        Assert.Contains("limitation", terms);
        Assert.Contains("bounded", terms);
        Assert.Contains("result", terms);
        Assert.Equal(terms, AcademicEvidenceSearchService.ExpandedIntentTerms(intents));
        Assert.Empty(AcademicEvidenceSearchService.DetectIntentNames("verification"));
    }

}
