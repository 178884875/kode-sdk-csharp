using Kode.Agent.Sdk.Core.Types;

namespace KodaClaw.Runtime;

public sealed class AutomationSessionOptions
{
    public string Model { get; init; } = "koda-main";

    public string? SystemPrompt { get; init; } = "You are KodaClaw automation assistant.";

    public int MaxIterations { get; init; } = 8;

    public int MaxPromptCharacters { get; init; } = 16000;

    public IReadOnlyList<string> Tools { get; init; } = MainSessionOptions.DefaultTools;

    // Automation sessions run headless; no mid-turn tool approval gates.
    public PermissionConfig Permissions { get; init; } = new()
    {
        Mode = "auto",
        RequireApprovalTools = [],
    };
}
