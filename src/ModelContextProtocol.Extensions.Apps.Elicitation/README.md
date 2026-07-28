# Experimental MCP Apps Elicitation Extension

> [!WARNING]
> This is an unofficial prototype package published from
> [`krubenok/csharp-sdk`](https://github.com/krubenok/csharp-sdk/tree/feature/apps-elicitation).
> It is not an adopted MCP extension or an official Model Context Protocol SDK package. Its API and
> wire contract may change while
> [SEP-3118](https://github.com/modelcontextprotocol/modelcontextprotocol/pull/3118) is reviewed.

`Krubenok.ModelContextProtocol.Extensions.Apps.Elicitation` provides strongly typed conventions for
using an MCP App as the renderer for a core form elicitation. It follows SEP-3118 by composing:

- core form elicitation;
- `io.modelcontextprotocol/ui` with its MCP App HTML MIME type; and
- the nested `io.modelcontextprotocol/ui.elicitation` capability.

The elicitation always retains a complete `requestedSchema`. Clients that support form elicitation
but do not negotiate MCP Apps elicitation receive the ordinary native form request.

## Compatibility

| Component | Version |
| --- | --- |
| Package | `0.3.0-preview.1` |
| C# SDK | `2.0.0-rc.2` |
| MCP Apps | `io.modelcontextprotocol/ui` |
| Stateless protocol | `2026-07-28` MRTR |

## Install from GitHub Packages

The package is public. GitHub's NuGet registry still requires authentication, so configure Kyle
Rubenok's feed with a classic personal access token that has `read:packages`. Do not commit the
token.

```shell
dotnet nuget add source "https://nuget.pkg.github.com/krubenok/index.json" \
  --name krubenok-github \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_TOKEN \
  --store-password-in-clear-text

dotnet add package Krubenok.ModelContextProtocol.Extensions.Apps.Elicitation \
  --version 0.3.0-preview.1 \
  --source krubenok-github
```

If a repository uses NuGet package source mapping, route `Krubenok.*` to the GitHub feed and keep
official MCP packages on NuGet.org:

```xml
<packageSourceMapping>
  <packageSource key="nuget.org">
    <package pattern="ModelContextProtocol*" />
    <package pattern="Microsoft.*" />
    <package pattern="System.*" />
  </packageSource>
  <packageSource key="krubenok-github">
    <package pattern="Krubenok.*" />
  </packageSource>
</packageSourceMapping>
```

The public namespace remains `ModelContextProtocol.Extensions.Apps.Elicitation`; only the package ID
is fork-scoped.

## Enable the server extension

```csharp
using ModelContextProtocol.Extensions.Apps.Elicitation;

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<PortfolioTools>()
    .WithMcpAppElicitation();
```

`WithMcpAppElicitation()` also enables the required MCP Apps extension and advertises:

```json
{
  "extensions": {
    "io.modelcontextprotocol/ui": {
      "elicitation": {}
    }
  }
}
```

## Advertise client support

```csharp
var capabilities = McpAppElicitation.AddClientCapabilities(new ClientCapabilities());
```

This merges, rather than replaces, existing extension settings and advertises the canonical
SEP-3118 capability shape:

```json
{
  "elicitation": {
    "form": {}
  },
  "extensions": {
    "io.modelcontextprotocol/ui": {
      "mimeTypes": ["text/html;profile=mcp-app"],
      "elicitation": {}
    }
  }
}
```

## Migrating from 0.2 previews

Version 0.3 emits the nested SEP-3118 capability instead of the separate
`io.modelcontextprotocol/ui-elicitation` identifier used by 0.2 previews. `IsSupported(...)`
temporarily accepts both shapes so servers can migrate without application-level compatibility
code. The legacy shape is recognized only when its `requires` array includes
`io.modelcontextprotocol/ui`; both shapes still require core form support and the MCP App HTML MIME
type. `AddClientCapabilities(...)` and `WithMcpAppElicitation()` remove a preconfigured legacy entry
so upgraded emitters produce only the nested capability. New clients and fixtures should do the
same.

The app and host also negotiate first-class bridge capabilities during `ui/initialize`:

```json
{
  "appCapabilities": { "elicitation": {} },
  "hostCapabilities": { "elicitation": {} }
}
```

Do not place these bridge capabilities under `experimental` or use the legacy separate extension
identifier.

## Request app-rendered input

```csharp
var elicitation = McpAppElicitation.SetAppUiIfSupported(
    new ElicitRequestParams
    {
        Message = "Review the portfolio and confirm its manager.",
        RequestedSchema = requestedSchema,
    },
    context,
    "ui://portfolio/assign-manager");

var response = McpAppElicitation.ResolveOrRequest(
    server,
    context.Params,
    inputKey: "manager-assignment",
    elicitation,
    MyJsonContext.Default.ManagerAssignment,
    requestState: "assign-account-manager:v1");
```

When all capabilities are present, the core request gains the MCP Apps resource hint:

```json
{
  "_meta": {
    "ui": {
      "resourceUri": "ui://portfolio/assign-manager"
    }
  }
}
```

`ResolveOrRequest<T>` implements the explicit stateless MRTR convention:

1. The first invocation throws `InputRequiredException`, producing an `InputRequiredResult`.
2. The client renders the selected app or its native form fallback.
3. The client retries the original operation with `inputResponses` and `requestState`.
4. The second invocation validates and deserializes the matching response as `T`.

Any state required after the retry must be encoded in the original request arguments or the opaque
`requestState`; do not rely on server process affinity.

## Experimental diagnostics

Public APIs emit `MCPAELICITATION001`. MCP Apps APIs also emit the SDK's `MCPEXP003`. Prototype
consumers can acknowledge both diagnostics explicitly:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);MCPEXP003;MCPAELICITATION001</NoWarn>
</PropertyGroup>
```

## End-to-end sample

The source branch contains a
[`stateless server`](https://github.com/krubenok/csharp-sdk/tree/feature/apps-elicitation/samples/AppElicitationServer)
and a
[`minimal host with the HTML app`](https://github.com/krubenok/csharp-sdk/tree/feature/apps-elicitation/samples/AppElicitationHost).
Together they demonstrate both app-enhanced and native form fallback paths.
