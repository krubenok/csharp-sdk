using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Extensions.Apps.Elicitation;

/// <summary>Strongly typed conventions for using MCP Apps as form elicitation UI.</summary>
[Experimental(
    McpAppElicitationDiagnostics.DiagnosticId,
    UrlFormat = McpAppElicitationDiagnostics.Url)]
public static class McpAppElicitation
{
    /// <summary>The experimental extension identifier.</summary>
    public const string ExtensionId = "io.modelcontextprotocol/ui-elicitation";

    /// <summary>The metadata member inherited from the MCP Apps extension.</summary>
    public const string UiMetaKey = "ui";

    /// <summary>Adds all client capabilities required for app-rendered form elicitation.</summary>
    public static ClientCapabilities AddClientCapabilities(ClientCapabilities capabilities)
    {
#if NET
        ArgumentNullException.ThrowIfNull(capabilities);
#else
        if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));
#endif

        capabilities.Elicitation ??= new ElicitationCapability();
        capabilities.Elicitation.Form ??= new FormElicitationCapability();
        capabilities.Extensions ??= new Dictionary<string, object>();

        var appsCapability = ToCapabilityObject(
            capabilities.Extensions.TryGetValue(McpApps.ExtensionId, out var existingApps)
                ? existingApps
                : null);
        EnsureStringArrayValue(appsCapability, "mimeTypes", McpApps.HtmlMimeType);
        capabilities.Extensions[McpApps.ExtensionId] = appsCapability;

        var elicitationCapability = ToCapabilityObject(
            capabilities.Extensions.TryGetValue(ExtensionId, out var existingElicitation)
                ? existingElicitation
                : null);
        EnsureStringArrayValue(elicitationCapability, "requires", McpApps.ExtensionId);
        capabilities.Extensions[ExtensionId] = elicitationCapability;

        return capabilities;
    }

    /// <summary>Returns whether the client advertised form elicitation, MCP Apps, and this extension.</summary>
    public static bool IsSupported(ClientCapabilities? capabilities)
    {
        if (capabilities is null)
        {
            return false;
        }

        return HasFormElicitationCapability(capabilities.Elicitation) &&
            HasAppsHtmlCapability(capabilities) &&
            capabilities.Extensions?.TryGetValue(ExtensionId, out var value) == true &&
            IsCapabilityValue(value);
    }

    /// <summary>Associates an elicitation request with an MCP App UI resource.</summary>
    public static ElicitRequestParams SetAppUi(ElicitRequestParams request, string resourceUri)
    {
        ValidateAppUiArguments(request, resourceUri);

        request.Meta ??= [];
        request.Meta[UiMetaKey] = JsonSerializer.SerializeToNode(
            new McpAppElicitationMeta { ResourceUri = resourceUri },
            McpAppElicitationJsonContext.Default.McpAppElicitationMeta);
        return request;
    }

    /// <summary>
    /// Associates an elicitation request with an MCP App UI resource when the client advertised all required
    /// capabilities. Otherwise, leaves the core elicitation unchanged for native form rendering.
    /// </summary>
    public static ElicitRequestParams SetAppUiIfSupported(
        ElicitRequestParams request,
        ClientCapabilities? capabilities,
        string resourceUri)
    {
        ValidateAppUiArguments(request, resourceUri);
        return IsSupported(capabilities) ? SetAppUi(request, resourceUri) : request;
    }

    /// <summary>
    /// Associates an elicitation request with an MCP App UI resource when the requesting client advertised all
    /// required capabilities. Uses request-scoped capabilities on 2026-07-28 and session capabilities on legacy
    /// stateful connections.
    /// </summary>
    public static ElicitRequestParams SetAppUiIfSupported<TParams>(
        ElicitRequestParams request,
        RequestContext<TParams> context,
        string resourceUri)
    {
#if NET
        ArgumentNullException.ThrowIfNull(context);
#else
        if (context is null) throw new ArgumentNullException(nameof(context));
#endif
        var capabilities = context.JsonRpcRequest.Context?.ClientCapabilities ?? context.Server.ClientCapabilities;
        return SetAppUiIfSupported(request, capabilities, resourceUri);
    }

    private static void ValidateAppUiArguments(ElicitRequestParams request, string resourceUri)
    {
#if NET
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceUri);
#else
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(resourceUri)) throw new ArgumentException("The resource URI is required.", nameof(resourceUri));
#endif
        if (!Uri.TryCreate(resourceUri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "ui", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("MCP App elicitation resources must use the ui:// URI scheme.", nameof(resourceUri));
        }
    }

    /// <summary>Gets the app UI metadata from an elicitation request, if present and valid.</summary>
    public static McpAppElicitationMeta? GetAppUi(ElicitRequestParams request)
    {
#if NET
        ArgumentNullException.ThrowIfNull(request);
#else
        if (request is null) throw new ArgumentNullException(nameof(request));
#endif
        if (request.Meta?[UiMetaKey] is not JsonNode node)
        {
            return null;
        }

        try
        {
            return node.Deserialize(McpAppElicitationJsonContext.Default.McpAppElicitationMeta);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns a typed elicitation response on an MRTR retry, or requests the app-rendered elicitation on the first round.
    /// </summary>
    /// <remarks>
    /// This explicit retry-safe convention is suitable for stateless HTTP. Any operation state needed after the
    /// retry must be encoded in the original request arguments or in <paramref name="requestState"/>.
    /// Clients that do not support this extension can ignore <c>_meta.ui</c> and render the requested schema natively.
    /// </remarks>
    public static ElicitResult<T> ResolveOrRequest<T>(
        McpServer server,
        RequestParams requestParams,
        string inputKey,
        ElicitRequestParams elicitation,
        JsonTypeInfo<T> responseTypeInfo,
        string? requestState = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(requestParams);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputKey);
        ArgumentNullException.ThrowIfNull(elicitation);
        ArgumentNullException.ThrowIfNull(responseTypeInfo);
#else
        if (server is null) throw new ArgumentNullException(nameof(server));
        if (requestParams is null) throw new ArgumentNullException(nameof(requestParams));
        if (string.IsNullOrWhiteSpace(inputKey)) throw new ArgumentException("The input key is required.", nameof(inputKey));
        if (elicitation is null) throw new ArgumentNullException(nameof(elicitation));
        if (responseTypeInfo is null) throw new ArgumentNullException(nameof(responseTypeInfo));
#endif

        if (requestParams.InputResponses?.TryGetValue(inputKey, out var response) == true)
        {
            ElicitResult raw;
            try
            {
                raw = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo)
                    ?? throw InvalidResponse(inputKey, "the response was empty");
            }
            catch (JsonException ex)
            {
                throw InvalidResponse(inputKey, "the response was not a valid elicitation result", ex);
            }

            if (!raw.IsAccepted)
            {
                return new ElicitResult<T> { Action = raw.Action };
            }

            if (raw.Content is null)
            {
                throw InvalidResponse(inputKey, "an accepted response did not contain content");
            }

            try
            {
                JsonObject content = [];
                foreach (var item in raw.Content)
                {
                    content[item.Key] = JsonNode.Parse(item.Value.GetRawText());
                }

                var typed = JsonSerializer.Deserialize(content, responseTypeInfo);
                if (typed is null)
                {
                    throw InvalidResponse(inputKey, "the accepted content deserialized to null");
                }

                return new ElicitResult<T> { Action = raw.Action, Content = typed };
            }
            catch (JsonException ex)
            {
                throw InvalidResponse(inputKey, $"the accepted content was not valid {typeof(T).Name}", ex);
            }
        }

        if (requestParams.InputResponses is { Count: > 0 })
        {
            throw new McpProtocolException($"The MRTR retry did not contain the expected '{inputKey}' response.", McpErrorCode.InvalidParams);
        }

        if (!server.IsMrtrSupported)
        {
            throw new InvalidOperationException("App elicitation requires an MRTR-capable client or a stateful transport.");
        }

        throw new InputRequiredException(
            new Dictionary<string, InputRequest>
            {
                [inputKey] = InputRequest.ForElicitation(elicitation),
            },
            requestState);
    }

    private static bool IsCapabilityValue(object? value) => value switch
    {
        McpAppElicitationCapability => true,
        JsonObject => true,
        JsonElement { ValueKind: JsonValueKind.Object } => true,
        _ => false,
    };

    private static bool HasFormElicitationCapability(ElicitationCapability? capability) =>
        capability is not null && (capability.Form is not null || capability.Url is null);

    private static bool HasAppsHtmlCapability(ClientCapabilities capabilities)
    {
        if (capabilities.Extensions?.TryGetValue(McpApps.ExtensionId, out var value) != true)
        {
            return false;
        }

        return value switch
        {
            McpUiClientCapabilities typed => typed.MimeTypes?.Contains(
                McpApps.HtmlMimeType,
                StringComparer.OrdinalIgnoreCase) == true,
            JsonObject jsonObject => ContainsStringValue(jsonObject["mimeTypes"], McpApps.HtmlMimeType),
            JsonElement { ValueKind: JsonValueKind.Object } element =>
                element.TryGetProperty("mimeTypes", out var mimeTypes) &&
                mimeTypes.ValueKind == JsonValueKind.Array &&
                mimeTypes.EnumerateArray().Any(item =>
                    item.ValueKind == JsonValueKind.String &&
                    string.Equals(item.GetString(), McpApps.HtmlMimeType, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private static JsonObject ToCapabilityObject(object? value)
    {
        try
        {
            return value switch
            {
                JsonObject jsonObject => jsonObject,
                JsonElement { ValueKind: JsonValueKind.Object } element =>
                    JsonNode.Parse(element.GetRawText())!.AsObject(),
                McpUiClientCapabilities apps =>
                    JsonSerializer.SerializeToNode(
                        apps,
                        McpAppElicitationJsonContext.Default.McpUiClientCapabilities)!.AsObject(),
                McpAppElicitationCapability elicitation =>
                    JsonSerializer.SerializeToNode(
                        elicitation,
                        McpAppElicitationJsonContext.Default.McpAppElicitationCapability)!.AsObject(),
                _ => [],
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void EnsureStringArrayValue(JsonObject capability, string propertyName, string requiredValue)
    {
        if (capability[propertyName] is not JsonArray values)
        {
            values = [];
            capability[propertyName] = values;
        }

        if (!ContainsStringValue(values, requiredValue))
        {
            values.Add((JsonNode?)JsonValue.Create(requiredValue));
        }
    }

    private static bool ContainsStringValue(JsonNode? node, string requiredValue) =>
        node is JsonArray values &&
        values.Any(item =>
            item is JsonValue value &&
            value.TryGetValue<string>(out var text) &&
            string.Equals(text, requiredValue, StringComparison.OrdinalIgnoreCase));

    private static McpProtocolException InvalidResponse(
        string inputKey,
        string reason,
        Exception? innerException = null) =>
        new(
            $"The '{inputKey}' elicitation response was invalid because {reason}.",
            innerException,
            McpErrorCode.InvalidParams);
}
