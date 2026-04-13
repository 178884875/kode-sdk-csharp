using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Infrastructure.Providers;
using Kode.Agent.Sdk.Infrastructure.Sandbox;
using Kode.Agent.Sdk.Tools;
using Kode.Agent.Store.Json;
using Kode.Agent.Tools.Builtin;
using Kode.Agent.Sdk.Core.Templates;
using Kode.Agent.Tools.Builtin.FileSystem;
using Kode.Agent.Tools.Builtin.Shell;
using Kode.Agent.Tools.Builtin.Todo;
using Microsoft.Extensions.Logging;

namespace Kode.Agent.Examples.Shared;

/// <summary>
/// Factory for creating agent runtime dependencies.
/// </summary>
public static class RuntimeFactory
{
    public enum BuiltinGroup
    {
        Fs,
        Bash,
        Todo
    }

    public static string GetDefaultModelId()
    {
        var provider = EnvLoader.Get("DEFAULT_PROVIDER", "anthropic")?.Trim().ToLowerInvariant();
        return provider switch
        {
            "openai" => EnvLoader.Get("OPENAI_MODEL_ID", "gpt-4o"),
            _ => EnvLoader.Get("ANTHROPIC_MODEL_ID", "claude-sonnet-4-20250514")
        };
    }

    public sealed record RuntimeContext
    {
        public required AgentTemplateRegistry Templates { get; init; }
        public required ToolRegistry Tools { get; init; }
        public required Action<BuiltinGroup[]> RegisterBuiltin { get; init; }
    }

    /// <summary>
    /// Creates agent dependencies with default configuration.
    /// </summary>
    public static AgentDependencies CreateRuntime(
        string? storeDir = null,
        Action<RuntimeContext>? configure = null)
    {
        EnvLoader.Load();

        // Create store
        var store = new JsonAgentStore(storeDir ?? "./.kode");

        // Create template registry (TS-aligned)
        var templates = new AgentTemplateRegistry();

        // Create tool registry (TS-aligned ToolRegistry)
        var toolRegistry = new ToolRegistry();

        void RegisterBuiltin(params BuiltinGroup[] groups)
        {
            if (groups.Length == 0)
            {
                toolRegistry.RegisterBuiltinTools();
                return;
            }

            foreach (var group in groups.Distinct())
            {
                switch (group)
                {
                    case BuiltinGroup.Fs:
                        toolRegistry.Register(new FsReadTool());
                        toolRegistry.Register(new FsWriteTool());
                        toolRegistry.Register(new FsGlobTool());
                        toolRegistry.Register(new FsGrepTool());
                        toolRegistry.Register(new FsEditTool());
                        toolRegistry.Register(new FsRmTool());
                        toolRegistry.Register(new FsListTool());
                        break;
                    case BuiltinGroup.Bash:
                        toolRegistry.Register(new BashRunTool());
                        toolRegistry.Register(new BashKillTool());
                        toolRegistry.Register(new BashLogsTool());
                        break;
                    case BuiltinGroup.Todo:
                        toolRegistry.Register(new TodoReadTool());
                        toolRegistry.Register(new TodoWriteTool());
                        break;
                }
            }
        }

        if (configure == null)
        {
            // Default: register all builtins unless caller customizes.
            RegisterBuiltin();
        }
        else
        {
            configure(new RuntimeContext
            {
                Templates = templates,
                Tools = toolRegistry,
                RegisterBuiltin = RegisterBuiltin
            });
        }

        // Create sandbox factory
        var sandboxFactory = new DefaultSandboxFactory();

        // Create model provider based on configuration
        var modelProvider = CreateModelProvider();

        // Create logger factory
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        return new AgentDependencies
        {
            Store = store,
            ToolRegistry = toolRegistry,
            SandboxFactory = sandboxFactory,
            ModelProvider = modelProvider,
            TemplateRegistry = templates,
            LoggerFactory = loggerFactory
        };
    }

    /// <summary>
    /// Creates a model provider based on environment configuration.
    /// </summary>
    public static IModelProvider CreateModelProvider()
    {
        var provider = EnvLoader.Get("DEFAULT_PROVIDER", "anthropic");
        var httpClient = new HttpClient();

        return provider.ToLowerInvariant() switch
        {
            "openai" => CreateOpenAiProvider(httpClient),
            "openai-responses" => CreateOpenAiResponsesProvider(httpClient),
            _ => CreateAnthropicProvider(httpClient)
        };
    }

    private static AnthropicProvider CreateAnthropicProvider(HttpClient httpClient)
    {
        var apiKey = EnvLoader.Get("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is required");

        var options = new AnthropicOptions
        {
            ApiKey = apiKey,
            BaseUrl = EnvLoader.Get("ANTHROPIC_BASE_URL"),
            EnableBetaFeatures = EnvLoader.Get("ANTHROPIC_ENABLE_BETA", "false").Equals("true", StringComparison.OrdinalIgnoreCase)
        };

        return new AnthropicProvider(httpClient, options);
    }

    private static OpenAIProvider CreateOpenAiProvider(HttpClient httpClient)
    {
        var apiKey = EnvLoader.Get("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is required");

        var options = new OpenAIOptions
        {
            ApiKey = apiKey,
            BaseUrl = EnvLoader.Get("OPENAI_BASE_URL"),
            Organization = EnvLoader.Get("OPENAI_ORGANIZATION")
        };

        return new OpenAIProvider(httpClient, options);
    }

    private static OpenAIResponsesProvider CreateOpenAiResponsesProvider(HttpClient httpClient)
    {
        var apiKey = EnvLoader.Get("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is required");

        var options = new OpenAIResponsesOptions
        {
            ApiKey = apiKey,
            BaseUrl = EnvLoader.Get("OPENAI_BASE_URL")
        };

        return new OpenAIResponsesProvider(httpClient, options);
    }
}
