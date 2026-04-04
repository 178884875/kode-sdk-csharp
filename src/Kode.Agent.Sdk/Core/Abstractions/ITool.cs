namespace Kode.Agent.Sdk.Core.Abstractions;

/// <summary>
/// Interface for a tool that can be executed by an agent.
/// </summary>
public interface ITool
{
    /// <summary>
    /// Gets the unique name of the tool.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the description of what the tool does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the JSON Schema for the tool's input parameters.
    /// </summary>
    object InputSchema { get; }

    /// <summary>
    /// Gets the tool attributes.
    /// </summary>
    ToolAttributes Attributes { get; }

    /// <summary>
    /// Gets the prompt hint for the tool (injected into system prompt).
    /// </summary>
    /// <param name="context">The tool context.</param>
    /// <returns>Optional prompt text.</returns>
    ValueTask<string?> GetPromptAsync(ToolContext context);

    /// <summary>
    /// Executes the tool with the given arguments.
    /// </summary>
    /// <param name="arguments">The input arguments.</param>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tool execution result.</returns>
    Task<ToolResult> ExecuteAsync(
        object arguments,
        ToolContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts the tool to a descriptor for serialization.
    /// </summary>
    ToolDescriptor ToDescriptor();
}

/// <summary>
/// Tool attributes defining behavior and permissions.
/// </summary>
public record ToolAttributes
{
    /// <summary>
    /// Whether the tool is read-only (doesn't modify state).
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Whether the tool has no side effects.
    /// </summary>
    public bool NoEffect { get; init; }

    /// <summary>
    /// Whether the tool requires user approval.
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// Whether the tool can be run in parallel with others.
    /// </summary>
    public bool AllowParallel { get; init; } = true;

    /// <summary>
    /// Custom permission category.
    /// </summary>
    public string? PermissionCategory { get; init; }
}

/// <summary>
/// Context provided to tool execution.
/// </summary>
public record ToolContext
{
    /// <summary>
    /// The agent ID executing the tool.
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// The tool call ID.
    /// </summary>
    public required string CallId { get; init; }

    /// <summary>
    /// The sandbox for file/command operations.
    /// </summary>
    public required ISandbox Sandbox { get; init; }

    /// <summary>
    /// The agent instance (for sub-agent spawning).
    /// </summary>
    public IAgent? Agent { get; init; }

    /// <summary>
    /// Additional services available to the tool.
    /// </summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>
    /// Emits a custom event.
    /// </summary>
    public Action<string, object?>? Emit { get; init; }

    /// <summary>
    /// The sandbox options used to configure the current agent's sandbox.
    /// Tools that spawn child agents (e.g. isolate_task) should inherit these
    /// to preserve filesystem access rights.
    /// </summary>
    public SandboxOptions? SandboxOptions { get; init; }

    /// <summary>
    /// Context pressure in [0.0, 1.0] — ratio of current token usage to the compression
    /// trigger threshold. Tools may use this to adaptively limit their output size.
    /// 0 = context nearly empty; 1.0 = at compression threshold; >1.0 = over threshold.
    /// </summary>
    public float ContextPressure { get; init; }

    /// <summary>
    /// Cancellation token.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Result of tool execution.
/// </summary>
public record ToolResult
{
    /// <summary>
    /// Whether execution was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The result value.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static ToolResult Ok(object? value = null) => new() { Success = true, Value = value };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static ToolResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Interface for tool registry.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Registers a tool factory.
    /// </summary>
    /// <param name="id">The registry ID.</param>
    /// <param name="factory">The tool factory function.</param>
    void Register(string id, Func<ToolFactoryContext, ITool> factory);

    /// <summary>
    /// Registers a tool instance.
    /// </summary>
    /// <param name="tool">The tool instance.</param>
    void Register(ITool tool);

    /// <summary>
    /// Creates a tool instance from the registry.
    /// </summary>
    /// <param name="id">The registry ID.</param>
    /// <param name="config">Optional configuration.</param>
    /// <returns>The tool instance.</returns>
    ITool Create(string id, Dictionary<string, object>? config = null);

    /// <summary>
    /// Checks if a tool is registered.
    /// </summary>
    bool Has(string id);

    /// <summary>
    /// Lists all registered tool IDs.
    /// </summary>
    IReadOnlyList<string> List();

    /// <summary>
    /// Gets a tool by name.
    /// </summary>
    ITool? Get(string name);
}

/// <summary>
/// Context for tool factory.
/// </summary>
public record ToolFactoryContext
{
    /// <summary>
    /// Configuration passed to the factory.
    /// </summary>
    public Dictionary<string, object>? Config { get; init; }

    /// <summary>
    /// Service provider for dependency resolution.
    /// </summary>
    public IServiceProvider? Services { get; init; }
}
