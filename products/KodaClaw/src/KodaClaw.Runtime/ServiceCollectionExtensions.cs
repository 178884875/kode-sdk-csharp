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

    public int MaxIterations { get; set; } = 8;
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
            MaxIterations = options.MaxIterations,
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
            MaxIterations = options.MaxIterations,
        });
        services.TryAddSingleton(new ChannelSessionOptions
        {
            Model = options.DefaultModel ?? string.Empty,
            SystemPrompt = options.SystemPrompt ?? "You are KodaClaw channel assistant.",
            MaxIterations = options.MaxIterations,
        });
        services.TryAddSingleton<IMainSessionAgentDependenciesFactory>(sp =>
        {
            var toolRegistry = sp.GetRequiredService<IToolRegistry>();
            toolRegistry.RegisterBuiltinTools();

            var workspaceService = sp.GetRequiredService<IWorkspaceService>();
            toolRegistry.Register("workspace_memory_append",
                _ => new WorkspaceMemoryAppendTool(workspaceService));
            toolRegistry.Register("workspace_protocol_update",
                _ => new WorkspaceProtocolUpdateTool(workspaceService));

            var canvasRepository = sp.GetRequiredService<ICanvasArtifactRepository>();
            toolRegistry.Register("canvas_upsert",
                _ => new CanvasUpsertTool(workspaceService, canvasRepository));

            var inboxRepository = sp.GetRequiredService<IInboxRepository>();
            toolRegistry.Register("inbox_create",
                _ => new InboxCreateTool(inboxRepository));
            toolRegistry.Register("inbox_read",
                _ => new InboxReadTool(inboxRepository));
            toolRegistry.Register("workspace_read",
                _ => new WorkspaceReadTool(workspaceService));

            var channelSendService = sp.GetService<IChannelSendService>();
            if (channelSendService is not null)
            {
                toolRegistry.Register("channel_send",
                    _ => new ChannelSendTool(channelSendService));
            }

            var bindingRepository = sp.GetService<IThreadBindingRepository>();
            if (bindingRepository is not null)
            {
                toolRegistry.Register("channel_list",
                    _ => new ChannelListTool(bindingRepository));
            }

            var generationService = sp.GetService<KodaClaw.ModelHub.IGenerationService>();
            if (generationService is not null)
            {
                toolRegistry.Register("generate_image",
                    _ => new GenerateImageTool(generationService, workspaceService, canvasRepository));
            }

            return new DefaultMainSessionAgentDependenciesFactory(new MainSessionDependencies
            {
                ModelProvider = sp.GetRequiredService<IModelProvider>(),
                ToolRegistry = toolRegistry,
                SandboxFactory = sp.GetService<ISandboxFactory>(),
                LoggerFactory = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>(),
            });
        });
        services.TryAddSingleton<IMainSessionService, MainSessionService>();
        services.TryAddSingleton<IBootstrapDraftService, BootstrapDraftService>();
        services.TryAddSingleton<IAutomationSessionService, AutomationSessionService>();
        services.TryAddSingleton<IChannelSessionService, ChannelSessionService>();
        services.TryAddSingleton<IChatSessionService, ChatSessionService>();
        return services;
    }
}
