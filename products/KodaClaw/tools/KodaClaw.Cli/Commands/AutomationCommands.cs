using System.CommandLine;
using System.Text.Json.Serialization;

namespace KodaClaw.Cli.Commands;

public static class AutomationCommands
{
    public static Command Build()
    {
        var autoCmd = new Command("automation", "Automation management");
        autoCmd.AddCommand(BuildListCommand());
        autoCmd.AddCommand(BuildRunCommand());
        autoCmd.AddCommand(BuildEnableCommand());
        autoCmd.AddCommand(BuildDisableCommand());
        autoCmd.AddCommand(BuildRunsCommand());
        return autoCmd;
    }

    private static Command BuildListCommand()
    {
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var cmd = new Command("list", "List all automations") { jsonOpt };

        cmd.SetHandler(async (bool json) =>
        {
            using var client = new HttpGatewayClient(HttpGatewayClient.ResolveGatewayUrl(), HttpGatewayClient.ResolveToken());
            try
            {
                var result = await client.GetAsync<AutomationListResponse>("api/automations");
                var items = result?.Items ?? [];

                if (json)
                {
                    OutputFormatter.WriteJson(items.Select(a => new
                    {
                        id = a.Id,
                        name = a.Title,
                        cronExpression = a.CronExpression,
                        enabled = a.Enabled,
                        lastRunAt = a.LastRunAt,
                        nextRunAt = a.NextRunAt,
                        lastRunStatus = a.LastRunStatus
                    }));
                }
                else
                {
                    if (items.Count == 0) { Console.WriteLine("No automations found."); return; }
                    OutputFormatter.WriteTable(
                        items.Select(a => new[]
                        {
                            a.Id, a.Title, a.CronExpression ?? "-",
                            a.Enabled ? "enabled" : "disabled",
                            a.LastRunAt?.ToString("yyyy-MM-dd HH:mm") ?? "-"
                        }),
                        ["ID", "Name", "Cron", "Status", "LastRun"]);
                }
            }
            catch (HttpRequestException ex) { OutputFormatter.WriteError($"Gateway error: {ex.Message}"); Environment.Exit(1); }
        }, jsonOpt);

        return cmd;
    }

