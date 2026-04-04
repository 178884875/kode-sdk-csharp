using Kode.Agent.Sdk.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Extension methods to register orchestration tools with a <see cref="IToolRegistry"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all orchestration tools into the registry.
    /// </summary>
    public static IToolRegistry RegisterOrchestrationTools(
        this IToolRegistry registry,
        IModelProvider modelProvider,
        string modelId,
        ISandboxFactory sandboxFactory,
        ILoggerFactory? loggerFactory = null)
    {
        registry.Register("isolate_task",
            _ => new IsolateTaskTool(modelProvider, modelId, registry, sandboxFactory, loggerFactory));
        registry.Register("pipeline",
            _ => new PipelineTool(modelProvider, modelId, registry, sandboxFactory, loggerFactory));
        registry.Register("parallel_research",
            _ => new ParallelResearchTool(modelProvider, modelId, registry, sandboxFactory, loggerFactory));
        registry.Register("retry_with_reflection",
            _ => new RetryWithReflectionTool(modelProvider, modelId, registry, sandboxFactory, loggerFactory));
        registry.Register("ask_specialist",
            _ => new AskSpecialistTool(modelProvider, modelId, registry, sandboxFactory, loggerFactory));
        registry.Register("context_distill",
            _ => new ContextDistillTool(modelProvider, modelId, registry, sandboxFactory, loggerFactory));
        registry.Register("validate_and_fix",
            _ => new ValidateAndFixTool(modelProvider, modelId, registry, sandboxFactory, loggerFactory));
        registry.Register("fan_out_fan_in",
            _ => new FanOutFanInTool(modelProvider, modelId, registry, sandboxFactory, loggerFactory));
        registry.Register("map_reduce",
            _ => new MapReduceTool(modelProvider, modelId, registry, sandboxFactory, loggerFactory));
        registry.Register("debate",
            _ => new DebateTool(modelProvider, modelId, registry, sandboxFactory, loggerFactory));
        return registry;
    }
}
