using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

public sealed class YoksisCollectRequest : ServiceRequest
{
    public int? ResearcherId { get; set; } = null;
    public string? TcKimlikNo { get; set; } = null;
    public DateTime? UpdatedAfter { get; set; } = null;
    public bool IncludeRecords { get; set; } = true;
    public bool IncludeRawResponses { get; set; } = true;
}

public sealed class YoksisCollectResponse : ServiceResponse
{
    public int? ResearcherId { get; set; } = null;
    public string? ResearcherDisplayName { get; set; } = null;
    public bool IsSaved { get; set; }
    public int YoksisRecordCount { get; set; }
    public int YoksisPublicationCount { get; set; }
    public int PublicationSummaryCount { get; set; }
    public DateTime CollectedAt { get; set; }
    public int SuccessfulCategoryCount { get; set; }
    public int FailedCategoryCount { get; set; }
    public int TotalRecordCount { get; set; }
    public List<string> Messages { get; set; } = [];
    public List<YoksisOperationResult> Categories { get; set; } = [];
}

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
    public List<Dictionary<string, string?>> Records { get; set; } = [];
    public List<string> RawResponsesXml { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

internal sealed class YoksisOperationDefinition
{
    public string CategoryName { get; }
    public string OperationName { get; }
    public string RequestElementName { get; }
    public string? DetailCategoryName { get; }
    public string? DetailOperationName { get; }
    public string? DetailRequestElementName { get; }
    public string? DetailIdentifierFieldName { get; }

    public YoksisOperationDefinition(
        string categoryName,
        string operationName,
        string requestElementName,
        string? detailCategoryName = null,
        string? detailOperationName = null,
        string? detailRequestElementName = null,
        string? detailIdentifierFieldName = null)
    {
        CategoryName = categoryName;
        OperationName = operationName;
        RequestElementName = requestElementName;
        DetailCategoryName = detailCategoryName;
        DetailOperationName = detailOperationName;
        DetailRequestElementName = detailRequestElementName;
        DetailIdentifierFieldName = detailIdentifierFieldName;
    }
}