    private static Command BuildRunCommand()
    {
        var idArg = new Argument<string>("id", "Automation ID");
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var cmd = new Command("run", "Trigger an automation manually") { idArg, jsonOpt };

        cmd.SetHandler(async (string id, bool json) =>
        {
            using var client = new HttpGatewayClient(HttpGatewayClient.ResolveGatewayUrl(), HttpGatewayClient.ResolveToken());
            try
            {
                var result = await client.PostAsync<AutomationTriggerResponse>($"api/automations/{id}/trigger");
                if (json)
                    OutputFormatter.WriteJson(new { ok = true, automationId = id, sessionId = result?.SessionId });
                else
                    OutputFormatter.WriteSuccess($"Triggered '{id}'" + (result?.SessionId != null ? $"  (session: {result.SessionId})" : ""));
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            { OutputFormatter.WriteError($"Automation '{id}' not found"); Environment.Exit(3); }
            catch (HttpRequestException ex)
            { OutputFormatter.WriteError($"Gateway error: {ex.Message}"); Environment.Exit(1); }
        }, idArg, jsonOpt);

        return cmd;
    }

    private static Command BuildEnableCommand()
    {
        var idArg = new Argument<string>("id", "Automation ID");
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var cmd = new Command("enable", "Enable an automation") { idArg, jsonOpt };

        cmd.SetHandler(async (string id, bool json) =>
            await SetEnabledAsync(id, enabled: true, json), idArg, jsonOpt);

        return cmd;
    }

    private static Command BuildDisableCommand()
    {
        var idArg = new Argument<string>("id", "Automation ID");
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var cmd = new Command("disable", "Disable an automation") { idArg, jsonOpt };

        cmd.SetHandler(async (string id, bool json) =>
            await SetEnabledAsync(id, enabled: false, json), idArg, jsonOpt);

        return cmd;
    }

    private static async Task SetEnabledAsync(string id, bool enabled, bool json)
    {
        using var client = new HttpGatewayClient(HttpGatewayClient.ResolveGatewayUrl(), HttpGatewayClient.ResolveToken());
        try
        {
            await client.PatchAsync($"api/automations/{id}", new { enabled });
            if (json)
                OutputFormatter.WriteJson(new { ok = true, automationId = id, enabled });
            else
                OutputFormatter.WriteSuccess($"Automation '{id}' {(enabled ? "enabled" : "disabled")}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        { OutputFormatter.WriteError($"Automation '{id}' not found"); Environment.Exit(3); }
        catch (HttpRequestException ex)
        { OutputFormatter.WriteError($"Gateway error: {ex.Message}"); Environment.Exit(1); }
    }

    private static Command BuildRunsCommand()
    {
        var idArg = new Argument<string>("id", "Automation ID");
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var limitOpt = new Option<int>("--limit", () => 10, "Number of runs to show");
        var cmd = new Command("runs", "Show recent runs for an automation") { idArg, jsonOpt, limitOpt };

        cmd.SetHandler(async (string id, bool json, int limit) =>
        {
            using var client = new HttpGatewayClient(HttpGatewayClient.ResolveGatewayUrl(), HttpGatewayClient.ResolveToken());
            try
            {
                var result = await client.GetAsync<AutomationRunsResponse>($"api/automations/{id}/runs?limit={limit}");
                var runs = result?.Items ?? [];

                if (json)
                {
                    OutputFormatter.WriteJson(runs);
                }
                else
                {
                    if (runs.Count == 0) { Console.WriteLine($"No runs found for '{id}'."); return; }
                    OutputFormatter.WriteTable(
                        runs.Select(r => new[]
                        {
                            r.RunId[..Math.Min(8, r.RunId.Length)],
                            r.Status,
                            r.StartedAt.LocalDateTime.ToString("MM-dd HH:mm"),
                            r.CompletedAt.HasValue ? $"{(r.CompletedAt.Value - r.StartedAt).TotalSeconds:F0}s" : "-",
                            r.Summary ?? r.ErrorMessage ?? ""
                        }),
                        ["Run", "Status", "Started", "Duration", "Summary"]);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            { OutputFormatter.WriteError($"Automation '{id}' not found"); Environment.Exit(3); }
            catch (HttpRequestException ex)
            { OutputFormatter.WriteError($"Gateway error: {ex.Message}"); Environment.Exit(1); }
        }, idArg, jsonOpt, limitOpt);

        return cmd;
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private record AutomationListResponse
    {
        [JsonPropertyName("items")] public IReadOnlyList<AutomationItem> Items { get; init; } = [];
    }

    private record AutomationItem
    {
        [JsonPropertyName("id")] public string Id { get; init; } = "";
        [JsonPropertyName("title")] public string Title { get; init; } = "";
        [JsonPropertyName("cronExpression")] public string? CronExpression { get; init; }
        [JsonPropertyName("enabled")] public bool Enabled { get; init; }
        [JsonPropertyName("lastRunAt")] public DateTimeOffset? LastRunAt { get; init; }
        [JsonPropertyName("nextRunAt")] public DateTimeOffset? NextRunAt { get; init; }
        [JsonPropertyName("lastRunStatus")] public string? LastRunStatus { get; init; }
    }

    private record AutomationTriggerResponse
    {
        [JsonPropertyName("sessionId")] public string? SessionId { get; init; }
    }

    private record AutomationRunsResponse
    {
        [JsonPropertyName("items")] public IReadOnlyList<AutomationRunItem> Items { get; init; } = [];
    }

    private record AutomationRunItem
    {
        [JsonPropertyName("runId")] public string RunId { get; init; } = "";
        [JsonPropertyName("status")] public string Status { get; init; } = "";
        [JsonPropertyName("trigger")] public string Trigger { get; init; } = "";
        [JsonPropertyName("startedAt")] public DateTimeOffset StartedAt { get; init; }
        [JsonPropertyName("completedAt")] public DateTimeOffset? CompletedAt { get; init; }
        [JsonPropertyName("summary")] public string? Summary { get; init; }
        [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; init; }
    }
}
