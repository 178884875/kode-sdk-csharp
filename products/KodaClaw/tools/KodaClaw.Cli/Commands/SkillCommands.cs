using System.CommandLine;
using System.Text.Json.Serialization;

namespace KodaClaw.Cli.Commands;

public static class SkillCommands
{
    public static Command Build()
    {
        var cmd = new Command("skill", "Skills management");
        cmd.AddCommand(BuildListCommand());
        return cmd;
    }

    private static Command BuildListCommand()
    {
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var cmd = new Command("list", "List available skills") { jsonOpt };

        cmd.SetHandler(async (bool json) =>
        {
            using var client = new HttpGatewayClient(HttpGatewayClient.ResolveGatewayUrl(), HttpGatewayClient.ResolveToken());
            try
            {
                var skills = await client.GetAsync<IReadOnlyList<SkillItem>>("api/skills");
                skills ??= [];

                if (json)
                {
                    OutputFormatter.WriteJson(skills);
                }
                else
                {
                    if (skills.Count == 0) { Console.WriteLine("No skills found."); return; }
                    OutputFormatter.WriteTable(
                        skills.Select(s => new[]
                        {
                            s.Name,
                            s.Kind,
                            s.Version ?? "-",
                            s.AllowedTools.Count > 0 ? string.Join(" ", s.AllowedTools) : "-"
                        }),
                        ["Name", "Kind", "Version", "AllowedTools"]);
                }
            }
            catch (HttpRequestException ex)
            { OutputFormatter.WriteError($"Gateway error: {ex.Message}"); Environment.Exit(1); }
        }, jsonOpt);

        return cmd;
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private record SkillItem
    {
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("kind")] public string Kind { get; init; } = "";
        [JsonPropertyName("version")] public string? Version { get; init; }
        [JsonPropertyName("compatibility")] public string? Compatibility { get; init; }
        [JsonPropertyName("allowedTools")] public IReadOnlyList<string> AllowedTools { get; init; } = [];
        [JsonPropertyName("tags")] public IReadOnlyList<string> Tags { get; init; } = [];
    }
}
