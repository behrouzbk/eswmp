using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eswmp.Core.IntegrationTests;

/// <summary>
/// Matches the server's actual wire format. HttpClient's ReadFromJsonAsync uses a
/// plain JsonSerializerOptions by default, which knows neither that ASP.NET Core's
/// MVC JsonOptions default to camelCase (Program.cs's AddJsonOptions only adds the
/// enum converter on top of that default, it doesn't change it) nor that the server
/// serializes enums as strings. Without PropertyNamingPolicy set here, deserialization
/// silently leaves properties at their C# default (e.g. Id = Guid.Empty) instead of
/// throwing — found via the equivalent Eswmp.Work.IntegrationTests/TestJson.cs bug,
/// where a type with `required` members made the same defect throw instead of silently
/// passing a same-default-value assertion (e.g. two empty Guids compared equal).
/// </summary>
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}
