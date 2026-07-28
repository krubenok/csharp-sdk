using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Extensions.Apps.Elicitation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

#pragma warning disable MCPEXP003
#pragma warning disable MCPAELICITATION001

namespace ModelContextProtocol.Tests.Server;

public class McpAppElicitationTests
{
    [Fact]
    public void AddClientCapabilities_EmitsNestedCandidateAndExperimentalGate()
    {
        var capabilities = McpAppElicitation.AddClientCapabilities(new ClientCapabilities());
        var json = JsonSerializer.SerializeToNode(capabilities, McpJsonUtilities.DefaultOptions)!.AsObject();
        var extensions = json["extensions"]!.AsObject();
        var apps = extensions[McpApps.ExtensionId]!.AsObject();
        var gate = extensions[McpAppElicitation.ExtensionId]!.AsObject();

        Assert.NotNull(capabilities.Elicitation?.Form);
        Assert.Equal(2, extensions.Count);
        Assert.Contains(
            apps["mimeTypes"]!.AsArray(),
            value => value?.GetValue<string>() == McpApps.HtmlMimeType);
        Assert.IsType<JsonObject>(apps["elicitation"]);
        Assert.Contains(
            gate["requires"]!.AsArray(),
            value => value?.GetValue<string>() == McpApps.ExtensionId);
        Assert.True(McpAppElicitation.IsSupported(capabilities));
    }

    [Fact]
    public void AddClientCapabilities_MergesExistingSettings()
    {
        var apps = new JsonObject
        {
            ["mimeTypes"] = new JsonArray("application/example"),
            ["customSetting"] = true,
        };
        var capabilities = new ClientCapabilities
        {
            Extensions = new Dictionary<string, object>
            {
                [McpApps.ExtensionId] = apps,
                [McpAppElicitation.ExtensionId] = new JsonObject
                {
                    ["customSetting"] = true,
                },
                ["com.example/other"] = new JsonObject
                {
                    ["customSetting"] = true,
                },
            },
        };

        McpAppElicitation.AddClientCapabilities(capabilities);

        Assert.True(apps["customSetting"]?.GetValue<bool>());
        Assert.Contains(
            apps["mimeTypes"]!.AsArray(),
            value => value?.GetValue<string>() == McpApps.HtmlMimeType);
        Assert.IsType<JsonObject>(apps["elicitation"]);
        var gate = Assert.IsType<JsonObject>(capabilities.Extensions[McpAppElicitation.ExtensionId]);
        Assert.True(gate["customSetting"]?.GetValue<bool>());
        Assert.Contains(
            gate["requires"]!.AsArray(),
            value => value?.GetValue<string>() == McpApps.ExtensionId);
        Assert.True(
            Assert.IsType<JsonObject>(capabilities.Extensions["com.example/other"])["customSetting"]?.GetValue<bool>());
    }

    [Fact]
    public void IsSupported_BackwardCompatibleEmptyCoreElicitationCapabilityIsFormCapable()
    {
        var capabilities = McpAppElicitation.AddClientCapabilities(new ClientCapabilities());
        capabilities.Elicitation = new ElicitationCapability();

        Assert.True(McpAppElicitation.IsSupported(capabilities));
    }

    [Fact]
    public void IsSupported_UrlOnlyCoreElicitationCapabilityIsNotFormCapable()
    {
        var capabilities = McpAppElicitation.AddClientCapabilities(new ClientCapabilities());
        capabilities.Elicitation = new ElicitationCapability { Url = new UrlElicitationCapability() };

        Assert.False(McpAppElicitation.IsSupported(capabilities));
    }

    [Fact]
    public void IsSupported_AppsCapabilityWithoutMcpAppMimeTypeIsNotSupported()
    {
        var capabilities = McpAppElicitation.AddClientCapabilities(new ClientCapabilities());
        capabilities.Extensions![McpApps.ExtensionId] = new JsonObject
        {
            ["mimeTypes"] = new JsonArray("text/html"),
        };

        Assert.False(McpAppElicitation.IsSupported(capabilities));
    }

    [Fact]
    public void IsSupported_PrototypeCapabilityMustRequireMcpApps()
    {
        var capabilities = CreateLegacyPreviewCapabilities();
        capabilities.Extensions![McpAppElicitation.ExtensionId] = new JsonObject();

        Assert.False(McpAppElicitation.IsSupported(capabilities));
    }

    [Fact]
    public void IsSupported_AcceptsLegacySeparatePreviewShape()
    {
        Assert.True(McpAppElicitation.IsSupported(CreateLegacyPreviewCapabilities()));
    }

