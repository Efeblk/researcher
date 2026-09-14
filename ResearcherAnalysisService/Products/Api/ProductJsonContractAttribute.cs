using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ResearcherAnalysisService.Products.Api;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class ProductJsonContractAttribute : Attribute, IResultFilter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult result)
        {
            context.Result = new JsonResult(result.Value, SerializerOptions)
            {
                ContentType = result.ContentTypes.FirstOrDefault(),
                StatusCode = result.StatusCode
            };
        }
        else if (context.Result is JsonResult json)
        {
            json.SerializerSettings = SerializerOptions;
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
