using AcademicCollectorDemo.Modules.AcademicPerformance.Data;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class CanonicalWorkLockExceptionTests
{
    [Theory]
    [InlineData("write-gate", "ortak yayın kayıt kilidi")]
    [InlineData("researcher:safe-hash", "akademisyen yayın kilidi")]
    [InlineData("identity:safe-hash", "yayın kimliği kilidi")]
    public void Constructor_KnownResource_ReportsSafeScope(string resource, string expectedScope)
    {
        CanonicalWorkLockException exception = new(resource, -1, TimeSpan.FromSeconds(15));

        Assert.Equal(expectedScope, exception.LockScope);
        Assert.Equal(-1, exception.SqlResult);
        Assert.True(exception.IsRetryable);
        Assert.DoesNotContain("safe-hash", exception.Message);
    }

    [Fact]
    public void Constructor_NonTimeoutResult_IsNotRetryable()
    {
        CanonicalWorkLockException exception = new("write-gate", -3, TimeSpan.FromSeconds(15));

        Assert.False(exception.IsRetryable);
    }
}
