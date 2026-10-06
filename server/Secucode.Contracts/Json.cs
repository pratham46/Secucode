using System.Text.Json;
using System.Text.Json.Serialization;

namespace Secucode.Contracts;

// One serializer configuration for the API, its tests and (later) the clients: web casing, enums as strings.
public static class Json
{
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
