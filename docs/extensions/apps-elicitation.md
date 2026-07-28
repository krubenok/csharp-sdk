# MCP Apps as elicitation UI

> [!WARNING]
> This is an unofficial reference package for
> [SEP-3118](https://github.com/modelcontextprotocol/modelcontextprotocol/pull/3118). The package is
> `Krubenok.ModelContextProtocol.Extensions.Apps.Elicitation`.

This prototype composes core form elicitation, MCP Apps, and Multi Round-Trip Requests (MRTR) into one
interoperable flow. It is informed by ext-apps issue #511, discussion #514, PR #531, and the deferred-tool
workaround in PR #390.

## Capability negotiation

SEP-3118 proposes app-rendered elicitation as an additive capability of the existing MCP Apps
extension. While the SEP remains unapproved, this package also uses a separate experimental opt-in
gate:

```json
{
  "capabilities": {
    "elicitation": { "form": {} },
    "extensions": {
      "io.modelcontextprotocol/ui": {
        "mimeTypes": ["text/html;profile=mcp-app"],
        "elicitation": {}
      },
      "io.modelcontextprotocol/ui-elicitation": {
        "requires": ["io.modelcontextprotocol/ui"]
      }
    }
  }
}
```

`AddClientCapabilities(...)` and `WithMcpAppElicitation()` emit both entries. The separate extension
and its `requires` member are non-normative package conventions; the MCP extension framework does
not define dependency semantics.

`McpAppElicitation.IsSupported(...)` requires the gate before adding the app-rendering hint. It
accepts the current dual shape and the earlier 0.2 gate-only shape, but rejects nested-only clients
until SEP-3118 is approved or mainlined. Gate removal must therefore be an explicit future preview
migration rather than an application-level workaround.

The app and host bridge independently negotiate first-class elicitation support during
`ui/initialize`:

```json
{
  "appCapabilities": { "elicitation": {} },
  "hostCapabilities": { "elicitation": {} }
}
```

Both members must be present before the host forwards `elicitation/create` to the bound app.

## Elicitation request convention

The request remains a valid core form elicitation. The app link reuses MCP Apps metadata exactly as proposed in
issue #511:

```json
{
  "method": "elicitation/create",
  "params": {
    "mode": "form",
    "message": "Review the portfolio and confirm its manager.",
    "requestedSchema": {
      "type": "object",
      "properties": {
        "confirmed": { "type": "boolean" },
        "selectedManagerId": { "type": "string" }
      },
      "required": ["confirmed", "selectedManagerId"]
    },
    "_meta": {
      "ui": { "resourceUri": "ui://portfolio/assign-manager" }
    }
  }
}
```

A capable host reads and renders the resource, then forwards `elicitation/create` to that app
as JSON-RPC after the normal `ui/initialize` / `ui/notifications/initialized` handshake. The app returns the
standard `ElicitResult`. This follows the direction explored by PR #531 while making app selection explicit.

A capability-aware server omits `_meta.ui` when the client has form elicitation but lacks MCP Apps elicitation, so
the client renders `requestedSchema` using its native form UI. A server that sends the optional hint unconditionally
remains compatible with clients that ignore unknown metadata. In both cases, the server receives the same core
`ElicitResult`.

## Stateless 2026-07-28 MRTR flow

```text
Host                  Stateless MCP server              MCP App
 | tools/call -----------------> |                         |
 | <--- input_required ----------|                         |
 |      elicitation/create + ui:// resource                |
 | resources/read -------------> |                         |
 | <--- text/html;profile=mcp-app|                         |
 | ui/initialize ----------------------------------------> |
 | <----------------------------- ui/notifications/initialized
 | elicitation/create ----------------------------------> |
 | <----------------------------- ElicitResult -----------|
 | tools/call + inputResponses ->|                         |
 | <--- final CallToolResult -----|                         |
```

The server cannot suspend an in-memory handler across stateless HTTP requests. The C# convention therefore uses
`InputRequiredException` on round one and deterministically re-runs the handler on round two. The original tool
arguments and opaque `requestState` must contain everything needed to resume safely. Implementations must avoid
performing non-idempotent work before the elicitation has resolved.

## C# API shape

- `WithMcpAppElicitation()` advertises the nested MCP Apps candidate capability and temporary gate.
- `AddClientCapabilities(...)` advertises form elicitation, the MCP App HTML MIME type, nested elicitation, and the gate.
- `SetAppUi(...)` and `GetAppUi(...)` strongly type the `_meta.ui.resourceUri` convention.
- `SetAppUiIfSupported(...)` reads the request-scoped 2026-07-28 capabilities (or legacy session capabilities) and
  leaves the core request unchanged unless form elicitation, MCP Apps, and the temporary gate were advertised.
  The nested member must be object-valued when present; its absence is accepted only for 0.2 compatibility.
- `ResolveOrRequest<T>(...)` emits the first-round MRTR request and deserializes the retried response as `T`.

## Host requirements and safety

- Validate the URI and only resolve declared `ui://` resources from the requesting server.
- Preserve the normal elicitation identity, review, decline, cancel, and notification behavior.
- Validate accepted content against `requestedSchema`; the app is not a trusted validator.
- Apply the complete MCP Apps sandbox, CSP, permissions, origin, and teardown rules.
- Do not use form mode for secrets or credentials; use core URL-mode elicitation for sensitive input.
- Bind pending elicitations to the originating server, user, request, and rendered app instance.
- Support sequential requests explicitly; concurrent routing needs stable per-elicitation app instances.

## Remaining spec questions

1. Should forwarding use the standard `elicitation/create` method, as PR #531 does, or a UI-prefixed method?
2. Should `_meta.ui.resourceUri` alone opt into routing, or must client-to-server capability negotiation always be present?
3. Who performs final schema validation and how are invalid app responses surfaced without losing the elicitation?
4. What lifecycle notification tells the app and host that the elicitation has completed or been cancelled externally?
5. How should multiple simultaneous app elicitations from one tool call be ordered and displayed?
