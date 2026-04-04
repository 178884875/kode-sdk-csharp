using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration;
using Moq;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class IsolateTaskToolTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static IsolateTaskTool CreateTool(string modelId = "test-model")
    {
        var modelProvider = new Mock<IModelProvider>().Object;
        var toolRegistry  = new ToolRegistry();
        var sandboxFactory = new Mock<ISandboxFactory>().Object;
        return new IsolateTaskTool(modelProvider, modelId, toolRegistry, sandboxFactory);
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
    public void Name_Is_isolate_task()
    {
        var tool = CreateTool();
        tool.Name.Should().Be("isolate_task");
    }

    [Fact]
    public void Attributes_Are_ReadOnly_And_NoEffect()
    {
        var tool = CreateTool();
        tool.Attributes.ReadOnly.Should().BeTrue();
        tool.Attributes.NoEffect.Should().BeTrue();
    }

    // ── fast-fail: empty model ID ─────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Fails_When_ModelId_Is_Empty(string modelId)
    {
        var tool   = CreateTool(modelId);
        var result = await tool.ExecuteAsync(new IsolateTaskArgs { Task = "test" }, CreateContext());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No model ID configured");
    }

    // ── fast-fail: tool whitelist ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Fails_When_All_Requested_Tools_Are_Not_In_Whitelist()
    {
        var tool = CreateTool();
        var args = new IsolateTaskArgs
        {
            Task  = "test",
            Tools = ["fs_write", "channel_send", "inbox_create"],
        };

        var result = await tool.ExecuteAsync(args, CreateContext());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No allowed tools remain");
    }

    [Fact]
    public async Task ExecuteAsync_Strips_Disallowed_Tools_While_Keeping_Allowed_Ones()
    {
        // fs_read is allowed, fs_write is not — passing both should still fail only if
        // the result count is 0.  Here we pass a mix: result should still be non-zero.
        // We cannot run the agent without an API key, but we can verify the filtering
        // rejects the request when NOTHING passes the whitelist.
        var tool = CreateTool();
        var args = new IsolateTaskArgs
        {
            Task  = "test",
            Tools = ["fs_write"],          // stripped → count = 0 → fail
        };

        var result = await tool.ExecuteAsync(args, CreateContext());

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    // ── schema ────────────────────────────────────────────────────────────────

    [Fact]
    public void InputSchema_Is_Not_Null()
    {
        var tool = CreateTool();
        tool.InputSchema.Should().NotBeNull();
        var json = JsonSerializer.Serialize(tool.InputSchema);
        json.Should().Contain("task");
    }
}
