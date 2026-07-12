using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eswmp.Work.IntegrationTests;

/// <summary>
/// Matches the server's actual wire format. HttpClient's ReadFromJsonAsync uses a
/// plain JsonSerializerOptions by default, which knows neither that ASP.NET Core's
/// MVC JsonOptions default to camelCase (Program.cs's AddJsonOptions only adds the
/// enum converter on top of that default, it doesn't change it) nor that the server
/// serializes enums as strings. Without PropertyNamingPolicy set here, deserialization
/// silently leaves non-required properties at their C# default (e.g. Id = Guid.Empty)
/// instead of throwing — so a same-default-value assertion (like comparing two empty
/// Guids) can pass while the actual round trip is broken; it only surfaces loudly on
/// a type with `required` members, whose absence in the mismatched-case JSON is a hard
/// deserialization error rather than a silent default.
/// </summary>
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}
