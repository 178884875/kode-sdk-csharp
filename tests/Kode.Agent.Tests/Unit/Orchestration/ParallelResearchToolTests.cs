using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration;
using Moq;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class ParallelResearchToolTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ParallelResearchTool CreateTool(string modelId = "test-model")
    {
        var modelProvider  = new Mock<IModelProvider>().Object;
        var toolRegistry   = new ToolRegistry();
        var sandboxFactory = new Mock<ISandboxFactory>().Object;
        return new ParallelResearchTool(modelProvider, modelId, toolRegistry, sandboxFactory);
    }

    private static ToolContext CreateContext() => new()
    {
        AgentId        = "test-parent",
        CallId         = "test-call",
        Sandbox        = new Mock<ISandbox>().Object,
        SandboxOptions = new SandboxOptions { WorkingDirectory = "/tmp", EnforceBoundary = false },
    };

    private static ResearchTask TaskWithStrippedTools(string name) =>
        new() { Name = name, Task = "test", Tools = ["fs_write"] };

    // ── metadata ──────────────────────────────────────────────────────────────

    [Fact]
    public void Name_Is_parallel_research()
    {
        CreateTool().Name.Should().Be("parallel_research");
    }

    [Fact]
    public void Attributes_Are_ReadOnly_And_NoEffect()
    {
        var attrs = CreateTool().Attributes;
        attrs.ReadOnly.Should().BeTrue();
        attrs.NoEffect.Should().BeTrue();
    }

    [Fact]
    public void InputSchema_Contains_tasks()
    {
        var json = JsonSerializer.Serialize(CreateTool().InputSchema);
        json.Should().Contain("tasks");
    }

    // ── fast-fail: model ID ───────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Fails_When_ModelId_Is_Empty(string modelId)
    {
        var tool   = CreateTool(modelId);
        var args   = new ParallelResearchArgs { Tasks = [new ResearchTask { Task = "test" }] };
        var result = await tool.ExecuteAsync(args, CreateContext());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No model ID configured");
    }

    // ── fast-fail: empty tasks ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Fails_When_Tasks_Is_Empty()
    {
        var tool   = CreateTool();
        var result = await tool.ExecuteAsync(new ParallelResearchArgs { Tasks = [] }, CreateContext());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("at least one task");
    }

    // ── fast-fail: negative concurrency ──────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Fails_When_MaxConcurrency_Is_Negative()
    {
        var tool = CreateTool();
        var args = new ParallelResearchArgs
        {
            Tasks          = [new ResearchTask { Task = "test" }],
            MaxConcurrency = -1,
        };

        var result = await tool.ExecuteAsync(args, CreateContext());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("MaxConcurrency");
    }

    // ── tool-whitelist enforcement (no API needed) ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Reports_All_Failures_When_Tools_Stripped_And_FailFast_False()
    {
        var tool = CreateTool();
        var args = new ParallelResearchArgs
        {
            Tasks    = [TaskWithStrippedTools("T1"), TaskWithStrippedTools("T2")],
            FailFast = false,
        };

        var result = await tool.ExecuteAsync(args, CreateContext());

        result.Success.Should().BeTrue();           // Ok (not Fail) — caller reads the detail
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("\"succeeded\":false");
        json.Should().Contain("\"failureCount\":2");
        json.Should().Contain("\"totalTasks\":2");
        json.Should().Contain("T1");
        json.Should().Contain("T2");
    }

    [Fact]
    public async Task ExecuteAsync_MaxConcurrency_Zero_Runs_All_Tasks_Without_Blocking()
    {
        // No API → stripped tools → all fail fast. Just verifies no deadlock with concurrency=0.
        var tool = CreateTool();
        var args = new ParallelResearchArgs
        {
            Tasks          = [TaskWithStrippedTools("T1"), TaskWithStrippedTools("T2"), TaskWithStrippedTools("T3")],
            MaxConcurrency = 0,
            FailFast       = false,
        };

        var result = await tool.ExecuteAsync(args, CreateContext()).WaitAsync(TimeSpan.FromSeconds(5));

        result.Success.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("\"totalTasks\":3");
    }
}
