using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class SafeArticleFetcherTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("169.254.1.1")]
    [InlineData("172.20.1.1")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.1.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("2002:7f00:1::")]
    public void IsPublic_PrivateOrSpecialAddress_ReturnsFalse(string value) =>
        Assert.False(SafeArticleFetcher.IsPublic(IPAddress.Parse(value)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void IsPublic_GlobalAddress_ReturnsTrue(string value) =>
        Assert.True(SafeArticleFetcher.IsPublic(IPAddress.Parse(value)));

    [Theory]
    [InlineData("http://127.0.0.1/file.pdf")]
    [InlineData("file:///tmp/paper.pdf")]
    [InlineData("https://user:pass@example.org/paper.pdf")]
    public void ValidateUri_UnsafeUrl_Throws(string value) =>
        Assert.Throws<ArticleSourceException>(() => SafeArticleFetcher.ValidateUri(new Uri(value)));
}