    [Fact]
    public void IsSupported_AcceptsCurrentDualShape()
    {
        var capabilities = CreateLegacyPreviewCapabilities();
        var apps = Assert.IsType<JsonElement>(capabilities.Extensions![McpApps.ExtensionId]);
        var appsObject = JsonNode.Parse(apps.GetRawText())!.AsObject();
        appsObject["elicitation"] = new JsonObject();
        capabilities.Extensions[McpApps.ExtensionId] = appsObject;

        Assert.True(McpAppElicitation.IsSupported(capabilities));
    }

    [Fact]
    public void IsSupported_NestedCandidateWithoutExperimentalGateIsNotSupported()
    {
        var capabilities = CreateCanonicalSepCapabilities();

        Assert.False(McpAppElicitation.IsSupported(capabilities));
        Assert.False(capabilities.Extensions?.ContainsKey(McpAppElicitation.ExtensionId));
    }

    [Fact]
    public void IsSupported_CanonicalNestedCapabilityMustBeAnObject()
    {
        var capabilities = McpAppElicitation.AddClientCapabilities(new ClientCapabilities());
        var appsObject = Assert.IsType<JsonObject>(capabilities.Extensions![McpApps.ExtensionId]);
        appsObject["elicitation"] = true;

        Assert.False(McpAppElicitation.IsSupported(capabilities));
    }

    [Fact]
    public void WithMcpAppElicitation_AdvertisesNestedCandidateAndExperimentalGate()
    {
        var services = new ServiceCollection();
        services.AddMcpServer(options =>
        {
            options.Capabilities = new ServerCapabilities
            {
                Extensions = new Dictionary<string, object>
                {
                    [McpAppElicitation.ExtensionId] = new JsonObject
                    {
                        ["requires"] = new JsonArray(McpApps.ExtensionId),
                    },
                },
            };
        }).WithMcpAppElicitation();

        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.Capabilities;

        Assert.Equal(2, capabilities!.Extensions!.Count);
        var apps = Assert.IsType<JsonObject>(capabilities?.Extensions?[McpApps.ExtensionId]);
        Assert.IsType<JsonObject>(apps["elicitation"]);
        var gate = Assert.IsType<JsonObject>(capabilities?.Extensions?[McpAppElicitation.ExtensionId]);
        Assert.Contains(
            gate["requires"]!.AsArray(),
            value => value?.GetValue<string>() == McpApps.ExtensionId);
    }

    [Fact]
    public void WithMcpAppElicitation_MergesTypedAppsCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer(options =>
        {
            options.Capabilities = new ServerCapabilities
            {
                Extensions = new Dictionary<string, object>
                {
                    [McpApps.ExtensionId] = new McpUiClientCapabilities
                    {
                        MimeTypes = ["application/example"],
                    },
                },
            };
        }).WithMcpAppElicitation();

        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.Capabilities;

