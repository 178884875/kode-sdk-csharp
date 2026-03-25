using System.CommandLine;
using System.Text.Json.Serialization;

namespace KodaClaw.Cli.Commands;

public static class AuthCommands
{
    public static Command Build()
    {
        var authCmd = new Command("auth", "Authentication and Gateway connectivity");
        authCmd.AddCommand(BuildLoginCommand());
        authCmd.AddCommand(BuildStatusCommand());
        return authCmd;
    }

    private static Command BuildLoginCommand()
    {
        var tokenOpt = new Option<string>("--token", "Gateway API token") { IsRequired = true };
        var urlOpt = new Option<string>("--url", () => "http://127.0.0.1:5076", "Gateway URL");
        var cmd = new Command("login", "Save Gateway token to ~/.kc-cli/config.json") { tokenOpt, urlOpt };

        cmd.SetHandler(async (string token, string url) =>
        {
            using var client = new HttpGatewayClient(url, token);
            try
            {
                var result = await client.GetAsync<SystemHealthResponse>("api/system/health");
                if (result?.Status != "healthy")
                {
                    OutputFormatter.WriteError("Gateway responded but status is not healthy.");
                    Environment.Exit(1);
                    return;
                }
                KcConfig.Save(token, url);
                OutputFormatter.WriteSuccess($"Logged in. Config saved to ~/.kc-cli/config.json  ({url})");
            }
            catch (HttpRequestException)
            {
                OutputFormatter.WriteError($"Cannot connect to Gateway at {url}. Is KodaClaw running?");
                Environment.Exit(1);
            }
        }, tokenOpt, urlOpt);

        return cmd;
    }

    private static Command BuildStatusCommand()
    {
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var cmd = new Command("status", "Check Gateway connection status") { jsonOpt };

        cmd.SetHandler(async (bool json) =>
        {
            var url = HttpGatewayClient.ResolveGatewayUrl();
            using var client = new HttpGatewayClient(url, HttpGatewayClient.ResolveToken());
            try
            {
                var result = await client.GetAsync<SystemHealthResponse>("api/system/health");
                var output = new AuthStatusOutput
                {
                    Authenticated = result?.Status == "healthy",
                    GatewayUrl = url,
                    Version = result?.Name ?? "unknown"
                };

                if (json)
                    OutputFormatter.WriteJson(output);
                else
                    OutputFormatter.WriteSuccess($"Connected to {url}  (version: {output.Version})");
            }
            catch (HttpRequestException)
            {
                if (json)
                    OutputFormatter.WriteJson(new { authenticated = false, gatewayUrl = url, error = "Connection refused" });
                else
                    OutputFormatter.WriteError($"Cannot connect to Gateway at {url}. Is KodaClaw running?");
                Environment.Exit(1);
            }
        }, jsonOpt);

        return cmd;
    }

    private record AuthStatusOutput
    {
        [JsonPropertyName("authenticated")] public bool Authenticated { get; init; }
        [JsonPropertyName("gatewayUrl")] public string GatewayUrl { get; init; } = "";
        [JsonPropertyName("version")] public string Version { get; init; } = "";
    }

    private record SystemHealthResponse
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("status")] public string? Status { get; init; }
        [JsonPropertyName("mode")] public string? Mode { get; init; }
    }
}
