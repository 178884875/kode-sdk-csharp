using System.CommandLine;
using System.Text.Json.Serialization;

namespace KodaClaw.Cli.Commands;

public static class InboxCommands
{
    public static Command Build()
    {
        var cmd = new Command("inbox", "Inbox and approval management");
        cmd.AddCommand(BuildListCommand());
        cmd.AddCommand(BuildApproveCommand());
        cmd.AddCommand(BuildRejectCommand());
        return cmd;
    }

    private static Command BuildListCommand()
    {
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var statusOpt = new Option<string?>("--status", "Filter by status: Open, Acknowledged, Resolved, Archived");
        var cmd = new Command("list", "List inbox items") { jsonOpt, statusOpt };

        cmd.SetHandler(async (bool json, string? status) =>
        {
            using var client = new HttpGatewayClient(HttpGatewayClient.ResolveGatewayUrl(), HttpGatewayClient.ResolveToken());
            try
            {
                var query = status != null ? $"?status={status}" : "?status=Open";
                var result = await client.GetAsync<InboxListResponse>($"api/inbox{query}");
                var items = result?.Items ?? [];

                if (json)
                {
                    OutputFormatter.WriteJson(items);
                }
                else
                {
                    if (items.Count == 0) { Console.WriteLine("No inbox items."); return; }
                    OutputFormatter.WriteTable(
                        items.Select(i => new[]
                        {
                            i.Id[..Math.Min(8, i.Id.Length)],
                            i.Kind,
                            i.RequiresAction ? "⚡ " + i.Title : i.Title,
                            i.Status,
                            i.CreatedAt.LocalDateTime.ToString("MM-dd HH:mm")
                        }),
                        ["ID", "Kind", "Title", "Status", "Created"]);
                }
            }
            catch (HttpRequestException ex)
            { OutputFormatter.WriteError($"Gateway error: {ex.Message}"); Environment.Exit(1); }
        }, jsonOpt, statusOpt);

        return cmd;
    }

    private static Command BuildApproveCommand()
    {
        var idArg = new Argument<string>("id", "Inbox item ID");
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var cmd = new Command("approve", "Approve an inbox item") { idArg, jsonOpt };

        cmd.SetHandler(async (string id, bool json) =>
            await UpdateStatusAsync(id, "Resolved", json, "approved"), idArg, jsonOpt);

        return cmd;
    }

    private static Command BuildRejectCommand()
    {
        var idArg = new Argument<string>("id", "Inbox item ID");
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var cmd = new Command("reject", "Reject/archive an inbox item") { idArg, jsonOpt };

        cmd.SetHandler(async (string id, bool json) =>
            await UpdateStatusAsync(id, "Archived", json, "rejected"), idArg, jsonOpt);

        return cmd;
    }

    private static async Task UpdateStatusAsync(string id, string status, bool json, string verb)
    {
        using var client = new HttpGatewayClient(HttpGatewayClient.ResolveGatewayUrl(), HttpGatewayClient.ResolveToken());
        try
        {
            await client.PatchAsync($"api/inbox/{id}/status", new { status });
            if (json)
                OutputFormatter.WriteJson(new { ok = true, id, status });
            else
                OutputFormatter.WriteSuccess($"Inbox item '{id}' {verb}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        { OutputFormatter.WriteError($"Inbox item '{id}' not found"); Environment.Exit(3); }
        catch (HttpRequestException ex)
        { OutputFormatter.WriteError($"Gateway error: {ex.Message}"); Environment.Exit(1); }
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private record InboxListResponse
    {
        [JsonPropertyName("items")] public IReadOnlyList<InboxItem> Items { get; init; } = [];
    }

    private record InboxItem
    {
        [JsonPropertyName("id")] public string Id { get; init; } = "";
        [JsonPropertyName("kind")] public string Kind { get; init; } = "";
        [JsonPropertyName("status")] public string Status { get; init; } = "";
        [JsonPropertyName("title")] public string Title { get; init; } = "";
        [JsonPropertyName("summary")] public string Summary { get; init; } = "";
        [JsonPropertyName("requiresAction")] public bool RequiresAction { get; init; }
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    }
}
