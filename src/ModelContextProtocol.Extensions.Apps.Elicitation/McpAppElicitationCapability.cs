using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Apps.Elicitation;

/// <summary>Describes the legacy separately negotiated preview capability for MCP Apps elicitation.</summary>
[Experimental(
    McpAppElicitationDiagnostics.DiagnosticId,
    UrlFormat = McpAppElicitationDiagnostics.Url)]
public sealed class McpAppElicitationCapability
{
    /// <summary>Gets the extensions required by the legacy preview capability.</summary>
    [JsonPropertyName("requires")]
    public IList<string> Requires { get; set; } = [McpApps.ExtensionId];
}
