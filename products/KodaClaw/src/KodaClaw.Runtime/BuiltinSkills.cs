namespace KodaClaw.Runtime;

/// <summary>
/// Built-in skill name constants and per-session-type auto-activation lists.
/// Lives in KodaClaw.Runtime (session config behavior, not public API contract).
/// </summary>
public static class BuiltinSkills
{
    public const string KodaWorkspace = "koda-workspace";
    public const string KodaMemory = "koda-memory";
    public const string KodaChannels = "koda-channels";
    public const string KodaAutomation = "koda-automation";
    public const string KodaCanvas = "koda-canvas";

    /// <summary>Main chat sessions: workspace protocol + memory management.</summary>
    public static readonly IReadOnlyList<string> ChatAutoActivate =
        [KodaWorkspace, KodaMemory];

    /// <summary>Channel sessions: workspace protocol + channel send tool guide.</summary>
    public static readonly IReadOnlyList<string> ChannelAutoActivate =
        [KodaWorkspace, KodaChannels];

    /// <summary>Automation sessions: workspace protocol + HEARTBEAT.md syntax.</summary>
    public static readonly IReadOnlyList<string> AutomationAutoActivate =
        [KodaWorkspace, KodaAutomation];

    /// <summary>
    /// Tools hidden from model schema by default; revealed only when their owning skill activates.
    /// canvas_upsert → koda-canvas
    /// channel_send / channel_list → koda-channels
    /// </summary>
    public static readonly IReadOnlyList<string> SkillGatedTools =
        ["canvas_upsert", "channel_send", "channel_list"];
}
