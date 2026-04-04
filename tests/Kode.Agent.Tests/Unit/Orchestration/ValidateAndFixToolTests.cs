using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Orchestration;
using Moq;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class ValidateAndFixToolTests
{
    private static ValidateAndFixTool CreateTool(string modelId = "test-model")
    {
        return new ValidateAndFixTool(
            new Mock<IModelProvider>().Object, modelId,
            new ToolRegistry(), new Mock<ISandboxFactory>().Object);
    }

    private static ToolContext CreateContext() => new()
    {
        AgentId = "test-parent", CallId = "test-call",
        Sandbox = new Mock<ISandbox>().Object,
        SandboxOptions = new SandboxOptions { WorkingDirectory = "/tmp", EnforceBoundary = false },
    };

    [Fact] public void Name_Is_validate_and_fix() => CreateTool().Name.Should().Be("validate_and_fix");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Fails_When_ModelId_Is_Empty(string modelId)
    {
        var result = await CreateTool(modelId).ExecuteAsync(
            new ValidateAndFixArgs { Task = "test", ValidationCriteria = "criteria" }, CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No model ID configured");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_ValidationCriteria_Is_Empty()
    {
        var result = await CreateTool().ExecuteAsync(
            new ValidateAndFixArgs { Task = "test", ValidationCriteria = "   " }, CreateContext());
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("ValidationCriteria");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Ok_With_Passed_False_When_Execution_Fails()
    {
        // execution sub-agent fails (tools stripped) → returns Ok{passed:false} not Fail
        var result = await CreateTool().ExecuteAsync(
            new ValidateAndFixArgs
            {
                Task = "test",
                ValidationCriteria = "must not be empty",
                Tools = ["fs_write"],   // stripped → execution fails
            }, CreateContext());

        result.Success.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("\"passed\":false");
    }

    [Fact]
    public void InputSchema_Contains_validationCriteria()
    {
        var json = JsonSerializer.Serialize(CreateTool().InputSchema);
        json.Should().Contain("validationCriteria");
    }
}
