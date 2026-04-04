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
/// Live integration tests for <see cref="ParallelResearchTool"/>.
/// Skipped unless ISOLATE_TASK_API_KEY is set in the environment.
/// </summary>
public sealed class ParallelResearchToolIntegrationTests
{
    [ParallelResearchFact]
    public async Task Three_independent_tasks_all_succeed_in_parallel()
    {
        using var fixture = new ParallelResearchFixture();
        var tool    = fixture.CreateTool();
        var context = fixture.CreateContext();

        var args = new ParallelResearchArgs
        {
            Tasks =
            [
                new ResearchTask { Name = "Alpha",   Task = "Reply with exactly: ALPHA_DONE" },
                new ResearchTask { Name = "Beta",    Task = "Reply with exactly: BETA_DONE"  },
                new ResearchTask { Name = "Gamma",   Task = "Reply with exactly: GAMMA_DONE" },
            ],
            MaxConcurrency = 0,
            FailFast       = false,
        };

        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        result.Success.Should().BeTrue(because: result.Error ?? "(no error)");
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("\"succeeded\":true");
        json.Should().Contain("\"successCount\":3");
        json.Should().Contain("ALPHA_DONE");
        json.Should().Contain("BETA_DONE");
        json.Should().Contain("GAMMA_DONE");
    }

    [ParallelResearchFact]
    public async Task FailFast_false_collects_all_results_even_when_some_fail()
    {
        using var fixture = new ParallelResearchFixture();
        var tool    = fixture.CreateTool();
        var context = fixture.CreateContext();

        var args = new ParallelResearchArgs
        {
            Tasks =
            [
                new ResearchTask { Name = "OK",   Task = "Reply with exactly: TASK_OK" },
                // Stripped tools → fast fail (no API call for the sub-agent)
                new ResearchTask { Name = "Fail", Task = "test", Tools = ["fs_write"] },
            ],
            FailFast = false,
        };

        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        result.Success.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("\"succeeded\":false");
        json.Should().Contain("\"successCount\":1");
        json.Should().Contain("\"failureCount\":1");
        json.Should().Contain("TASK_OK");
    }

    [ParallelResearchFact]
    public async Task MaxConcurrency_one_still_completes_all_tasks()
    {
        using var fixture = new ParallelResearchFixture();
        var tool    = fixture.CreateTool();
        var context = fixture.CreateContext();

        var args = new ParallelResearchArgs
        {
            Tasks =
            [
                new ResearchTask { Name = "T1", Task = "Reply with exactly: T1_OK" },
                new ResearchTask { Name = "T2", Task = "Reply with exactly: T2_OK" },
            ],
            MaxConcurrency = 1,   // serial via semaphore
            FailFast       = false,
        };

        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        result.Success.Should().BeTrue(because: result.Error ?? "(no error)");
        var json = JsonSerializer.Serialize(result.Value);
        json.Should().Contain("\"totalTasks\":2");
        json.Should().Contain("T1_OK");
        json.Should().Contain("T2_OK");
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private sealed class ParallelResearchFixture : IDisposable
    {
        private readonly string          _workDir       = AppContext.BaseDirectory;
        private readonly IModelProvider  _modelProvider;
        private readonly IToolRegistry   _toolRegistry;
        private readonly ISandboxFactory _sandboxFactory;

        public ParallelResearchFixture()
        {
            _modelProvider = new OpenAIProvider(new OpenAIOptions
            {
                ApiKey  = ParallelResearchFactAttribute.ReadApiKey()!,
                BaseUrl = ParallelResearchFactAttribute.ReadBaseUrl(),
            });
            _toolRegistry = new ToolRegistry();
            _toolRegistry.RegisterBuiltinTools();
            _sandboxFactory = new LocalSandboxFactory();
        }

        public ParallelResearchTool CreateTool() => new(
            _modelProvider,
            ParallelResearchFactAttribute.ReadModel(),
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

    private sealed class ParallelResearchFactAttribute : FactAttribute
    {
        private const string EnvApiKey  = "ISOLATE_TASK_API_KEY";
        private const string EnvBaseUrl = "ISOLATE_TASK_BASE_URL";
        private const string EnvModel   = "ISOLATE_TASK_MODEL";

        public ParallelResearchFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(ReadApiKey()))
                Skip = $"Set {EnvApiKey} to run ParallelResearch live tests. " +
                       $"Optionally set {EnvBaseUrl} and {EnvModel}.";
        }

        internal static string? ReadApiKey()  => Environment.GetEnvironmentVariable(EnvApiKey);
        internal static string? ReadBaseUrl() => Environment.GetEnvironmentVariable(EnvBaseUrl);
        internal static string  ReadModel()   => Environment.GetEnvironmentVariable(EnvModel) ?? "gpt-4o-mini";
    }
}
