using System.Net.Http;
using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Extensions;
using Kode.Agent.Sdk.Infrastructure.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Kode.Agent.Tools.Builtin;

namespace KodaClaw.Runtime;

public sealed class KodaClawRuntimeOptions
{
    public string? DefaultModel { get; set; }

    public string? OpenAIApiKey { get; set; }

    public string? OpenAIBaseUrl { get; set; }

    public string? AnthropicApiKey { get; set; }

    public string? AnthropicBaseUrl { get; set; }

    public string? SystemPrompt { get; set; }

    public int MainMaxIterations { get; set; } = 30;

    public int ChannelMaxIterations { get; set; } = 15;

    public int AutomationMaxIterations { get; set; } = 50;
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKodaClawRuntime(
        this IServiceCollection services,
        Action<KodaClawRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new KodaClawRuntimeOptions();
        configure?.Invoke(options);
        var snapshot = RuntimeConfigurationSnapshot.FromOptions(options);

        services.TryAddSingleton<IRuntimeConfigurationResolver>(
            new StaticRuntimeConfigurationResolver(snapshot));
        services.AddHttpClient(nameof(OpenAIProvider));
        services.AddHttpClient(nameof(AnthropicProvider))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler());
        services.TryAddSingleton<IRuntimeModelProviderFactory, DefaultRuntimeModelProviderFactory>();
        services.TryAddSingleton<DynamicModelProvider>();
        services.TryAddSingleton<IModelProvider, RegistryAwareModelProvider>();

        services.AddAgentSdk();

        services.TryAddSingleton(new MainSessionOptions
        {
            Model = options.DefaultModel ?? string.Empty,
            SystemPrompt = options.SystemPrompt ?? "You are KodaClaw main assistant.",
            MaxIterations = options.MainMaxIterations,
        });
        services.TryAddSingleton(new BootstrapDraftOptions
        {
            Model = options.DefaultModel ?? string.Empty,
            SystemPrompt = options.SystemPrompt ?? "You are KodaClaw bootstrap assistant.",
        });
        services.TryAddSingleton(new AutomationSessionOptions
        {
            Model = options.DefaultModel ?? string.Empty,
            SystemPrompt = options.SystemPrompt ?? "You are KodaClaw automation assistant.",
            MaxIterations = options.AutomationMaxIterations,
        });
        services.TryAddSingleton(new ChannelSessionOptions
        {
            Model = options.DefaultModel ?? string.Empty,
            SystemPrompt = options.SystemPrompt ?? "You are KodaClaw channel assistant.",
            MaxIterations = options.ChannelMaxIterations,
        });
        services.TryAddSingleton<IMainSessionAgentDependenciesFactory>(sp =>
        {
            var toolRegistry = sp.GetRequiredService<IToolRegistry>();
            toolRegistry.RegisterBuiltinTools();

            var workspaceService = sp.GetRequiredService<IWorkspaceService>();
            toolRegistry.Register("get_current_datetime",
                _ => new GetCurrentDateTimeTool());

            var correlationContextAccessor = sp.GetService<ICorrelationContextAccessor>();
            var diagnosticsService = sp.GetService<IDiagnosticsService>();

            toolRegistry.Register("workspace_memory_append",
                _ => new WorkspaceMemoryAppendTool(workspaceService, diagnosticsService));
            toolRegistry.Register("workspace_protocol_update",
                _ => new WorkspaceProtocolUpdateTool(workspaceService, diagnosticsService));

            var canvasRepository = sp.GetRequiredService<ICanvasArtifactRepository>();
            toolRegistry.Register("canvas_upsert",
                _ => new CanvasUpsertTool(workspaceService, canvasRepository, correlationContextAccessor, diagnosticsService));

            var inboxRepository = sp.GetRequiredService<IInboxRepository>();
            toolRegistry.Register("inbox_create",
                _ => new InboxCreateTool(inboxRepository, correlationContextAccessor, diagnosticsService));
            toolRegistry.Register("inbox_read",
                _ => new InboxReadTool(inboxRepository));
            var bindingRepository = sp.GetService<IThreadBindingRepository>();
            toolRegistry.Register("workspace_read",
                _ => new WorkspaceReadTool(workspaceService, bindingRepository));

            var channelSendService = sp.GetService<IChannelSendService>();
            if (channelSendService is not null)
            {
                toolRegistry.Register("channel_send",
                    _ => new ChannelSendTool(channelSendService));
            }

            if (bindingRepository is not null)
            {
                toolRegistry.Register("channel_list",
                    _ => new ChannelListTool(bindingRepository));
            }

            var generationService = sp.GetService<KodaClaw.ModelHub.IGenerationService>();
            if (generationService is not null)
            {
                toolRegistry.Register("generate_image",
                    _ => new GenerateImageTool(generationService, workspaceService, canvasRepository, correlationContextAccessor, diagnosticsService));
            }

            var speechService = sp.GetService<KodaClaw.ModelHub.ISpeechService>();
            if (speechService is not null)
            {
                toolRegistry.Register("generate_speech",
                    _ => new GenerateSpeechTool(speechService, diagnosticsService));
            }

            if (diagnosticsService is not null)
            {
                toolRegistry.Register("diagnostics_query",
                    _ => new DiagnosticsQueryTool(diagnosticsService, correlationContextAccessor));
            }

            return new DefaultMainSessionAgentDependenciesFactory(new MainSessionDependencies
            {
                ModelProvider = sp.GetRequiredService<IModelProvider>(),
                ToolRegistry = toolRegistry,
                SandboxFactory = sp.GetService<ISandboxFactory>(),
                LoggerFactory = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>(),
            });
        });
        services.TryAddSingleton<IMemorySessionSummaryService, MemorySessionSummaryService>();
        services.TryAddSingleton<IMemoryConsolidationService, MemoryConsolidationService>();
        services.TryAddSingleton<IMainSessionService, MainSessionService>();
        services.TryAddSingleton<IBootstrapDraftService, BootstrapDraftService>();
        services.TryAddSingleton<IAutomationSessionService, AutomationSessionService>();
        services.TryAddSingleton<IChannelSessionService, ChannelSessionService>();
        services.TryAddSingleton<IChatSessionService, ChatSessionService>();
        return services;
    }
}
