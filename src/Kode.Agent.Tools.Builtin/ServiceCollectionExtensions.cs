using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Tools.Builtin.FileSystem;
using Kode.Agent.Tools.Builtin.History;
using Kode.Agent.Tools.Builtin.Shell;
using Kode.Agent.Tools.Builtin.Skills;
using Kode.Agent.Tools.Builtin.Todo;
using Microsoft.Extensions.DependencyInjection;

namespace Kode.Agent.Tools.Builtin;

/// <summary>
/// Extension methods for registering built-in tools.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all built-in tools to the service collection.
    /// </summary>
    public static IServiceCollection AddBuiltinTools(this IServiceCollection services)
    {
        // File system tools
        services.AddSingleton<ITool, FsReadTool>();
        services.AddSingleton<ITool, FsWriteTool>();
        services.AddSingleton<ITool, FsGlobTool>();
        services.AddSingleton<ITool, FsGrepTool>();
        services.AddSingleton<ITool, FsEditTool>();
        services.AddSingleton<ITool, FsRmTool>();
        services.AddSingleton<ITool, FsListTool>();

        // Shell tools
        services.AddSingleton<ITool, BashRunTool>();
        services.AddSingleton<ITool, BashKillTool>();
        services.AddSingleton<ITool, BashLogsTool>();

        // Todo tools
        services.AddSingleton<ITool, TodoReadTool>();
        services.AddSingleton<ITool, TodoWriteTool>();

        return services;
    }

    /// <summary>
    /// Registers all built-in tools with the tool registry.
    /// </summary>
    public static IToolRegistry RegisterBuiltinTools(this IToolRegistry registry)
    {
        // File system tools
        registry.Register(new FsReadTool());
        registry.Register(new FsWriteTool());
        registry.Register(new FsGlobTool());
        registry.Register(new FsGrepTool());
        registry.Register(new FsEditTool());
        registry.Register(new FsRmTool());
        registry.Register(new FsListTool());
        
        // Shell tools
        registry.Register(new BashRunTool());
        registry.Register(new BashKillTool());
        registry.Register(new BashLogsTool());

        // Todo tools (without service)
        registry.Register(new TodoReadTool());
        registry.Register(new TodoWriteTool());
        
        // Skills tools
        registry.Register(new SkillListTool());
        registry.Register(new SkillActivateTool());
        registry.Register(new SkillResourceTool());

        // History tools
        registry.Register(new HistorySearchTool());

        return registry;
    }
}

/// <summary>
/// Toolkit containing all built-in tools.
/// </summary>
public sealed class BuiltinToolKit : ToolKit
{
    protected override void RegisterTools()
    {
        // File system tools
        RegisterTool(new FsReadTool());
        RegisterTool(new FsWriteTool());
        RegisterTool(new FsGlobTool());
        RegisterTool(new FsGrepTool());
        RegisterTool(new FsEditTool());
        RegisterTool(new FsRmTool());
        RegisterTool(new FsListTool());

        // Shell tools
        RegisterTool(new BashRunTool());
        RegisterTool(new BashKillTool());
        RegisterTool(new BashLogsTool());

        // Todo tools
        RegisterTool(new TodoReadTool());
        RegisterTool(new TodoWriteTool());
    }
}
