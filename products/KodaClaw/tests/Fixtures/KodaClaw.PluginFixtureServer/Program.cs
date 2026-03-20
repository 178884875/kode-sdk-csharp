using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KodaClaw.PluginFixtureServer;

internal static class Program
{
    // If set to a truthy value (1/true/yes), health_ping returns IsError=true.
    private const string FailHealthPingEnvVar = "KODACLAW_FIXTURE_HEALTH_PING_FAIL";

    // Alternate env var name to keep the fixture generic/reusable across products.
    private const string FailHealthPingAltEnvVar = "MCP_FIXTURE_HEALTH_PING_FAIL";

    private const string ToolEcho = "echo";
    private const string ToolHealthPing = "health_ping";

    public static async Task<int> Main(string[] args)
    {
        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            PrintHelp();
            return 0;
        }

        var failHealthPing = ShouldFailHealthPing(args);

        // IMPORTANT:
        // - MCP stdio uses stdout for protocol messages.
        // - Do not write to stdout from this process; use stderr for any diagnostics.

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        var serverName = "KodaClaw.PluginFixtureServer";

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = serverName, Version = version },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
                {
                    Tools = [CreateEchoTool(), CreateHealthPingTool()],
                }),

                CallToolHandler = (request, cancellationToken) =>
                {
                    _ = cancellationToken;

                    var @params = request.Params ?? throw new McpProtocolException(
                        "Missing required field: params",
                        McpErrorCode.InvalidParams);

                    var name = @params.Name;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        throw new McpProtocolException("Missing required field: params.name", McpErrorCode.InvalidParams);
                    }

                    switch (name)
                    {
                        case ToolEcho:
                        {
                            var message = GetRequiredStringArgument(@params, "message");
                            return ValueTask.FromResult(new CallToolResult
                            {
                                Content = [new TextContentBlock { Text = message }],
                                IsError = false,
                            });
                        }

                        case ToolHealthPing:
                        {
                            if (failHealthPing)
                            {
                                return ValueTask.FromResult(new CallToolResult
                                {
                                    Content =
                                    [
                                        new TextContentBlock
                                        {
                                            Text = "health_ping: FAILED (fixture is configured as degraded)"
                                        }
                                    ],
                                    IsError = true,
                                });
                            }

                            return ValueTask.FromResult(new CallToolResult
                            {
                                Content = [new TextContentBlock { Text = "ok" }],
                                IsError = false,
                            });
                        }

                        default:
                            throw new McpProtocolException($"Unknown tool: '{name}'", McpErrorCode.InvalidRequest);
                    }
                },
            }
        };

        await using var server = McpServer.Create(new StdioServerTransport(serverName), options);
        await server.RunAsync();
        return 0;
    }

    private static string GetRequiredStringArgument(CallToolRequestParams @params, string argumentName)
    {
        if (@params.Arguments is null || !@params.Arguments.TryGetValue(argumentName, out var raw))
        {
            throw new McpProtocolException(
                $"Missing required argument '{argumentName}'",
                McpErrorCode.InvalidParams);
        }

        // In this SDK version, tool arguments are represented as JsonElement values.
        // Normalize into a string for convenience.
        if (raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new McpProtocolException(
                $"Missing required argument '{argumentName}'",
                McpErrorCode.InvalidParams);
        }

        return raw.ValueKind == JsonValueKind.String
            ? raw.GetString() ?? string.Empty
            : raw.ToString();
    }

    private static bool ShouldFailHealthPing(string[] args)
    {
        var env = Environment.GetEnvironmentVariable(FailHealthPingEnvVar)
                  ?? Environment.GetEnvironmentVariable(FailHealthPingAltEnvVar);

        if (IsTruthy(env))
        {
            return true;
        }

        // CLI flag for tests: `--fail-health-ping` (aliases supported).
        return args.Any(a =>
            a.Equals("--fail-health-ping", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--health-fail", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--degraded", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim() switch
        {
            "1" => true,
            "true" => true,
            "TRUE" => true,
            "yes" => true,
            "YES" => true,
            "on" => true,
            "ON" => true,
            _ => false,
        };
    }

    private static Tool CreateEchoTool() => new()
    {
        Name = ToolEcho,
        Description = "Echoes the provided message back to the caller.",
        InputSchema = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "type": "object",
              "properties": {
                "message": { "type": "string", "description": "Message to echo back" }
              },
              "required": ["message"]
            }
            """),
    };

    private static Tool CreateHealthPingTool() => new()
    {
        Name = ToolHealthPing,
        Description = "Returns ok when healthy; can be configured to fail for degraded tests.",
        InputSchema = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "type": "object",
              "properties": {}
            }
            """),
    };

    private static void PrintHelp()
    {
        Console.Error.WriteLine(
            """
            KodaClaw.PluginFixtureServer - MCP stdio fixture server (for integration tests)

            This process speaks MCP over stdio:
              - stdout: MCP protocol messages (do not print anything else)
              - stderr: diagnostics/logging

            Tools:
              - echo(message: string) -> text
              - health_ping() -> "ok" (or error when degraded)

            Degraded mode:
              - Set env var KODACLAW_FIXTURE_HEALTH_PING_FAIL=1 (or MCP_FIXTURE_HEALTH_PING_FAIL=1)
              - Or pass CLI flag --fail-health-ping (aliases: --health-fail, --degraded)

            Examples:
              dotnet run --project products/KodaClaw/tests/Fixtures/KodaClaw.PluginFixtureServer/KodaClaw.PluginFixtureServer.csproj
              KODACLAW_FIXTURE_HEALTH_PING_FAIL=1 dotnet run --project products/KodaClaw/tests/Fixtures/KodaClaw.PluginFixtureServer/KodaClaw.PluginFixtureServer.csproj
              dotnet run --project products/KodaClaw/tests/Fixtures/KodaClaw.PluginFixtureServer/KodaClaw.PluginFixtureServer.csproj -- --fail-health-ping
            """);
    }
}
