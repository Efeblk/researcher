using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

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
