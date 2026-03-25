using System.CommandLine;
using System.Text.Json.Serialization;

namespace KodaClaw.Cli.Commands;

public static class WorkspaceCommands
{
    public static Command Build()
    {
        var wsCmd = new Command("workspace", "Workspace management");
        wsCmd.AddCommand(BuildStatusCommand());
        wsCmd.AddCommand(BuildReadCommand());
        wsCmd.AddCommand(BuildWriteCommand());
        return wsCmd;
    }

    private static Command BuildStatusCommand()
    {
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var cmd = new Command("status", "Show workspace readiness") { jsonOpt };

        cmd.SetHandler(async (bool json) =>
        {
            using var client = new HttpGatewayClient(HttpGatewayClient.ResolveGatewayUrl(), HttpGatewayClient.ResolveToken());
            try
            {
                var result = await client.GetAsync<WorkspaceReadinessResponse>("api/workspace/readiness");
                if (result == null) { OutputFormatter.WriteError("Empty response from Gateway"); Environment.Exit(1); return; }

                if (json)
                {
                    OutputFormatter.WriteJson(result);
                }
                else
                {
                    Console.WriteLine($"Identity {(result.IsIdentitySet ? "✓" : "✗")}  Soul {(result.IsSoulSet ? "✓" : "✗")}  User {(result.IsUserSet ? "✓" : "✗")}");
                    if (result.HasAnyGap)
                        Console.WriteLine("  → Run setup to configure your workspace identity");
                }
            }
            catch (HttpRequestException ex) { OutputFormatter.WriteError($"Gateway error: {ex.Message}"); Environment.Exit(1); }
        }, jsonOpt);

        return cmd;
    }

    private static Command BuildReadCommand()
    {
        var targetArg = new Argument<string>("target", "File target: identity, soul, user, memory, heartbeat");
        var cmd = new Command("read", "Read a workspace file") { targetArg };

        cmd.SetHandler(async (string target) =>
        {
            using var client = new HttpGatewayClient(HttpGatewayClient.ResolveGatewayUrl(), HttpGatewayClient.ResolveToken());
            try
            {
                var result = await client.GetAsync<WorkspaceFileResponse>($"api/workspace/file?target={target}");
                if (result?.Content != null)
                    Console.Write(result.Content);
                else
                    OutputFormatter.WriteError("No content returned");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            { OutputFormatter.WriteError($"Workspace file '{target}' not found"); Environment.Exit(3); }
            catch (HttpRequestException ex)
            { OutputFormatter.WriteError($"Gateway error: {ex.Message}"); Environment.Exit(1); }
        }, targetArg);

        return cmd;
    }

    private static Command BuildWriteCommand()
    {
        var targetArg = new Argument<string>("target", "File target: identity, soul, user, memory, heartbeat");
        var contentOpt = new Option<string?>("--content", "Content to write (or pipe via stdin)");
        var cmd = new Command("write", "Write a workspace file") { targetArg, contentOpt };

        cmd.SetHandler(async (string target, string? content) =>
        {
            if (content == null && Console.IsInputRedirected)
                content = await Console.In.ReadToEndAsync();

            if (string.IsNullOrEmpty(content))
            {
                OutputFormatter.WriteError("No content provided. Use --content or pipe via stdin.");
                Environment.Exit(1);
                return;
            }

            using var client = new HttpGatewayClient(HttpGatewayClient.ResolveGatewayUrl(), HttpGatewayClient.ResolveToken());
            try
            {
                await client.PutAsync($"api/workspace/file", new { target, content });
                OutputFormatter.WriteSuccess($"Workspace '{target}' updated");
            }
            catch (HttpRequestException ex)
            { OutputFormatter.WriteError($"Gateway error: {ex.Message}"); Environment.Exit(1); }
        }, targetArg, contentOpt);

        return cmd;
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private record WorkspaceReadinessResponse
    {
        [JsonPropertyName("isIdentitySet")] public bool IsIdentitySet { get; init; }
        [JsonPropertyName("isSoulSet")] public bool IsSoulSet { get; init; }
        [JsonPropertyName("isUserSet")] public bool IsUserSet { get; init; }
        [JsonPropertyName("hasAnyGap")] public bool HasAnyGap { get; init; }
    }

    private record WorkspaceFileResponse
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
    }
}
