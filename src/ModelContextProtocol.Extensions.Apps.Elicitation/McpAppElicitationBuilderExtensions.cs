using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Apps.Elicitation;

/// <summary>Builder extensions for app-rendered elicitation.</summary>
[Experimental(
    McpAppElicitationDiagnostics.DiagnosticId,
    UrlFormat = McpAppElicitationDiagnostics.Url)]
public static class McpAppElicitationBuilderExtensions
{
    /// <summary>Enables app-rendered elicitation as a capability of the MCP Apps extension.</summary>
    public static IMcpServerBuilder WithMcpAppElicitation(this IMcpServerBuilder builder)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
#endif
        builder.WithMcpApps();
        builder.Services.AddSingleton<IPostConfigureOptions<McpServerOptions>, PostConfigureOptions>();
        return builder;
    }

    private sealed class PostConfigureOptions : IPostConfigureOptions<McpServerOptions>
    {
        public void PostConfigure(string? name, McpServerOptions options)
        {
            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Extensions ??= new Dictionary<string, object>();
            var appsCapability = options.Capabilities.Extensions.TryGetValue(McpApps.ExtensionId, out var existing)
                ? ToCapabilityObject(existing)
                : new JsonObject();
            if (appsCapability[McpAppElicitation.NestedCapabilityName] is not JsonObject)
            {
                appsCapability[McpAppElicitation.NestedCapabilityName] = new JsonObject();
            }
            options.Capabilities.Extensions[McpApps.ExtensionId] = appsCapability;

            var gateCapability = options.Capabilities.Extensions.TryGetValue(
                McpAppElicitation.ExtensionId,
                out var existingGate)
                ? ToCapabilityObject(existingGate)
                : new JsonObject();
            if (gateCapability["requires"] is not JsonArray requires)
            {
                requires = new JsonArray();
                gateCapability["requires"] = requires;
            }
            if (!requires.Any(item =>
                item is JsonValue value &&
                value.TryGetValue<string>(out var text) &&
                string.Equals(text, McpApps.ExtensionId, StringComparison.OrdinalIgnoreCase)))
            {
                requires.Add((JsonNode?)JsonValue.Create(McpApps.ExtensionId));
            }
            options.Capabilities.Extensions[McpAppElicitation.ExtensionId] = gateCapability;
        }

        private static JsonObject ToCapabilityObject(object? value) => value switch
        {
            JsonObject jsonObject => jsonObject,
            JsonElement { ValueKind: JsonValueKind.Object } element =>
                JsonNode.Parse(element.GetRawText())!.AsObject(),
            McpUiClientCapabilities typed =>
                JsonSerializer.SerializeToNode(
                    typed,
                    McpAppElicitationJsonContext.Default.McpUiClientCapabilities)!.AsObject(),
            McpAppElicitationCapability typed =>
                JsonSerializer.SerializeToNode(
                    typed,
                    McpAppElicitationJsonContext.Default.McpAppElicitationCapability)!.AsObject(),
            IReadOnlyDictionary<string, object?> properties =>
                (JsonSerializer.SerializeToNode(
                    properties,
                    McpJsonUtilities.DefaultOptions.GetTypeInfo(
                        typeof(IReadOnlyDictionary<string, object?>))) as JsonObject)!,
            _ => throw new InvalidOperationException(
                $"The '{McpApps.ExtensionId}' server capability must be a JSON object."),
        };
    }
}