        var apps = Assert.IsType<JsonObject>(capabilities?.Extensions?[McpApps.ExtensionId]);
        Assert.Contains(
            apps["mimeTypes"]!.AsArray(),
            value => value?.GetValue<string>() == "application/example");
        Assert.IsType<JsonObject>(apps["elicitation"]);
    }

    [Fact]
    public void WithMcpAppElicitation_MergesDictionaryAppsCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer(options =>
        {
            options.Capabilities = new ServerCapabilities
            {
                Extensions = new Dictionary<string, object>
                {
                    [McpApps.ExtensionId] = new Dictionary<string, object>
                    {
                        ["customSetting"] = true,
                    },
                },
            };
        }).WithMcpAppElicitation();

        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.Capabilities;

        var apps = Assert.IsType<JsonObject>(capabilities?.Extensions?[McpApps.ExtensionId]);
        Assert.True(apps["customSetting"]?.GetValue<bool>());
        Assert.IsType<JsonObject>(apps["elicitation"]);
    }

    [Fact]
    public void SetAppUi_RoundTripsResourceUri()
    {
        var request = McpAppElicitation.SetAppUi(CreateRequest(), "ui://portfolio/assign-manager");

        Assert.Equal("ui://portfolio/assign-manager", McpAppElicitation.GetAppUi(request)?.ResourceUri);
        Assert.Equal("ui://portfolio/assign-manager", request.Meta?["ui"]?["resourceUri"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com/app")]
    [InlineData("not-a-uri")]
    public void SetAppUi_RejectsInvalidResourceUri(string resourceUri)
    {
        Assert.Throws<ArgumentException>(() => McpAppElicitation.SetAppUi(CreateRequest(), resourceUri));
    }

    [Fact]
    public void SetAppUiIfSupported_FormOnlyClientLeavesCoreRequestUnchanged()
    {
        var capabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() },
        };

        var request = McpAppElicitation.SetAppUiIfSupported(
            CreateRequest(),
            capabilities,
            "ui://portfolio/assign-manager");

        Assert.Null(request.Meta);
        Assert.NotNull(request.RequestedSchema);
    }

    [Fact]
    public void SetAppUiIfSupported_AppElicitationClientAddsResourceUri()
    {
        var capabilities = McpAppElicitation.AddClientCapabilities(new ClientCapabilities());

        var request = McpAppElicitation.SetAppUiIfSupported(
            CreateRequest(),
            capabilities,
            "ui://portfolio/assign-manager");

        Assert.Equal("ui://portfolio/assign-manager", McpAppElicitation.GetAppUi(request)?.ResourceUri);
    }

    [Fact]
    public void SetAppUiIfSupported_RequestCapabilitiesTakePrecedenceOverServerCapabilities()
    {
        var server = new Mock<McpServer>();
        server.SetupGet(s => s.ClientCapabilities)
            .Returns(McpAppElicitation.AddClientCapabilities(new ClientCapabilities()));
        var requestCapabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() },
        };
        var context = new RequestContext<CallToolRequestParams>(
            server.Object,
            new JsonRpcRequest
            {
                Id = new RequestId(1),
                Method = RequestMethods.ToolsCall,
                Context = new JsonRpcMessageContext
                {
                    ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
                    ClientCapabilities = requestCapabilities,
                },
            },
            new CallToolRequestParams { Name = "assign_account_manager" });

        var request = McpAppElicitation.SetAppUiIfSupported(
            CreateRequest(),
            context,
            "ui://portfolio/assign-manager");

        Assert.Null(request.Meta);
    }

    [Fact]
    public void SetAppUiIfSupported_RequestScopedNestedCapabilitiesWithoutGateLeaveRequestUnchanged()
    {
        var server = new Mock<McpServer>();
        var context = new RequestContext<CallToolRequestParams>(
            server.Object,
            new JsonRpcRequest
            {
                Id = new RequestId(1),
                Method = RequestMethods.ToolsCall,
                Context = new JsonRpcMessageContext
                {
                    ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
                    ClientCapabilities = CreateCanonicalSepCapabilities(),
                },
            },
            new CallToolRequestParams { Name = "assign_account_manager" });

        var request = McpAppElicitation.SetAppUiIfSupported(
            CreateRequest(),
            context,
            "ui://portfolio/assign-manager");

        Assert.Null(McpAppElicitation.GetAppUi(request));
    }

    [Fact]
    public void ResolveOrRequest_FirstRoundReturnsInputRequiredResult()
    {
        var server = new Mock<McpServer>();
        server.SetupGet(s => s.IsMrtrSupported).Returns(true);
        var requestParams = new CallToolRequestParams { Name = "assign_account_manager" };
        var elicitation = McpAppElicitation.SetAppUi(CreateRequest(), "ui://portfolio/assign-manager");

        var exception = Assert.Throws<InputRequiredException>(() => McpAppElicitation.ResolveOrRequest(
            server.Object,
            requestParams,
            "manager-assignment",
            elicitation,
            TestJsonContext.Default.AssignmentResponse,
            "state-v1"));

        Assert.Equal("state-v1", exception.Result.RequestState);
        var input = Assert.Single(exception.Result.InputRequests!);
        Assert.Equal("manager-assignment", input.Key);
        Assert.Equal(RequestMethods.ElicitationCreate, input.Value.Method);
    }

    [Fact]
    public void ResolveOrRequest_RetryReturnsTypedContent()
    {
        var server = new Mock<McpServer>();
        server.SetupGet(s => s.IsMrtrSupported).Returns(true);
        var requestParams = new CallToolRequestParams
        {
            Name = "assign_account_manager",
            RequestState = "state-v1",
            InputResponses = new Dictionary<string, InputResponse>
            {
                ["manager-assignment"] = InputResponse.FromElicitResult(new ElicitResult
                {
                    Action = "accept",
                    Content = new Dictionary<string, JsonElement>
                    {
                        ["confirmed"] = JsonSerializer.SerializeToElement(true, McpJsonUtilities.DefaultOptions),
                        ["selectedManagerId"] = JsonSerializer.SerializeToElement("mgr-priya", McpJsonUtilities.DefaultOptions),
                    },
                }),
            },
        };

        var result = McpAppElicitation.ResolveOrRequest(
            server.Object,
            requestParams,
            "manager-assignment",
            CreateRequest(),
            TestJsonContext.Default.AssignmentResponse);

        Assert.True(result.IsAccepted);
        Assert.True(result.Content?.Confirmed);
        Assert.Equal("mgr-priya", result.Content?.SelectedManagerId);
    }

    [Fact]
    public void ResolveOrRequest_AcceptedResponseWithoutContentIsInvalid()
    {
        var exception = Assert.Throws<McpProtocolException>(() => McpAppElicitation.ResolveOrRequest(
            CreateMrtrServer(),
            CreateRetryParams(new ElicitResult { Action = "accept" }),
            "manager-assignment",
            CreateRequest(),
            TestJsonContext.Default.AssignmentResponse));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Contains("did not contain content", exception.Message);
    }

    [Fact]
    public void ResolveOrRequest_ContentThatDoesNotMatchTypedContractIsInvalid()
    {
        var exception = Assert.Throws<McpProtocolException>(() => McpAppElicitation.ResolveOrRequest(
            CreateMrtrServer(),
            CreateRetryParams(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["confirmed"] = JsonSerializer.SerializeToElement("not-a-boolean", McpJsonUtilities.DefaultOptions),
                    ["selectedManagerId"] = JsonSerializer.SerializeToElement("mgr-priya", McpJsonUtilities.DefaultOptions),
                },
            }),
            "manager-assignment",
            CreateRequest(),
            TestJsonContext.Default.AssignmentResponse));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Contains(nameof(AssignmentResponse), exception.Message);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    private static McpServer CreateMrtrServer()
    {
        var server = new Mock<McpServer>();
        server.SetupGet(s => s.IsMrtrSupported).Returns(true);
        return server.Object;
    }

    private static CallToolRequestParams CreateRetryParams(ElicitResult result) => new()
    {
        Name = "assign_account_manager",
        RequestState = "state-v1",
        InputResponses = new Dictionary<string, InputResponse>
        {
            ["manager-assignment"] = InputResponse.FromElicitResult(result),
        },
    };

    private static ElicitRequestParams CreateRequest() => new()
    {
        Message = "Choose a manager.",
        RequestedSchema = new ElicitRequestParams.RequestSchema(),
    };

    private static ClientCapabilities CreateCanonicalSepCapabilities() =>
        JsonSerializer.Deserialize<ClientCapabilities>(
            """
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
            """,
            McpJsonUtilities.DefaultOptions)!;

    private static ClientCapabilities CreateLegacyPreviewCapabilities() =>
        JsonSerializer.Deserialize<ClientCapabilities>(
            """
            {
              "elicitation": {
                "form": {}
              },
              "extensions": {
                "io.modelcontextprotocol/ui": {
                  "mimeTypes": ["text/html;profile=mcp-app"]
                },
                "io.modelcontextprotocol/ui-elicitation": {
                  "requires": ["io.modelcontextprotocol/ui"]
                }
              }
            }
            """,
            McpJsonUtilities.DefaultOptions)!;
}

