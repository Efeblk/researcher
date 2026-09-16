using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

public sealed class YoksisOperationResult
{
    public string? CategoryName { get; set; } = null;
    public string? OperationName { get; set; } = null;
    public bool IsSuccess { get; set; }
    public int? ResultCode { get; set; } = null;
    public string? ExternalResultCode { get; set; } = null;
    public string? ResultMessage { get; set; } = null;
    public int RequestCount { get; set; }
    public int RecordCount { get; set; }
    public int? ExpectedDetailCount { get; set; } = null;
    public int RetrievedDetailCount { get; set; }
    public int FailedDetailCount { get; set; }
    public List<YoksisFailureSummary> FailureReasons { get; set; } = [];
    public List<Dictionary<string, string?>> Records { get; set; } = [];
    public List<string> RawResponsesXml { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

public sealed class YoksisFailureSummary
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int AffectedCount { get; set; }
}
