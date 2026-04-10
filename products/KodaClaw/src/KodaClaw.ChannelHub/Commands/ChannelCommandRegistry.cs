namespace KodaClaw.ChannelHub.Commands;

/// <summary>
/// Describes a single registered channel command (control or directive).
/// </summary>
public sealed record ChannelCommandDefinition(
    string Key,
    IReadOnlyList<string> Aliases,
    string Description,
    string Category,
    ChannelControlCommandKind? ControlKind = null,
    ChannelDirectiveKind? DirectiveKind = null);

/// <summary>
/// Static registry of all channel commands and directives.
/// All waves' commands are defined here upfront; the parser/dispatcher are extended wave-by-wave.
/// </summary>
public static class ChannelCommandRegistry
{
    public static readonly IReadOnlyList<ChannelCommandDefinition> All = new List<ChannelCommandDefinition>
    {
        // ── Control commands ─────────────────────────────────────────────────────
        new(
            Key: "new-session",
            Aliases: ["/new", "/clear", "/reset"],
            Description: "开启新会话（清除当前对话历史）；支持可选模型参数：/new <序号> 或 /new <模型ID>",
            Category: "session",
            ControlKind: ChannelControlCommandKind.NewSession),

        new(
            Key: "model",
            Aliases: ["/model", "/models"],
            Description: "查看当前模型或列出所有可用模型（/model list）",
            Category: "session",
            ControlKind: ChannelControlCommandKind.Model),

        new(
            Key: "status",
            Aliases: ["/status", "/s"],
            Description: "查询当前会话状态",
            Category: "session",
            ControlKind: ChannelControlCommandKind.Status),

        new(
            Key: "stop",
            Aliases: ["/stop"],
            Description: "中断当前正在执行的 Agent 轮次",
            Category: "session",
            ControlKind: ChannelControlCommandKind.Stop),

        new(
            Key: "help",
            Aliases: ["/help", "/commands", "/?"],
            Description: "列出所有可用命令",
            Category: "info",
            ControlKind: ChannelControlCommandKind.Help),

        new(
            Key: "compact",
            Aliases: ["/compact"],
            Description: "立即压缩当前会话的上下文窗口",
            Category: "session",
            ControlKind: ChannelControlCommandKind.Compact),

        new(
            Key: "tools",
            Aliases: ["/tools"],
            Description: "列出当前会话可用的工具",
            Category: "info",
            ControlKind: ChannelControlCommandKind.Tools),

        new(
            Key: "whoami",
            Aliases: ["/whoami", "/me"],
            Description: "查看当前 Agent 的身份信息",
            Category: "info",
            ControlKind: ChannelControlCommandKind.WhoAmI),

        new(
            Key: "btw",
            Aliases: ["/btw"],
            Description: "发送一次性旁路问题（不影响主会话上下文）",
            Category: "meta",
            ControlKind: ChannelControlCommandKind.SideQuestion),

        // ── Directive modifiers ──────────────────────────────────────────────────
        new(
            Key: "think",
            Aliases: ["/think"],
            Description: "启用扩展思考模式（Thinking Budget）",
            Category: "modifier",
            DirectiveKind: ChannelDirectiveKind.Think),

        new(
            Key: "stream",
            Aliases: ["/stream"],
            Description: "为本次对话启用流式进度输出",
            Category: "modifier",
            DirectiveKind: ChannelDirectiveKind.Stream),

        new(
            Key: "quiet",
            Aliases: ["/quiet"],
            Description: "为本次对话禁用流式进度输出，只回复最终结果",
            Category: "modifier",
            DirectiveKind: ChannelDirectiveKind.Quiet),

        new(
            Key: "focus",
            Aliases: ["/focus"],
            Description: "为本次对话追加主题约束（/focus <主题> <正文>）",
            Category: "modifier",
            DirectiveKind: ChannelDirectiveKind.Focus),
    };

    // ── Lookup maps built once at startup ───────────────────────────────────────

    /// <summary>Maps every alias (lowercased) to its definition.</summary>
    private static readonly Dictionary<string, ChannelCommandDefinition> _byAlias =
        All.SelectMany(def => def.Aliases.Select(a => (Alias: a.ToLowerInvariant(), Def: def)))
           .ToDictionary(t => t.Alias, t => t.Def, StringComparer.Ordinal);

    /// <summary>
    /// Looks up a command definition by any of its aliases (case-insensitive).
    /// Returns null when the token is unrecognised.
    /// </summary>
    public static ChannelCommandDefinition? Find(string alias)
        => _byAlias.TryGetValue(alias.ToLowerInvariant(), out var def) ? def : null;

    /// <summary>Returns all control command definitions for a given category.</summary>
    public static IEnumerable<ChannelCommandDefinition> ByCategory(string category)
        => All.Where(d => string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase));
}
