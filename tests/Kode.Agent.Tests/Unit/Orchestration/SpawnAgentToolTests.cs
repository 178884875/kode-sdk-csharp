using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration;
using Moq;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class SpawnAgentToolTests
{
    private static SpawnAgentTool CreateTool(string modelId = "test-model") =>
        new(new Mock<IModelProvider>().Object, modelId,
            new ToolRegistry(), new Mock<ISandboxFactory>().Object);

    private static ToolContext CreateContext(string? workDir = "/tmp") => new()
    {
        AgentId = "test-parent",
        CallId = "test-call",
        Sandbox = new Mock<ISandbox>().Object,
        SandboxOptions = workDir == null ? null : new SandboxOptions
        {
            WorkingDirectory = workDir,
            EnforceBoundary = false,
        },
    };

    [Fact]
    public void Name_Is_spawn_agent() =>
        CreateTool().Name.Should().Be("spawn_agent");

    [Fact]
    public void InputSchema_Contains_templatePath()
    {
        var json = JsonSerializer.Serialize(CreateTool().InputSchema);
        json.Should().Contain("templatePath");
    }

    [Fact]
    public void InputSchema_Contains_prompt()
    {
        var json = JsonSerializer.Serialize(CreateTool().InputSchema);
        json.Should().Contain("prompt");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Fails_When_ModelId_IsEmpty(string modelId)
    {
        var result = await CreateTool(modelId).ExecuteAsync(
            new SpawnAgentArgs { TemplatePath = "/tmp/x.json", Prompt = "do it" },
            CreateContext());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No model ID configured");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_TemplateFile_NotFound()
    {
        var result = await CreateTool().ExecuteAsync(
            new SpawnAgentArgs { TemplatePath = "/nonexistent/agent.json", Prompt = "go" },
            CreateContext());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Failed to load template");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_Template_HasInvalidJson()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not json at all");

            var result = await CreateTool().ExecuteAsync(
                new SpawnAgentArgs { TemplatePath = path, Prompt = "go" },
                CreateContext());

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Failed to load template");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_Template_MissingId()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"system_prompt":"Hello."}""");

            var result = await CreateTool().ExecuteAsync(
                new SpawnAgentArgs { TemplatePath = path, Prompt = "go" },
                CreateContext());

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Failed to load template");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Attributes_NotReadOnly()
    {
        CreateTool().Attributes.ReadOnly.Should().BeFalse();
    }
}
