using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Store.Json;

namespace KodaClaw.Runtime;

public sealed class MainSessionOptions
{
    public static readonly IReadOnlyList<string> DefaultTools =
    [
        "fs_read",
        "fs_write",
        "fs_glob",
        "fs_grep",
        "fs_edit",
        "fs_rm",
        "fs_list",
        "bash_run",
        "bash_kill",
        "bash_logs",
        "todo_read",
        "todo_write",
        "skill_list",
        "skill_activate",
        "skill_resource",
        "workspace_memory_append",
        "workspace_protocol_update",
        "canvas_upsert",
        "inbox_create",
        "inbox_read",
        "workspace_read",
        "channel_send",
        "channel_list",
        "generate_image",
    ];

    public static readonly IReadOnlyList<string> DefaultRequireApprovalTools =
    [
        "fs_write",
        "fs_edit",
        "fs_rm",
        "bash_run",
        "bash_kill",
        "todo_write",
        "skill_activate",
    ];

    public string Model { get; init; } = "koda-main";

    public string? SystemPrompt { get; init; } = "You are KodaClaw main assistant.";

    public int MaxIterations { get; init; } = 8;

    public int MaxPromptCharacters { get; init; } = 16000;

    public IReadOnlyList<string> Tools { get; init; } = DefaultTools;

    public PermissionConfig Permissions { get; init; } = new()
    {
        Mode = "auto",
        RequireApprovalTools = DefaultRequireApprovalTools,
    };

    /// <summary>
    /// Fraction of the model's context window at which compression is triggered.
    /// </summary>
    public double ContextCompressionTriggerRatio { get; init; } = 0.75;

    /// <summary>
    /// Fraction of the model's context window to compress down to.
    /// </summary>
    public double ContextCompressionTargetRatio { get; init; } = 0.40;

    /// <summary>
    /// Assumed context window size (tokens) used to compute compression thresholds.
    /// Conservative default of 128k covers all modern OpenAI and Anthropic models.
    /// </summary>
    public int DefaultContextWindowSize { get; init; } = 128_000;
}

public interface IMainSessionAgentDependenciesFactory
{
    AgentDependencies Create(string sessionId, string sessionDirectory);
}

public sealed class MainSessionDependencies
{
    public required IModelProvider ModelProvider { get; init; }

    public IToolRegistry? ToolRegistry { get; init; }

    public ISandboxFactory? SandboxFactory { get; init; }

    public Microsoft.Extensions.Logging.ILoggerFactory? LoggerFactory { get; init; }
}

public sealed class DefaultMainSessionAgentDependenciesFactory : IMainSessionAgentDependenciesFactory
{
    private readonly MainSessionDependencies _dependencies;

    public DefaultMainSessionAgentDependenciesFactory(MainSessionDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    public AgentDependencies Create(string sessionId, string sessionDirectory)
    {
        var sessionsRoot = Directory.GetParent(sessionDirectory)?.FullName ?? sessionDirectory;

        return new AgentDependencies
        {
            Store = new JsonAgentStore(sessionsRoot),
            ToolRegistry = _dependencies.ToolRegistry ?? new ToolRegistry(),
            SandboxFactory = _dependencies.SandboxFactory ?? new LocalSandboxFactory(),
            ModelProvider = _dependencies.ModelProvider,
            LoggerFactory = _dependencies.LoggerFactory,
        };
    }
}
