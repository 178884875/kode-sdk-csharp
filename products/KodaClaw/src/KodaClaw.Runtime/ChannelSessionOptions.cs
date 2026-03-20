using Kode.Agent.Sdk.Core.Types;

namespace KodaClaw.Runtime;

public sealed class ChannelSessionOptions
{
    public string Model { get; init; } = "koda-main";

    public string? SystemPrompt { get; init; } = "You are KodaClaw channel assistant.";

    public int MaxIterations { get; init; } = 8;

    public int MaxPromptCharacters { get; init; } = 16000;

    public IReadOnlyList<string> Tools { get; init; } = MainSessionOptions.DefaultTools;

    // Channel sessions never pause for mid-turn tool approval.
    // The only approval gate is the post-turn Channel Delivery Approval.
    public PermissionConfig Permissions { get; init; } = new()
    {
        Mode = "auto",
        RequireApprovalTools = [],
    };
}
