using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ResearcherAnalysisService.Products.Api;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class ProductJsonContractAttribute : Attribute, IResultFilter
{
    private const long MaximumSafeInteger = 9_007_199_254_740_992L;
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    internal static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString |
                JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        options.Converters.Add(new SafeInt64JsonConverter());
        return options;
    }

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

    private sealed class SafeInt64JsonConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt64(),
            JsonTokenType.String when long.TryParse(reader.GetString(), out long value) => value,
            _ => throw new JsonException("Expected a 64-bit integer or its decimal string representation.")
        };

        public override void Write(Utf8JsonWriter writer, long value,
            JsonSerializerOptions options)
        {
            if (value is >= -MaximumSafeInteger and <= MaximumSafeInteger)
                writer.WriteNumberValue(value);
            else
                writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
