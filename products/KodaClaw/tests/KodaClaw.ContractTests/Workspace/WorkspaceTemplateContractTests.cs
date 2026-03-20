using FluentAssertions;
using KodaClaw.Workspace;
using Xunit;

namespace KodaClaw.ContractTests.Workspace;

/// <summary>
/// Verifies that DefaultWorkspaceTemplates contain the correct tool references
/// so that workspace automations call the right tools at runtime.
/// </summary>
public sealed class WorkspaceTemplateContractTests
{
    // ── Heartbeat: Nightly Memory Consolidation ──────────────────────────────

    [Fact]
    public void Heartbeat_nightly_consolidation_references_workspace_protocol_update()
    {
        var heartbeat = DefaultWorkspaceTemplates.Heartbeat();
        heartbeat.Should().Contain("workspace_protocol_update",
            because: "nightly consolidation must use workspace_protocol_update to overwrite MEMORY.md");
    }

    [Fact]
    public void Heartbeat_nightly_consolidation_specifies_target_memory()
    {
        var heartbeat = DefaultWorkspaceTemplates.Heartbeat();
        heartbeat.Should().Contain("target=memory",
            because: "nightly consolidation targets the memory protocol file");
    }

    [Fact]
    public void Heartbeat_nightly_consolidation_does_not_use_workspace_memory_append_to_write_memory_md()
    {
        var heartbeat = DefaultWorkspaceTemplates.Heartbeat();

        // workspace_memory_append is only for daily log files, not for MEMORY.md overwrites.
        // The template must not instruct the Agent to use workspace_memory_append on MEMORY.md.
        var consolidationSection = ExtractSection(heartbeat, "Nightly Memory Consolidation");
        consolidationSection.Should().NotContain("workspace_memory_append",
            because: "workspace_memory_append appends to daily logs, not overwrites MEMORY.md");
    }

    [Fact]
    public void Heartbeat_nightly_consolidation_is_disabled_by_default()
    {
        var consolidationSection = ExtractSection(DefaultWorkspaceTemplates.Heartbeat(), "Nightly Memory Consolidation");
        consolidationSection.Should().Contain("enabled: false",
            because: "nightly consolidation must be opt-in to avoid unexpected LLM calls for unconfigured users");
    }

    [Fact]
    public void Heartbeat_nightly_consolidation_lists_required_inputs()
    {
        var consolidationSection = ExtractSection(DefaultWorkspaceTemplates.Heartbeat(), "Nightly Memory Consolidation");
        consolidationSection.Should().Contain("MEMORY.md");
        consolidationSection.Should().Contain("memory/YYYY-MM-DD.md");
    }

    // ── Heartbeat: Weekday Memory Hygiene ────────────────────────────────────

    [Fact]
    public void Heartbeat_weekday_memory_hygiene_uses_current_memory_paths()
    {
        var hygieneSection = ExtractSection(DefaultWorkspaceTemplates.Heartbeat(), "Weekday Memory Hygiene");
        hygieneSection.Should().Contain("MEMORY.md",
            because: "hygiene task reads from the consolidated memory file");
        hygieneSection.Should().NotContain("memory/facts",
            because: "memory/facts is an old directory structure no longer in use");
        hygieneSection.Should().NotContain("memory/conversations",
            because: "memory/conversations is an old directory structure no longer in use");
    }

    // ── Agents: workspace_memory_append guidance ─────────────────────────────

    [Fact]
    public void Agents_template_contains_workspace_memory_append_guidance()
    {
        var agents = DefaultWorkspaceTemplates.Agents();
        agents.Should().Contain("workspace_memory_append",
            because: "AGENTS.md must guide the Agent when to record memories during a session");
    }

    // ── Soul: approval-first principle ───────────────────────────────────────

    [Fact]
    public void Soul_template_contains_outbound_approval_principle()
    {
        var soul = DefaultWorkspaceTemplates.Soul();
        soul.Should().Contain("approval",
            because: "SOUL.md must encode the product-level constraint against unsanctioned outbound actions");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ExtractSection(string markdown, string sectionName)
    {
        var heading = $"## {sectionName}";
        var start = markdown.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;

        var nextSection = markdown.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return nextSection >= 0
            ? markdown[start..nextSection]
            : markdown[start..];
    }
}
