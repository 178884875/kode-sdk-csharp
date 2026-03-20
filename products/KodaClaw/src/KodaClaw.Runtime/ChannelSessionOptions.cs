using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Infrastructure.Sandbox;

namespace KodaClaw.Runtime;

public sealed class ChannelSessionOptions
{
    public string Model { get; init; } = "koda-main";

    public string? SystemPrompt { get; init; } = "You are KodaClaw channel assistant.";

    public int MaxIterations { get; init; } = 8;

    public IReadOnlyList<string> Tools { get; init; } = MainSessionOptions.DefaultTools;

    public PermissionConfig Permissions { get; init; } = new()
    {
        Mode = "auto",
        RequireApprovalTools = MainSessionOptions.DefaultRequireApprovalTools,
    };
}
