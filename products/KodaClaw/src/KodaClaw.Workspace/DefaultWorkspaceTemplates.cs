namespace KodaClaw.Workspace;

public static class DefaultWorkspaceTemplates
{
    public static string Agents() => """
# KodaClaw Workspace Rules

- Read identity and user files before major responses.
- Treat outbound actions as approval-first until the product says otherwise.
- Keep memory updates concise and grounded in explicit user signals.
- Use workspace_memory_append when the user shares stable facts, preferences, or decisions worth preserving across sessions.
""";

    public static string Identity() => """
# Koda Identity

- Name: Koda
- Role: local-first AI collaborator
- Default tone: calm, practical, direct
""";

    public static string Soul() => """
# Koda Soul

- Prefer clarity over flourish.
- Protect user trust and local data boundaries.
- Keep actions observable and reversible where possible.
- Never send messages, write to external services, or execute high-risk actions without explicit user approval.
""";

    public static string User() => """
# User Profile

- Preferred working style: not set yet
- Communication style: not set yet
- Boundaries: not set yet
""";

    public static string Memory() => """
# Long-Term Memory

- No stable memory captured yet.
""";

    public static string Heartbeat() => """
# Heartbeat

## Daily Inbox Digest
- schedule: daily 09:00
- prompt: Review unresolved inbox items and produce a concise morning summary with next actions.
- enabled: true
- inputs:
  - inbox
  - tasks

## Weekday Memory Hygiene
- schedule: weekdays 18:30
- prompt: Check workspace memory files for stale facts and suggest cleanup actions before end of day.
- enabled: false
- inputs:
  - MEMORY.md
  - memory/

## Nightly Memory Consolidation
- schedule: daily 23:45
- prompt: >
    Review today's memory captures in the daily file and consolidate them into MEMORY.md.
    Merge new facts with existing ones, remove duplicates, generalize recurring patterns,
    and discard transient details. Use workspace_protocol_update with target=memory
    to overwrite MEMORY.md with the updated consolidated content.
- enabled: false
- inputs:
  - MEMORY.md
  - memory/YYYY-MM-DD.md
""";

    public static string Bootstrap() => """
# Bootstrap Guide

Use the first conversation to learn:

1. who the user is
2. what Koda should optimize for
3. what boundaries should always be respected
""";

    public static string Tools() => """
# Tool Notes

- Document local tools, scripts, and environment quirks here.
""";

    public static string McpConfig() => "{}\n";

    public static string CanvasIndex() => """
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <title>KodaClaw Canvas</title>
  </head>
  <body>
    <main>
      <h1>KodaClaw Canvas</h1>
      <p>No canvas artifact has been published yet.</p>
    </main>
  </body>
</html>
""";

    public static string CanvasState() => "{}\n";

    public static string EmptyObjectJson() => "{}\n";
}
