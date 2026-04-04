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
/// Live integration tests for <see cref="IsolateTaskTool"/>.
/// Skipped unless ISOLATE_TASK_API_KEY (and optionally ISOLATE_TASK_BASE_URL,
/// ISOLATE_TASK_MODEL) are set in the environment.
///
/// Typical setup:
///   export ISOLATE_TASK_API_KEY=sk-...
///   export ISOLATE_TASK_BASE_URL=https://api.openai.com   # optional
///   export ISOLATE_TASK_MODEL=gpt-4o-mini                 # optional, default
/// </summary>
public sealed class IsolateTaskToolIntegrationTests
{
    [IsolateTaskFact]
    public async Task Sub_agent_can_list_cs_files_and_return_coherent_summary()
    {
        using var fixture = new IsolateTaskFixture();
        var tool = fixture.CreateTool();
        var context = fixture.CreateContext();

        var args = new IsolateTaskArgs
        {
            Task = "List all .cs files in the current working directory (non-recursive). " +
                   "Report back the total count and the file names you found.",
            MaxIterations = 10,
            Tools = ["fs_glob", "fs_list"],
        };

        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        result.Success.Should().BeTrue(because: result.Error ?? "(no error)");
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        json.Should().Contain("summary");
        json.Should().Contain("stopReason");
    }

    [IsolateTaskFact]
    public async Task Sub_agent_fails_gracefully_when_all_requested_tools_are_stripped()
    {
        using var fixture = new IsolateTaskFixture();
        var tool = fixture.CreateTool();
        var context = fixture.CreateContext();

        // fs_write / channel_send are not in the hard whitelist → 0 tools remain
        var args = new IsolateTaskArgs
        {
            Task = "Write a file called test.txt with content 'hello'.",
            MaxIterations = 5,
            Tools = ["fs_write", "channel_send"],
        };

        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No allowed tools remain");
    }

    [IsolateTaskFact]
    public async Task Sub_agent_succeeds_with_no_tool_calls_needed()
    {
        using var fixture = new IsolateTaskFixture();
        var tool = fixture.CreateTool();
        var context = fixture.CreateContext();

        var args = new IsolateTaskArgs
        {
            Task = "Reply with exactly: DONE",
            MaxIterations = 5,
        };

        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        result.Success.Should().BeTrue(because: result.Error ?? "(no error)");
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        json.Should().Contain("DONE");
    }

    // ── fixture ──────────────────────────────────────────────────────────────

    private sealed class IsolateTaskFixture : IDisposable
    {
        private readonly string _workDir = AppContext.BaseDirectory;
        private readonly IModelProvider _modelProvider;
        private readonly IToolRegistry _toolRegistry;
        private readonly ISandboxFactory _sandboxFactory;

        public IsolateTaskFixture()
        {
            _modelProvider = new OpenAIProvider(new OpenAIOptions
            {
                ApiKey  = IsolateTaskFactAttribute.ReadApiKey()!,
                BaseUrl = IsolateTaskFactAttribute.ReadBaseUrl(),
            });

            _toolRegistry = new ToolRegistry();
            _toolRegistry.RegisterBuiltinTools();

            _sandboxFactory = new LocalSandboxFactory();
        }

        public IsolateTaskTool CreateTool() => new(
            _modelProvider,
            IsolateTaskFactAttribute.ReadModel(),
            _toolRegistry,
            _sandboxFactory);

        public ToolContext CreateContext()
        {
            var sandbox = _sandboxFactory.CreateAsync(new SandboxOptions
            {
                WorkingDirectory = _workDir,
                EnforceBoundary = false,
            }).GetAwaiter().GetResult();

            return new ToolContext
            {
                AgentId = "test-parent",
                CallId  = Guid.NewGuid().ToString("N"),
                Sandbox = sandbox,
                SandboxOptions = new SandboxOptions
                {
                    WorkingDirectory = _workDir,
                    EnforceBoundary = false,
                },
            };
        }

        public void Dispose() { }
    }

    // ── custom FactAttribute ─────────────────────────────────────────────────

    private sealed class IsolateTaskFactAttribute : FactAttribute
    {
        private const string EnvApiKey  = "ISOLATE_TASK_API_KEY";
        private const string EnvBaseUrl = "ISOLATE_TASK_BASE_URL";
        private const string EnvModel   = "ISOLATE_TASK_MODEL";

        public IsolateTaskFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(ReadApiKey()))
                Skip = $"Set {EnvApiKey} to run IsolateTask live tests. " +
                       $"Optionally set {EnvBaseUrl} and {EnvModel}.";
        }

        internal static string? ReadApiKey()  => Environment.GetEnvironmentVariable(EnvApiKey);
        internal static string? ReadBaseUrl() => Environment.GetEnvironmentVariable(EnvBaseUrl);
        internal static string  ReadModel()   => Environment.GetEnvironmentVariable(EnvModel) ?? "gpt-4o-mini";
    }
}
