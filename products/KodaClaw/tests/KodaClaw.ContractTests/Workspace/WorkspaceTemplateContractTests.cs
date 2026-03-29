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
    public void Heartbeat_nightly_consolidation_is_enabled_by_default()
    {
        var consolidationSection = ExtractSection(DefaultWorkspaceTemplates.Heartbeat(), "Nightly Memory Consolidation");
        consolidationSection.Should().Contain("enabled: true",
            because: "nightly consolidation is a core part of the memory pipeline and must be on by default");
    }

    [Fact]
    public void Heartbeat_nightly_consolidation_lists_required_inputs()
    {
        var consolidationSection = ExtractSection(DefaultWorkspaceTemplates.Heartbeat(), "Nightly Memory Consolidation");
        consolidationSection.Should().Contain("MEMORY.md");
        consolidationSection.Should().Contain("memory/YYYY-MM-DD.md");
    }

    [Fact]
    public void Heartbeat_nightly_consolidation_instructs_daily_log_cleanup()
    {
        var consolidationSection = ExtractSection(DefaultWorkspaceTemplates.Heartbeat(), "Nightly Memory Consolidation");
        consolidationSection.Should().Contain("fs_rm",
            because: "nightly consolidation must delete the daily log file after merging to prevent accumulation");
    }

    [Fact]
    public void Heartbeat_nightly_consolidation_specifies_retention_rule()
    {
        var consolidationSection = ExtractSection(DefaultWorkspaceTemplates.Heartbeat(), "Nightly Memory Consolidation");
        consolidationSection.Should().Contain("200 lines",
            because: "nightly consolidation must enforce a size limit to bound MEMORY.md growth");
    }

    // ── Heartbeat: Memory Freshness Review (Stage 5 of Nightly Consolidation) ─

    [Fact]
    public void Heartbeat_nightly_consolidation_includes_memory_freshness_review()
    {
        var consolidationSection = ExtractSection(DefaultWorkspaceTemplates.Heartbeat(), "Nightly Memory Consolidation");
        consolidationSection.Should().Contain("Memory freshness review",
            because: "nightly consolidation must include a stage to demote stale entries");
        consolidationSection.Should().Contain("MEMORY.md",
            because: "freshness review reads from the consolidated memory file");
        consolidationSection.Should().Contain("dormant",
            because: "stale entries should be moved to the dormant directory");
    }

    // ── Agents: workspace_memory_append guidance ─────────────────────────────

    [Fact]
    public void Agents_template_contains_workspace_memory_append_guidance()
    {
        var agents = DefaultWorkspaceTemplates.Agents();
        agents.Should().Contain("workspace_memory_append",
            because: "AGENTS.md must guide the Agent when to record memories during a session");
    }

    [Fact]
    public void Agents_template_contains_heartbeat_target_guidance()
    {
        var agents = DefaultWorkspaceTemplates.Agents();
        agents.Should().Contain("target=heartbeat",
            because: "AGENTS.md must guide the Agent to use workspace_protocol_update with target=heartbeat for automation rules");
    }

    [Fact]
    public void Agents_template_contains_canvas_upsert_guidance()
    {
        var agents = DefaultWorkspaceTemplates.Agents();
        agents.Should().Contain("canvas_upsert",
            because: "AGENTS.md must guide the Agent to publish results to Canvas");
    }

    [Fact]
    public void Agents_template_contains_inbox_create_guidance()
    {
        var agents = DefaultWorkspaceTemplates.Agents();
        agents.Should().Contain("inbox_create",
            because: "AGENTS.md must guide the Agent to proactively notify the user via Inbox");
    }

    [Fact]
    public void Agents_template_contains_inbox_read_guidance()
    {
        var agents = DefaultWorkspaceTemplates.Agents();
        agents.Should().Contain("inbox_read",
            because: "AGENTS.md must guide the Agent to read Inbox before summarizing or acting on pending items");
    }

    [Fact]
    public void Agents_template_contains_identity_target_guidance()
    {
        var agents = DefaultWorkspaceTemplates.Agents();
        agents.Should().Contain("target=identity",
            because: "AGENTS.md must guide the Agent to update Koda's identity via workspace_protocol_update");
    }

    [Fact]
    public void Agents_template_contains_user_target_guidance()
    {
        var agents = DefaultWorkspaceTemplates.Agents();
        agents.Should().Contain("target=user",
            because: "AGENTS.md must guide the Agent to update user profile via workspace_protocol_update");
    }

    // ── Bootstrap: write-back instruction ────────────────────────────────────

    [Fact]
    public void Bootstrap_template_instructs_write_back_after_discovery()
    {
        var bootstrap = DefaultWorkspaceTemplates.Bootstrap();
        bootstrap.Should().Contain("workspace_protocol_update",
            because: "Bootstrap guide must instruct Koda to persist what was learned using workspace_protocol_update");
    }

    [Fact]
    public void Bootstrap_template_covers_identity_soul_user_targets()
    {
        var bootstrap = DefaultWorkspaceTemplates.Bootstrap();
        bootstrap.Should().Contain("target=identity");
        bootstrap.Should().Contain("target=soul");
        bootstrap.Should().Contain("target=user",
            because: "Bootstrap must instruct writing all three core workspace files after the discovery conversation");
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
