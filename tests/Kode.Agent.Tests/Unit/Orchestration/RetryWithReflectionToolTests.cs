using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration;
using Moq;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class RetryWithReflectionToolTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static RetryWithReflectionTool CreateTool(string modelId = "test-model")
    {
        var modelProvider  = new Mock<IModelProvider>().Object;
        var toolRegistry   = new ToolRegistry();
        var sandboxFactory = new Mock<ISandboxFactory>().Object;
        return new RetryWithReflectionTool(modelProvider, modelId, toolRegistry, sandboxFactory);
    }

    private static ToolContext CreateContext() => new()
    {
        AgentId        = "test-parent",
        CallId         = "test-call",
        Sandbox        = new Mock<ISandbox>().Object,
        SandboxOptions = new SandboxOptions { WorkingDirectory = "/tmp", EnforceBoundary = false },
    };

    // ── metadata ──────────────────────────────────────────────────────────────

    [Fact]
    public void Name_Is_retry_with_reflection()
    {
        CreateTool().Name.Should().Be("retry_with_reflection");
    }

    [Fact]
    public void Attributes_Are_ReadOnly_And_NoEffect()
    {
        var attrs = CreateTool().Attributes;
        attrs.ReadOnly.Should().BeTrue();
        attrs.NoEffect.Should().BeTrue();
    }

    // ── fast-fail: empty model ID ─────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Fails_When_ModelId_Is_Empty(string modelId)
    {
        var tool   = CreateTool(modelId);
        var result = await tool.ExecuteAsync(new RetryWithReflectionArgs { Task = "test" }, CreateContext());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No model ID configured");
    }

    // ── retry exhaustion (no API needed — all tools stripped) ─────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Ok_With_Success_False_After_All_Attempts_Fail()
    {
        var tool = CreateTool();
        var args = new RetryWithReflectionArgs
        {
            Task       = "test",
            MaxRetries = 2,
            Tools      = ["fs_write"],   // stripped → each attempt fails fast
        };

        var result = await tool.ExecuteAsync(args, CreateContext());

        // Tool returns Ok (not Fail) so the caller can inspect all attempts
        result.Success.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("\"success\":false");
        json.Should().Contain("\"totalAttempts\":3");   // 2 retries + 1 initial
        json.Should().Contain("\"attempts\"");
    }

    [Fact]
    public async Task ExecuteAsync_Records_Correct_Attempt_Count_For_MaxRetries_Zero()
    {
        var tool = CreateTool();
        var args = new RetryWithReflectionArgs
        {
            Task       = "test",
            MaxRetries = 0,               // no retries — just 1 attempt
            Tools      = ["fs_write"],
        };

        var result = await tool.ExecuteAsync(args, CreateContext());

        result.Success.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("\"totalAttempts\":1");
    }

    // ── MaxRetries clamping ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Clamps_MaxRetries_To_Five()
    {
        var tool = CreateTool();
        var args = new RetryWithReflectionArgs
        {
            Task       = "test",
            MaxRetries = 99,              // clamped to 5 → 6 total attempts
            Tools      = ["fs_write"],
        };

        var result = await tool.ExecuteAsync(args, CreateContext());

        result.Success.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("\"totalAttempts\":6");  // Math.Clamp(99,0,5)+1 = 6
    }

    // ── schema ────────────────────────────────────────────────────────────────

    [Fact]
    public void InputSchema_Contains_task_And_maxRetries()
    {
        var json = JsonSerializer.Serialize(CreateTool().InputSchema);
        json.Should().Contain("task");
        json.Should().Contain("maxRetries");
    }
}
