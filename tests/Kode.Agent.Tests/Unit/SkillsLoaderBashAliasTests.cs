using Kode.Agent.Sdk.Core.Skills;
using Xunit;

namespace Kode.Agent.Tests.Unit;

public class SkillsLoaderBashAliasTests
{
    // ── NormalizeToolSpec: plain tool names ───────────────────────────────────

    [Fact]
    public void NormalizeToolSpec_plain_tool_name_returns_unchanged()
    {
        Assert.Equal("fs_read", SkillsLoader.NormalizeToolSpec("fs_read"));
    }

    [Fact]
    public void NormalizeToolSpec_Bash_alias_maps_to_bash_run()
    {
        Assert.Equal("bash_run", SkillsLoader.NormalizeToolSpec("Bash"));
    }

    [Fact]
    public void NormalizeToolSpec_Read_alias_maps_to_fs_read()
    {
        Assert.Equal("fs_read", SkillsLoader.NormalizeToolSpec("Read"));
    }

    [Fact]
    public void NormalizeToolSpec_Write_alias_maps_to_fs_write()
    {
        Assert.Equal("fs_write", SkillsLoader.NormalizeToolSpec("Write"));
    }

    // ── NormalizeToolSpec: constraint syntax ──────────────────────────────────

    [Fact]
    public void NormalizeToolSpec_Bash_constraint_produces_internal_format()
    {
        Assert.Equal("bash_run[kc]", SkillsLoader.NormalizeToolSpec("Bash(kc:*)"));
    }

    [Fact]
    public void NormalizeToolSpec_Bash_git_constraint_produces_correct_format()
    {
        Assert.Equal("bash_run[git]", SkillsLoader.NormalizeToolSpec("Bash(git:*)"));
    }

    [Fact]
    public void NormalizeToolSpec_unknown_tool_with_constraint_preserves_name()
    {
        Assert.Equal("custom_tool[prefix]", SkillsLoader.NormalizeToolSpec("custom_tool(prefix:*)"));
    }

    // ── ParseFrontmatter: allowed-tools with Bash syntax ─────────────────────

    [Fact]
    public void ParseFrontmatter_allowed_tools_maps_Bash_constraint()
    {
        var content = """
            ---
            name: koda-cli
            description: Test skill
            allowed-tools: Bash(kc:*) fs_read
            ---
            Body
            """;

        var meta = SkillsLoader.ParseFrontmatter(content);

        Assert.NotNull(meta.AllowedTools);
        Assert.Contains("bash_run[kc]", meta.AllowedTools);
        Assert.Contains("fs_read", meta.AllowedTools);
    }

    [Fact]
    public void ParseFrontmatter_allowed_tools_multiple_Bash_constraints()
    {
        var content = """
            ---
            name: test-skill
            description: Multi constraint skill
            allowed-tools: Bash(git:*) Bash(jq:*) Read
            ---
            Body
            """;

        var meta = SkillsLoader.ParseFrontmatter(content);

        Assert.NotNull(meta.AllowedTools);
        Assert.Contains("bash_run[git]", meta.AllowedTools);
        Assert.Contains("bash_run[jq]", meta.AllowedTools);
        Assert.Contains("fs_read", meta.AllowedTools);
    }

    // ── Comma-separated allowed-tools (Claude Code native format) ─────────────

    [Fact]
    public void ParseFrontmatter_comma_separated_tools_are_parsed()
    {
        var content = """
            ---
            name: agent-browser
            description: Browser skill
            allowed-tools: Bash(npx agent-browser:*), Bash(agent-browser:*)
            ---
            Body
            """;

        var meta = SkillsLoader.ParseFrontmatter(content);

        Assert.NotNull(meta.AllowedTools);
        Assert.Contains("bash_run[npx agent-browser]", meta.AllowedTools);
        Assert.Contains("bash_run[agent-browser]", meta.AllowedTools);
    }

    [Fact]
    public void NormalizeToolSpec_multiword_bash_constraint_produces_internal_format()
    {
        Assert.Equal("bash_run[npx agent-browser]", SkillsLoader.NormalizeToolSpec("Bash(npx agent-browser:*)"));
    }
}
