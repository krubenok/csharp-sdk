using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Apps.Elicitation;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(McpAppElicitationCapability))]
[JsonSerializable(typeof(McpAppElicitationMeta))]
[JsonSerializable(typeof(McpUiClientCapabilities))]
internal sealed partial class McpAppElicitationJsonContext : JsonSerializerContext
{
}
