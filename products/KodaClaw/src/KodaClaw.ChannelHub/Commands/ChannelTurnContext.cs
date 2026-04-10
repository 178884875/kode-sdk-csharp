namespace KodaClaw.ChannelHub.Commands;

/// <summary>
/// Carries per-turn modifier state derived from <see cref="ChannelDirectiveKind"/> directives.
/// Passed from the Orchestrator to the turn execution path to configure how the agent run behaves.
/// </summary>
public sealed record ChannelTurnContext(
    string? PromptPrefix,
    bool? EnableThinking,
    int? ThinkingBudget,
    bool? EnableProgressStreamingOverride,
    string? FocusConstraint)
{
    /// <summary>An empty context — no directives applied.</summary>
    public static readonly ChannelTurnContext Empty = new(null, null, null, null, null);

    /// <summary>
    /// Builds a <see cref="ChannelTurnContext"/> from the directives in <paramref name="parsed"/>.
    /// </summary>
    public static ChannelTurnContext FromDirectives(ParsedChannelCommand parsed)
    {
        bool? enableThinking = null;
        int? thinkingBudget = null;
        bool? streamOverride = null;
        string? focusConstraint = null;

        foreach (var directive in parsed.Directives)
        {
            switch (directive)
            {
                case ChannelDirectiveKind.Think:
                    enableThinking = true;
                    thinkingBudget = 8000;
                    break;
                case ChannelDirectiveKind.Stream:
                    streamOverride = true;
                    break;
                case ChannelDirectiveKind.Quiet:
                    streamOverride = false;
                    break;
                case ChannelDirectiveKind.Focus:
                    focusConstraint = parsed.DirectiveArg;
                    break;
            }
        }

        return new ChannelTurnContext(
            PromptPrefix: null,
            EnableThinking: enableThinking,
            ThinkingBudget: thinkingBudget,
            EnableProgressStreamingOverride: streamOverride,
            FocusConstraint: focusConstraint);
    }
}
