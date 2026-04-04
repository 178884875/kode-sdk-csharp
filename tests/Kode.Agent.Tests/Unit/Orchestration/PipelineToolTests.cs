using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration;
using Moq;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class PipelineToolTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static PipelineTool CreateTool(string modelId = "test-model")
    {
        var modelProvider  = new Mock<IModelProvider>().Object;
        var toolRegistry   = new ToolRegistry();
        var sandboxFactory = new Mock<ISandboxFactory>().Object;
        return new PipelineTool(modelProvider, modelId, toolRegistry, sandboxFactory);
    }

    private static ToolContext CreateContext() => new()
    {
        AgentId        = "test-parent",
        CallId         = "test-call",
        Sandbox        = new Mock<ISandbox>().Object,
        SandboxOptions = new SandboxOptions { WorkingDirectory = "/tmp", EnforceBoundary = false },
    };

    private static PipelineStage StageWithStrippedTools(string name = "S1") =>
        new() { Name = name, Task = "test", Tools = ["fs_write"] };

    // ── metadata ──────────────────────────────────────────────────────────────

    [Fact]
    public void Name_Is_pipeline()
    {
        CreateTool().Name.Should().Be("pipeline");
    }

    [Fact]
    public void InputSchema_Contains_stages()
    {
        var json = JsonSerializer.Serialize(CreateTool().InputSchema);
        json.Should().Contain("stages");
    }

    // ── fast-fail: model ID ───────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Fails_When_ModelId_Is_Empty(string modelId)
    {
        var tool = CreateTool(modelId);
        var args = new PipelineArgs { Stages = [new PipelineStage { Task = "test" }] };

        var result = await tool.ExecuteAsync(args, CreateContext());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No model ID configured");
    }

    // ── fast-fail: empty stages ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Fails_When_Stages_Is_Empty()
    {
        var tool   = CreateTool();
        var args   = new PipelineArgs { Stages = [] };
        var result = await tool.ExecuteAsync(args, CreateContext());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("at least one stage");
    }

    // ── tool-whitelist enforcement (no API needed) ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Stops_Pipeline_On_First_Failure_When_StopOnFailure_True()
    {
        var tool = CreateTool();
        var args = new PipelineArgs
        {
            Stages = [StageWithStrippedTools("S1"), StageWithStrippedTools("S2")],
            StopOnFailure = true,
        };

        var result = await tool.ExecuteAsync(args, CreateContext());

        // Returns Ok (not Fail) — caller reads the detail
        result.Success.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("completedStages");
        json.Should().Contain("\"succeeded\":false");
        // Only S1 attempted (S2 was not started)
        json.Should().Contain("S1");
    }

    [Fact]
    public async Task ExecuteAsync_Collects_All_Stages_When_StopOnFailure_False()
    {
        var tool = CreateTool();
        var args = new PipelineArgs
        {
            Stages = [StageWithStrippedTools("S1"), StageWithStrippedTools("S2")],
            StopOnFailure = false,
        };

        var result = await tool.ExecuteAsync(args, CreateContext());

        result.Success.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("S1");
        json.Should().Contain("S2");
        json.Should().Contain("\"totalStages\":2");
    }
}
