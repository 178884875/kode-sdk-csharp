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

    public double ContextCompressionTriggerRatio { get; init; } = 0.75;

    public double ContextCompressionTargetRatio { get; init; } = 0.40;

    public int DefaultContextWindowSize { get; init; } = 128_000;

    /// <summary>
    /// Number of days of inactivity after which a channel session is reset instead of resumed.
    /// Set to 0 to disable timeout-based reset.
    /// </summary>
    public int SessionTimeoutDays { get; init; } = 7;

    /// <summary>
    /// Number of lines in SUMMARY.md that triggers LLM compression of the oldest portion.
    /// </summary>
    public int SummaryCompressionThreshold { get; init; } = 80;

    /// <summary>
    /// Target number of recent lines to retain after LLM compression.
    /// </summary>
    public int SummaryCompressionTargetLines { get; init; } = 40;
}
