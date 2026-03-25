using FluentAssertions;
using Kode.Agent.Sdk.Core.Skills;
using Xunit;

namespace KodaClaw.ContractTests.Skills;

/// <summary>
/// L3 契约测试 — koda-cli SKILL.md frontmatter 符合 agentskills.io 标准。
/// </summary>
public sealed class KodaCliSkillContractTests
{
    private static readonly string SkillPath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "KodaClaw.Gateway", "skills", "koda-cli", "SKILL.md");

    private static SkillMetadata LoadMetadata()
    {
        var fullPath = Path.GetFullPath(SkillPath);
        File.Exists(fullPath).Should().BeTrue($"SKILL.md should exist at {fullPath}");
        var content = File.ReadAllText(fullPath);
        return SkillsLoader.ParseFrontmatter(content);
    }

    [Fact]
    public void KodaCli_skill_name_is_koda_cli()
    {
        var meta = LoadMetadata();
        meta.Name.Should().Be("koda-cli");
    }

    [Fact]
    public void KodaCli_skill_description_is_non_empty_and_mentions_key_concepts()
    {
        var meta = LoadMetadata();
        meta.Description.Should().NotBeNullOrWhiteSpace();
        // Description should contain keywords for agent triggering
        var desc = meta.Description.ToLowerInvariant();
        desc.Should().ContainAny("automat", "workspace", "kc", "cli");
    }

    [Fact]
    public void KodaCli_skill_compatibility_mentions_kc_cli()
    {
        var meta = LoadMetadata();
        meta.Compatibility.Should().NotBeNullOrWhiteSpace();
        meta.Compatibility!.ToLowerInvariant().Should().Contain("kc");
    }

    [Fact]
    public void KodaCli_skill_allowed_tools_contains_bash_run_kc_constraint()
    {
        var meta = LoadMetadata();
        meta.AllowedTools.Should().NotBeNullOrEmpty();
        // After NormalizeToolSpec, Bash(kc:*) → bash_run[kc]
        meta.AllowedTools!.Should().Contain("bash_run[kc]");
    }
}
