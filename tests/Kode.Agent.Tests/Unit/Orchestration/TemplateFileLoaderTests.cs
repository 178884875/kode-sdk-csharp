using FluentAssertions;
using Kode.Agent.Sdk.Core.Templates;
using Kode.Agent.Tools.Orchestration.Internal;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class TemplateFileLoaderTests
{
    // ── ParseJson: valid cases ────────────────────────────────────────────────

    [Fact]
    public void ParseJson_MinimalTemplate_MapsRequiredFields()
    {
        // Problem 1: JSON keys must be snake_case
        var json = """{"id":"analyst","system_prompt":"You are an analyst."}""";

        var result = TemplateFileLoader.ParseJson(json);

        result.Definition.Id.Should().Be("analyst");
        result.Definition.SystemPrompt.Should().Be("You are an analyst.");
        result.MaxIterations.Should().Be(20); // default
    }

    [Fact]
    public void ParseJson_FullTemplate_MapsAllFields()
    {
        var json = """
        {
          "id": "architect",
          "name": "系统架构师",
          "description": "技术选型",
          "version": "1.0.0",
          "system_prompt": "You are an architect.",
          "model": "claude-sonnet-4-20250514",
          "tools": {
            "allow_all": false,
            "allowed_tools": ["fs_read", "fs_write"]
          },
          "permission": {
            "mode": "auto",
            "deny": ["channel_send"]
          },
          "runtime": {
            "max_iterations": 30,
            "sub_agents": { "depth": 1 }
          }
        }
        """;

        var result = TemplateFileLoader.ParseJson(json);

        result.Definition.Id.Should().Be("architect");
        result.Definition.Name.Should().Be("系统架构师");
        result.Definition.Model.Should().Be("claude-sonnet-4-20250514");
        result.MaxIterations.Should().Be(30);
        result.Definition.Tools.AllowAll.Should().BeFalse();
        result.Definition.Tools.AllowedTools.Should().BeEquivalentTo(["fs_read", "fs_write"]);
        result.Definition.Permission!.DenyTools.Should().Contain("channel_send");
        result.Definition.Runtime!.SubAgents!.Depth.Should().Be(1);
    }

    [Fact]
    public void ParseJson_ToolsAllowAll_MapsToToolsConfigAll()
    {
        var json = """{"id":"t","system_prompt":"s","tools":{"allow_all":true}}""";

        var result = TemplateFileLoader.ParseJson(json);

        result.Definition.Tools.AllowAll.Should().BeTrue();
    }

    [Fact]
    public void ParseJson_ToolsMissing_DefaultsToAllowAll()
    {
        var json = """{"id":"t","system_prompt":"s"}""";

        var result = TemplateFileLoader.ParseJson(json);

        result.Definition.Tools.AllowAll.Should().BeTrue();
    }

    [Fact]
    public void ParseJson_AllowAllFalseWithNoList_DefaultsToAllowAll()
    {
        // allow_all=false but no allowed_tools → fall back to AllowAll
        var json = """{"id":"t","system_prompt":"s","tools":{"allow_all":false}}""";

        var result = TemplateFileLoader.ParseJson(json);

        result.Definition.Tools.AllowAll.Should().BeTrue();
    }

    // ── Problem 1: snake_case field names ─────────────────────────────────────

    [Fact]
    public void ParseJson_SnakeCase_RequireApproval_IsParsed()
    {
        var json = """
        {
          "id": "t", "system_prompt": "s",
          "permission": { "require_approval": ["fs_write", "bash_run"] }
        }
        """;

        var result = TemplateFileLoader.ParseJson(json);

        result.Definition.Permission!.RequireApprovalTools
            .Should().BeEquivalentTo(["fs_write", "bash_run"]);
    }

    // ── Problem 2: runtime.skills.auto_activate ───────────────────────────────

    [Fact]
    public void ParseJson_SkillsAutoActivate_IsParsed()
    {
        var json = """
        {
          "id": "ws-agent",
          "system_prompt": "You are a workspace agent.",
          "runtime": {
            "skills": {
              "auto_activate": ["koda-workspace", "koda-memory"]
            }
          }
        }
        """;

        var result = TemplateFileLoader.ParseJson(json);

        result.AutoActivateSkills.Should().BeEquivalentTo(["koda-workspace", "koda-memory"]);
    }

    [Fact]
    public void ParseJson_SkillsMissing_AutoActivateSkillsIsNull()
    {
        var json = """{"id":"t","system_prompt":"s"}""";

        var result = TemplateFileLoader.ParseJson(json);

        result.AutoActivateSkills.Should().BeNull();
    }

    [Fact]
    public void ParseJson_SkillsRecommend_IsParsed()
    {
        var json = """
        {
          "id": "t", "system_prompt": "s",
          "runtime": {
            "skills": { "auto_activate": [], "recommend": ["koda-canvas"] }
          }
        }
        """;

        // recommend is parsed without throwing
        var act = () => TemplateFileLoader.ParseJson(json);
        act.Should().NotThrow();
    }

    // ── Problem 4: id format validation ──────────────────────────────────────

    [Theory]
    [InlineData("valid-id")]
    [InlineData("valid_id")]
    [InlineData("valid123")]
    [InlineData("a")]
    [InlineData("abc-123_xyz")]
    public void ParseJson_ValidId_DoesNotThrow(string id)
    {
        var json = $$$"""{"id":"{{{id}}}","system_prompt":"s"}""";

        var act = () => TemplateFileLoader.ParseJson(json);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Invalid-Id")]   // uppercase
    [InlineData("has space")]     // space
    [InlineData("has.dot")]       // dot
    [InlineData("Has/Slash")]     // slash
    [InlineData("CamelCase")]     // uppercase letters
    public void ParseJson_InvalidIdFormat_Throws(string id)
    {
        var json = $$$"""{"id":"{{{id}}}","system_prompt":"s"}""";

        var act = () => TemplateFileLoader.ParseJson(json, "test.json");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*invalid 'id'*");
    }

    // ── Problem 4: id missing / empty ─────────────────────────────────────────

    [Theory]
    [InlineData("""{"system_prompt":"s"}""")]
    [InlineData("""{"id":"","system_prompt":"s"}""")]
    [InlineData("""{"id":"   ","system_prompt":"s"}""")]
    public void ParseJson_MissingId_Throws(string json)
    {
        var act = () => TemplateFileLoader.ParseJson(json, "test.json");

        act.Should().Throw<InvalidOperationException>().WithMessage("*'id'*");
    }

    [Theory]
    [InlineData("""{"id":"t"}""")]
    [InlineData("""{"id":"t","system_prompt":""}""")]
    [InlineData("""{"id":"t","system_prompt":"   "}""")]
    public void ParseJson_MissingSystemPrompt_Throws(string json)
    {
        var act = () => TemplateFileLoader.ParseJson(json, "test.json");

        act.Should().Throw<InvalidOperationException>().WithMessage("*'system_prompt'*");
    }

    [Fact]
    public void ParseJson_InvalidJson_Throws()
    {
        var act = () => TemplateFileLoader.ParseJson("not valid json", "bad.json");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Invalid JSON*");
    }

    // ── LoadFromFile ──────────────────────────────────────────────────────────

    [Fact]
    public void LoadFromFile_FileNotFound_Throws()
    {
        var act = () => TemplateFileLoader.LoadFromFile("/nonexistent/path/template.json");

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void LoadFromFile_ValidFile_ReturnsTemplate()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"id":"test","system_prompt":"Hello."}""");

            var result = TemplateFileLoader.LoadFromFile(path);

            result.Definition.Id.Should().Be("test");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_RelativePath_ResolvesAgainstBaseDir()
    {
        var dir = Path.GetTempPath();
        var fileName = $"test-{Guid.NewGuid():N}.json";
        var fullPath = Path.Combine(dir, fileName);
        try
        {
            File.WriteAllText(fullPath, """{"id":"rel","system_prompt":"Relative."}""");

            var result = TemplateFileLoader.LoadFromFile(fileName, dir);

            result.Definition.Id.Should().Be("rel");
        }
        finally
        {
            File.Delete(fullPath);
        }
    }
}
