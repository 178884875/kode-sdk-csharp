using KodaClaw.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

public static partial class GatewayApp
{
    private static void MapSkillsEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/skills");

        group.MapGet(string.Empty, async (
            HttpContext context,
            IConfiguration configuration,
            IWorkspaceService workspaceService,
            IDiagnosticsService diagnosticsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryAuthorize(context, configuration))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.auth",
                    eventType: "gateway.auth.failed",
                    level: "warning",
                    message: "Unauthorized access to skills list endpoint.");
                return Results.Unauthorized();
            }

            var paths = workspaceService.GetSkillsPaths();
            var appDir = Path.Combine(AppContext.BaseDirectory, "skills");
            var globalDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".agents",
                "skills");

            var items = new List<SkillDescriptor>();
            foreach (var searchPath in paths)
            {
                if (!Directory.Exists(searchPath))
                {
                    continue;
                }

                var source = string.Equals(searchPath, appDir, StringComparison.OrdinalIgnoreCase)
                    ? "built-in"
                    : string.Equals(searchPath, globalDir, StringComparison.OrdinalIgnoreCase)
                        ? "global"
                        : "workspace";

                foreach (var skillDir in Directory.GetDirectories(searchPath))
                {
                    var skillFile = Path.Combine(skillDir, "SKILL.md");
                    if (!File.Exists(skillFile))
                    {
                        continue;
                    }

                    var skillName = Path.GetFileName(skillDir);
                    string? description = null;

                    try
                    {
                        var lines = await File.ReadAllLinesAsync(skillFile, cancellationToken);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                            {
                                description = line["description:".Length..].Trim().Trim('"', '\'');
                                break;
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
                    {
                        // Non-critical — skip description if file cannot be read
                    }

                    var hasResources = Directory.GetFiles(skillDir).Length > 1;

                    items.Add(new SkillDescriptor(
                        Name: skillName,
                        Description: description,
                        Source: source,
                        Path: skillDir,
                        HasResources: hasResources));
                }
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.skills",
                eventType: "gateway.skills.listed",
                level: "info",
                message: $"Skills query returned {items.Count} items.",
                attributes: new Dictionary<string, string?>
                {
                    ["count"] = items.Count.ToString(),
                });

            return Results.Ok(items);
        });
    }

    private sealed record SkillDescriptor(
        string Name,
        string? Description,
        string Source,
        string Path,
        bool HasResources);
}
