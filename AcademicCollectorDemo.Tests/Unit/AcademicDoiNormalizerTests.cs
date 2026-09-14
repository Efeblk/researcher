using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class AcademicDoiNormalizerTests
{
    [Theory]
    [InlineData(" DOI: https://DOI.org/10.1234/ABC ", "10.1234/abc")]
    [InlineData("https://doi.org/doi:10.5555/Case", "10.5555/case")]
    public void Normalize_NestedKnownWrappers_ReturnsStableValue(string input, string expected)
    {
        Assert.Equal(expected, AcademicDoiNormalizer.Normalize(input));
        Assert.Equal(expected, AcademicDoiNormalizer.NormalizeValid(input));
    }

    [Theory]
    [InlineData("not-a-doi")]
    [InlineData("10.1234")]
    [InlineData("")]
    public void NormalizeValid_InvalidValue_ReturnsNull(string input) =>
        Assert.Null(AcademicDoiNormalizer.NormalizeValid(input));
}
