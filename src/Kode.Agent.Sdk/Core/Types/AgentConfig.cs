namespace Kode.Agent.Sdk.Core.Types;

using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Context;
using Kode.Agent.Sdk.Core.Hooks;
using Kode.Agent.Sdk.Core.Skills;
using Kode.Agent.Sdk.Core.Templates;

/// <summary>
/// Configuration for creating an agent.
/// </summary>
public record AgentConfig
{
  /// <summary>
  /// The model to use (e.g., "claude-3-5-sonnet-20241022").
  /// </summary>
  public string Model { get; init; } = string.Empty;

    /// <summary>
    /// System prompt/instructions.
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// Template ID for agent templates.
    /// </summary>
    public string? TemplateId { get; init; }

    /// <summary>
    /// Maximum iterations before stopping.
    /// </summary>
    public int MaxIterations { get; init; } = 100;

    /// <summary>
    /// Maximum tokens per model call.
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Temperature for model sampling.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// Whether to enable extended thinking.
    /// </summary>
    public bool EnableThinking { get; init; }

    /// <summary>
    /// Token budget for thinking.
    /// </summary>
    public int? ThinkingBudget { get; init; }

    /// <summary>
    /// Whether to expose thinking/reasoning content in events/messages (default: false, aligned with TS exposeThinking).
    /// </summary>
    public bool? ExposeThinking { get; init; }

    /// <summary>
    /// Tool names to enable.
    /// </summary>
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>
    /// Permission mode configuration.
    /// </summary>
    public PermissionConfig? Permissions { get; init; }

    /// <summary>
    /// Sandbox options for the agent runtime.
    /// </summary>
    public SandboxOptions? SandboxOptions { get; init; }

    /// <summary>
    /// Optional hooks to run during agent execution.
    /// </summary>
    public IReadOnlyList<IHooks>? Hooks { get; init; }

  /// <summary>
  /// Optional context manager configuration (aligned with TS context options).
  /// </summary>
  public ContextManagerOptions? Context { get; init; }

    /// <summary>
    /// Optional skills configuration (aligned with TS config.skills).
    /// </summary>
    public SkillsConfig? Skills { get; init; }

    /// <summary>
    /// Optional sub-agent configuration (aligned with TS template.runtime.subagents / overrides.subagents).
    /// </summary>
    public SubAgentConfig? SubAgents { get; init; }

    /// <summary>
    /// Optional todo configuration (aligned with TS template.runtime.todo / overrides.todo).
    /// </summary>
    public TodoConfig? Todo { get; init; }

    /// <summary>
    /// Maximum tool concurrency.
    /// </summary>
    public int MaxToolConcurrency { get; init; } = 3;

    /// <summary>
    /// Tool execution timeout (default: 30min).
    /// CLI tools like claude -p may run 2-5 minutes for complex tasks;
    /// 60s hard-kill was too aggressive, causing premature cancellation.
    /// </summary>
    public TimeSpan ToolTimeout { get; init; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Optional override values used when resuming from Store metadata (aligned with TS resumeFromStore overrides).
/// Any null property means "keep the stored value".
/// </summary>
public record AgentConfigOverrides
{
    public string? Model { get; init; }
    public string? SystemPrompt { get; init; }
    public string? TemplateId { get; init; }
    public int? MaxIterations { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
    public bool? EnableThinking { get; init; }
    public int? ThinkingBudget { get; init; }
    public bool? ExposeThinking { get; init; }
    public IReadOnlyList<string>? Tools { get; init; }
    public PermissionConfig? Permissions { get; init; }
    public SandboxOptions? SandboxOptions { get; init; }
    public IReadOnlyList<IHooks>? Hooks { get; init; }
    public ContextManagerOptions? Context { get; init; }
    public SkillsConfig? Skills { get; init; }
    public SubAgentConfig? SubAgents { get; init; }
    public TodoConfig? Todo { get; init; }
    public int? MaxToolConcurrency { get; init; }
    public TimeSpan? ToolTimeout { get; init; }
}

/// <summary>
/// Permission configuration for tool execution.
/// </summary>
public record PermissionConfig
{
    /// <summary>
    /// TS-aligned permission mode name.
    /// Built-in: "auto" | "approval" | "readonly". Custom modes are allowed.
    /// </summary>
    public string Mode { get; init; } = "auto";

    /// <summary>
    /// Optional allowlist of tools that are permitted to run.
    /// If specified (non-empty), any tool not in this list will be denied.
    /// Use "*" to allow all tools.
    /// </summary>
    public IReadOnlyList<string>? AllowTools { get; init; }

    /// <summary>
    /// Tools that require explicit approval (TS: requireApprovalTools).
    /// </summary>
    public IReadOnlyList<string>? RequireApprovalTools { get; init; }

    /// <summary>
    /// Tools that are always denied (TS: denyTools).
    /// </summary>
    public IReadOnlyList<string>? DenyTools { get; init; }

    /// <summary>
    /// Tools hidden from the model's tool schema until revealed via skill activation.
    /// Hidden tools are still registered and executable if explicitly granted,
    /// but their schema is not sent to the model until a skill calls GrantTools().
    /// </summary>
    public IReadOnlyList<string>? SchemaHiddenTools { get; init; }

    /// <summary>
    /// Optional metadata for custom permission modes.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}

/// <summary>
/// Options for resuming an agent.
/// </summary>
public record ResumeOptions
{
    /// <summary>
    /// Whether to auto-run after resuming.
    /// </summary>
    public bool AutoRun { get; init; }

    /// <summary>
    /// Recovery strategy for incomplete tool calls.
    /// </summary>
    public RecoveryStrategy Strategy { get; init; } = RecoveryStrategy.Crash;
}

/// <summary>
/// Strategy for recovering from incomplete state.
/// </summary>
public enum RecoveryStrategy
{
    /// <summary>
    /// Seal incomplete tool calls as failed.
    /// </summary>
    Crash,

    /// <summary>
    /// Keep state as-is for manual recovery.
    /// </summary>
    Manual
}
