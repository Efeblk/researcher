using System.Net;
using AcademicCollector.Analysis.Contracts;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;

public sealed class ArticleReviewUnavailableException(string message) : Exception(message);
public sealed class ArticleReviewBusyException : Exception;
public sealed class ArticleReviewSourceChangedException : Exception;
public sealed class ArticleReviewInputTooLargeException : Exception;
public sealed class ArticleReviewAnalysisException(
    HttpStatusCode statusCode,
    string message,
    string errorCode,
    AnalysisFailureDetail? failure,
    ArticleReviewProviderAttempt? providerAttempt = null) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public AnalysisFailureDetail? Failure { get; } = failure;
    public ArticleReviewProviderAttempt? ProviderAttempt { get; } = providerAttempt;
}