public sealed class AssignmentResponse
{
    public bool Confirmed { get; set; }

    public string SelectedManagerId { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AssignmentResponse))]
internal sealed partial class TestJsonContext : JsonSerializerContext
{
}

#if !NET472
public sealed class McpAppElicitationCompatibilityTests : ClientServerTestBase
{
    public McpAppElicitationCompatibilityTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(
        Microsoft.Extensions.DependencyInjection.ServiceCollection services,
        IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options => options.ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion);
        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                CompleteAssignment,
                new McpServerToolCreateOptions
                {
                    Name = "complete-assignment",
                    Description = "Completes an assignment using app-enhanced or native form elicitation.",
                })
        ]);
    }

    [Fact]
    public async Task FormOnlyClient_UsesNativeElicitationAndCompletesMrtrRetry()
    {
        ElicitRequestParams? observedRequest = null;
        var options = CreateClientOptions(new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() },
        });
        options.Handlers.ElicitationHandler = (request, _) =>
        {
            observedRequest = request;
            return new ValueTask<ElicitResult>(CreateAcceptedResult());
        };

        await using var client = await CreateMcpClientForServer(options);
        var result = await client.CallToolAsync(
            "complete-assignment",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(observedRequest);
        Assert.Null(McpAppElicitation.GetAppUi(observedRequest));
        Assert.NotNull(observedRequest.RequestedSchema);
        Assert.Equal("native:mgr-priya", GetText(result));
    }

    [Fact]
    public async Task DualShapeClient_ReceivesResourceHintAndCompletesMrtrRetry()
    {
        ElicitRequestParams? observedRequest = null;
        var options = CreateClientOptions(
            McpAppElicitation.AddClientCapabilities(new ClientCapabilities()));
        options.Handlers.ElicitationHandler = (request, _) =>
        {
            observedRequest = request;
            return new ValueTask<ElicitResult>(CreateAcceptedResult());
        };

        await using var client = await CreateMcpClientForServer(options);
        var result = await client.CallToolAsync(
            "complete-assignment",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(observedRequest);
        Assert.Equal(
            "ui://portfolio/assign-manager",
            McpAppElicitation.GetAppUi(observedRequest)?.ResourceUri);
        Assert.Equal("app:mgr-priya", GetText(result));
    }

    [Fact]
    public async Task LegacyGateOnlyClient_ReceivesResourceHintAndCompletesMrtrRetry()
    {
        ElicitRequestParams? observedRequest = null;
        var options = CreateClientOptions(CreateLegacyGateOnlyCapabilities());
        options.Handlers.ElicitationHandler = (request, _) =>
        {
            observedRequest = request;
            return new ValueTask<ElicitResult>(CreateAcceptedResult());
        };

        await using var client = await CreateMcpClientForServer(options);
        var result = await client.CallToolAsync(
            "complete-assignment",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(observedRequest);
        Assert.Equal(
            "ui://portfolio/assign-manager",
            McpAppElicitation.GetAppUi(observedRequest)?.ResourceUri);
        Assert.Equal("app:mgr-priya", GetText(result));
    }

    [Fact]
    public async Task NestedOnlySepClient_UsesNativeFormAndCompletesMrtrRetry()
    {
        ElicitRequestParams? observedRequest = null;
        var options = CreateClientOptions(JsonSerializer.Deserialize<ClientCapabilities>(
            """
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
            """,
            McpJsonUtilities.DefaultOptions)!);
        options.Handlers.ElicitationHandler = (request, _) =>
        {
            observedRequest = request;
            return new ValueTask<ElicitResult>(CreateAcceptedResult());
        };

        await using var client = await CreateMcpClientForServer(options);
        var result = await client.CallToolAsync(
            "complete-assignment",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(observedRequest);
        Assert.Null(McpAppElicitation.GetAppUi(observedRequest));
        Assert.Equal("native:mgr-priya", GetText(result));
    }

    private static string CompleteAssignment(
        McpServer server,
        RequestContext<CallToolRequestParams> context)
    {
        var elicitation = McpAppElicitation.SetAppUiIfSupported(
            CreateRequest(),
            context,
            "ui://portfolio/assign-manager");
        var presentation = McpAppElicitation.GetAppUi(elicitation) is null ? "native" : "app";
        var response = McpAppElicitation.ResolveOrRequest(
            server,
            context.Params,
            "manager-assignment",
            elicitation,
            TestJsonContext.Default.AssignmentResponse,
            "state-v1");

        return $"{presentation}:{response.Content?.SelectedManagerId}";
    }

    private static McpClientOptions CreateClientOptions(ClientCapabilities capabilities) => new()
    {
        ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
        Capabilities = capabilities,
    };

    private static ClientCapabilities CreateLegacyGateOnlyCapabilities() =>
        JsonSerializer.Deserialize<ClientCapabilities>(
            """
            {
              "elicitation": {
                "form": {}
              },
              "extensions": {
                "io.modelcontextprotocol/ui": {
                  "mimeTypes": ["text/html;profile=mcp-app"]
                },
                "io.modelcontextprotocol/ui-elicitation": {
                  "requires": ["io.modelcontextprotocol/ui"]
                }
              }
            }
            """,
            McpJsonUtilities.DefaultOptions)!;

    private static ElicitResult CreateAcceptedResult() => new()
    {
        Action = "accept",
        Content = new Dictionary<string, JsonElement>
        {
            ["confirmed"] = JsonSerializer.SerializeToElement(true, McpJsonUtilities.DefaultOptions),
            ["selectedManagerId"] = JsonSerializer.SerializeToElement("mgr-priya", McpJsonUtilities.DefaultOptions),
        },
    };

    private static string GetText(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static ElicitRequestParams CreateRequest() => new()
    {
        Message = "Choose a manager.",
        RequestedSchema = new ElicitRequestParams.RequestSchema
        {
            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
            {
                ["confirmed"] = new ElicitRequestParams.BooleanSchema(),
                ["selectedManagerId"] = new ElicitRequestParams.StringSchema(),
            },
            Required = ["confirmed", "selectedManagerId"],
        },
    };
}
#endif
