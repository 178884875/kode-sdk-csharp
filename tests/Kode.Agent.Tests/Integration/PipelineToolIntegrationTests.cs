using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Providers;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Builtin;
using Kode.Agent.Tools.Orchestration;
using Xunit;

namespace Kode.Agent.Tests.Integration;

/// <summary>
/// Live integration tests for <see cref="PipelineTool"/>.
/// Skipped unless ISOLATE_TASK_API_KEY is set in the environment.
/// Reuses the same env-var convention as <see cref="IsolateTaskToolIntegrationTests"/>.
/// </summary>
public sealed class PipelineToolIntegrationTests
{
    [PipelineFact]
    public async Task Two_stage_pipeline_succeeds_and_second_stage_receives_context()
    {
        using var fixture = new PipelineFixture();
        var tool    = fixture.CreateTool();
        var context = fixture.CreateContext();

        var args = new PipelineArgs
        {
            Stages =
            [
                new PipelineStage
                {
                    Name          = "Count",
                    Task          = "Count the number of .cs files in the current working directory (non-recursive). " +
                                    "Reply with ONLY a single integer, nothing else.",
                    Tools         = ["fs_list"],
                    MaxIterations = 5,
                },
                new PipelineStage
                {
                    Name          = "Report",
                    Task          = "Based on the count provided in the context, write a one-sentence summary " +
                                    "that includes the exact number of .cs files.",
                    Tools         = null,         // no tools restriction — agent may skip tool calls
                    MaxIterations = 3,
                },
            ],
            StopOnFailure = true,
        };

        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        result.Success.Should().BeTrue(because: result.Error ?? "(no error)");
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("completedStages");
        json.Should().Contain("\"succeeded\":true");
        json.Should().Contain("Count");
        json.Should().Contain("Report");
    }

    [PipelineFact]
    public async Task Pipeline_stops_after_first_stage_when_stopOnFailure_true_and_stage_has_no_tools()
    {
        using var fixture = new PipelineFixture();
        var tool    = fixture.CreateTool();
        var context = fixture.CreateContext();

        // Stage 1: request only disallowed tools → fails fast (no API call for agent)
        // Stage 2: should never run
        var args = new PipelineArgs
        {
            Stages =
            [
                new PipelineStage { Name = "Fail",   Task = "test", Tools = ["fs_write"] },
                new PipelineStage { Name = "Skipped", Task = "test" },
            ],
            StopOnFailure = true,
        };

        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        result.Success.Should().BeTrue();    // Ok (not Fail) — caller reads the stage detail
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("\"completedStages\":0");
        json.Should().Contain("Fail");
        json.Should().NotContain("Skipped"); // never started
    }

    [PipelineFact]
    public async Task Pipeline_no_tools_stage_can_still_produce_summary()
    {
        using var fixture = new PipelineFixture();
        var tool    = fixture.CreateTool();
        var context = fixture.CreateContext();

        var args = new PipelineArgs
        {
            Stages =
            [
                new PipelineStage
                {
                    Name          = "Echo",
                    Task          = "Reply with exactly: PIPELINE_OK",
                    MaxIterations = 3,
                },
            ],
        };

        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        result.Success.Should().BeTrue(because: result.Error ?? "(no error)");
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("PIPELINE_OK");
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private sealed class PipelineFixture : IDisposable
    {
        private readonly string          _workDir       = AppContext.BaseDirectory;
        private readonly IModelProvider  _modelProvider;
        private readonly IToolRegistry   _toolRegistry;
        private readonly ISandboxFactory _sandboxFactory;

        public PipelineFixture()
        {
            _modelProvider = new OpenAIProvider(new OpenAIOptions
            {
                ApiKey  = PipelineFactAttribute.ReadApiKey()!,
                BaseUrl = PipelineFactAttribute.ReadBaseUrl(),
            });
            _toolRegistry = new ToolRegistry();
            _toolRegistry.RegisterBuiltinTools();
            _sandboxFactory = new LocalSandboxFactory();
        }

        public PipelineTool CreateTool() => new(
            _modelProvider,
            PipelineFactAttribute.ReadModel(),
            _toolRegistry,
            _sandboxFactory);

        public ToolContext CreateContext()
        {
            var sandbox = _sandboxFactory.CreateAsync(new SandboxOptions
            {
                WorkingDirectory = _workDir,
                EnforceBoundary  = false,
            }).GetAwaiter().GetResult();

            return new ToolContext
            {
                AgentId        = "test-parent",
                CallId         = Guid.NewGuid().ToString("N"),
                Sandbox        = sandbox,
                SandboxOptions = new SandboxOptions
                {
                    WorkingDirectory = _workDir,
                    EnforceBoundary  = false,
                },
            };
        }

        public void Dispose() { }
    }

    // ── custom FactAttribute ──────────────────────────────────────────────────

    private sealed class PipelineFactAttribute : FactAttribute
    {
        private const string EnvApiKey  = "ISOLATE_TASK_API_KEY";
        private const string EnvBaseUrl = "ISOLATE_TASK_BASE_URL";
        private const string EnvModel   = "ISOLATE_TASK_MODEL";

        public PipelineFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(ReadApiKey()))
                Skip = $"Set {EnvApiKey} to run Pipeline live tests. " +
                       $"Optionally set {EnvBaseUrl} and {EnvModel}.";
        }

        internal static string? ReadApiKey()  => Environment.GetEnvironmentVariable(EnvApiKey);
        internal static string? ReadBaseUrl() => Environment.GetEnvironmentVariable(EnvBaseUrl);
        internal static string  ReadModel()   => Environment.GetEnvironmentVariable(EnvModel) ?? "gpt-4o-mini";
    }
}
