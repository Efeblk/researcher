namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public sealed class CanonicalWorkLockException : InvalidOperationException
{
    public int SqlResult { get; }
    public string LockScope { get; }
    public TimeSpan WaitTimeout { get; }
    public bool IsRetryable => SqlResult == -1;

    public CanonicalWorkLockException(string resource, int sqlResult, TimeSpan waitTimeout)
        : base(CreateMessage(GetLockScope(resource), sqlResult, waitTimeout))
    {
        SqlResult = sqlResult;
        LockScope = GetLockScope(resource);
        WaitTimeout = waitTimeout;
    }

    private static string GetLockScope(string resource)
    {
        if (resource == "write-gate")
            return "ortak yayın kayıt kilidi";
        if (resource.StartsWith("researcher:", StringComparison.Ordinal))
            return "akademisyen yayın kilidi";
        if (resource.StartsWith("identity:", StringComparison.Ordinal))
            return "yayın kimliği kilidi";
        return "yayın kayıt kilidi";
    }

    private static string CreateMessage(string scope, int sqlResult, TimeSpan waitTimeout) => sqlResult == -1
        ? $"{scope} {waitTimeout.TotalSeconds:0} saniye içinde alınamadı; başka bir veritabanı işlemi " +
          "kilidi kullanıyor. Lütfen toplamayı yeniden deneyin."
        : $"{scope} alınamadı (SQL sonucu {sqlResult}).";
}
