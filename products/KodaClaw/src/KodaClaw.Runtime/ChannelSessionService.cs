using System.Text;
using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using AgentRuntime = Kode.Agent.Sdk.Core.Agent.Agent;

namespace KodaClaw.Runtime;

public sealed class ChannelSessionService : IChannelSessionService, IAsyncDisposable
{
    private const string ThreadSummaryFileName = "SUMMARY.md";

    private readonly IWorkspaceService _workspaceService;
    private readonly IMainSessionAgentDependenciesFactory _dependenciesFactory;
    private readonly ChannelSessionOptions _options;
    private readonly IRuntimeConfigurationResolver? _runtimeConfigurationResolver;
    private readonly Dictionary<string, IAgent> _agents = new(StringComparer.Ordinal);

    public ChannelSessionService(
        IWorkspaceService workspaceService,
        IMainSessionAgentDependenciesFactory dependenciesFactory,
        ChannelSessionOptions? options = null,
        IRuntimeConfigurationResolver? runtimeConfigurationResolver = null)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _dependenciesFactory = dependenciesFactory ?? throw new ArgumentNullException(nameof(dependenciesFactory));
        _options = options ?? new ChannelSessionOptions();
        _runtimeConfigurationResolver = runtimeConfigurationResolver;
    }

    public async Task<ChannelSessionHandle> EnsureChannelSessionAsync(
        ThreadBinding binding,
        ChannelPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(policy);
        ValidateBindingAndPolicy(binding, policy);

        var snapshot = await _workspaceService.EnsureInitializedAsync(cancellationToken);
        var contextDocuments = await LoadContextDocumentsAsync(snapshot.RootPath, binding, policy, cancellationToken);
        var systemPrompt = BuildSystemPrompt(binding, policy, contextDocuments);
        var sessionDirectory = _workspaceService.GetSessionDirectory(binding.SessionId);

        if (_agents.TryGetValue(binding.SessionId, out var cached))
        {
            return CreateHandle(
                binding,
                sessionDirectory,
                resumedFromStore: false,
                resumeFailureMessage: null,
                cached);
        }

        Directory.CreateDirectory(sessionDirectory);

        var dependencies = _dependenciesFactory.Create(binding.SessionId, sessionDirectory);
        if (await dependencies.Store.ExistsAsync(binding.SessionId, cancellationToken))
        {
            var configuredModel = ResolveConfiguredModel();
            try
            {
                var resumed = await AgentRuntime.ResumeFromStoreAsync(
                    binding.SessionId,
                    dependencies,
                    options: new ResumeOptions { Strategy = RecoveryStrategy.Crash },
                    overrides: new AgentConfigOverrides
                    {
                        Model = configuredModel,
                        SystemPrompt = systemPrompt,
                        Tools = _options.Tools,
                        Permissions = _options.Permissions,
                    },
                    cancellationToken: cancellationToken);

                _agents[binding.SessionId] = resumed;
                return CreateHandle(
                    binding,
                    sessionDirectory,
                    resumedFromStore: true,
                    resumeFailureMessage: null,
                    resumed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
            {
                var createdAfterFallback = await AgentRuntime.CreateAsync(
                    binding.SessionId,
                    CreateAgentConfig(sessionDirectory, systemPrompt, configuredModel),
                    dependencies,
                    cancellationToken);

                _agents[binding.SessionId] = createdAfterFallback;
                return CreateHandle(
                    binding,
                    sessionDirectory,
                    resumedFromStore: false,
                    resumeFailureMessage: FormatResumeFailureMessage(ex),
                    createdAfterFallback);
            }
        }

        var initialModel = ResolveConfiguredModel();
        var created = await AgentRuntime.CreateAsync(
            binding.SessionId,
            CreateAgentConfig(sessionDirectory, systemPrompt, initialModel),
            dependencies,
            cancellationToken);

        _agents[binding.SessionId] = created;
        return CreateHandle(
            binding,
            sessionDirectory,
            resumedFromStore: false,
            resumeFailureMessage: null,
            created);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var agent in _agents.Values)
        {
            try
            {
                await agent.DisposeAsync();
            }
            catch (ObjectDisposedException)
            {
                // Reused runtime handles may already have released the agent.
            }
        }

        _agents.Clear();
    }

    private async Task<IReadOnlyList<ContextDocument>> LoadContextDocumentsAsync(
        string workspaceRoot,
        ThreadBinding binding,
        ChannelPolicy policy,
        CancellationToken cancellationToken)
    {
        var scope = ResolveEffectiveScope(policy);
        var documents = new List<ContextDocument>();
        var seenPaths = new HashSet<string>(GetPathComparer());
        var workspaceDirectory = Path.Combine(workspaceRoot, KodaClawWorkspaceLayout.WorkspaceDirectory);

        if (scope.LoadAgents)
        {
            await TryAddContextDocumentAsync(
                Path.Combine(workspaceDirectory, KodaClawWorkspaceLayout.AgentsFile),
                workspaceRoot,
                seenPaths,
                documents,
                cancellationToken);
        }

        if (scope.LoadIdentity)
        {
            await TryAddContextDocumentAsync(
                Path.Combine(workspaceDirectory, KodaClawWorkspaceLayout.IdentityFile),
                workspaceRoot,
                seenPaths,
                documents,
                cancellationToken);
        }

        if (scope.LoadSoul)
        {
            await TryAddContextDocumentAsync(
                Path.Combine(workspaceDirectory, KodaClawWorkspaceLayout.SoulFile),
                workspaceRoot,
                seenPaths,
                documents,
                cancellationToken);
        }

        if (scope.LoadUserProfile)
        {
            await TryAddContextDocumentAsync(
                Path.Combine(workspaceDirectory, KodaClawWorkspaceLayout.UserFile),
                workspaceRoot,
                seenPaths,
                documents,
                cancellationToken);
        }

        if (scope.LoadRecentThreadSummary)
        {
            await TryAddContextDocumentAsync(
                Path.Combine(
                    workspaceDirectory,
                    "channels",
                    binding.Id,
                    ThreadSummaryFileName),
                workspaceRoot,
                seenPaths,
                documents,
                cancellationToken);
        }

        return documents;
    }

    private static async Task TryAddContextDocumentAsync(
        string absolutePath,
        string workspaceRoot,
        HashSet<string> seenPaths,
        ICollection<ContextDocument> documents,
        CancellationToken cancellationToken)
    {
        if (!seenPaths.Add(absolutePath) || !File.Exists(absolutePath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(absolutePath, cancellationToken);
        documents.Add(new ContextDocument(ToDisplayPath(workspaceRoot, absolutePath), content));
    }

    private AgentConfig CreateAgentConfig(
        string sessionDirectory,
        string systemPrompt,
        string model)
    {
        return new AgentConfig
        {
            Model = model,
            SystemPrompt = systemPrompt,
            MaxIterations = _options.MaxIterations,
            Tools = _options.Tools,
            Permissions = _options.Permissions,
            SandboxOptions = new SandboxOptions
            {
                WorkingDirectory = sessionDirectory,
                EnforceBoundary = true,
            },
        };
    }

    private string ResolveConfiguredModel()
    {
        return RuntimeProviderSelector.ResolveModelOrThrow(
            _runtimeConfigurationResolver,
            _options.Model);
    }

    private string BuildSystemPrompt(
        ThreadBinding binding,
        ChannelPolicy policy,
        IReadOnlyList<ContextDocument> contextDocuments)
    {
        var scope = ResolveEffectiveScope(policy);
        var displayTitle = binding.ChannelIdentity.DisplayName
            ?? binding.ChannelIdentity.Username
            ?? binding.ExternalThreadId;

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(_options.SystemPrompt))
        {
            builder.AppendLine(_options.SystemPrompt);
            builder.AppendLine();
        }

        builder.AppendLine("Channel Session");
        builder.AppendLine($"BindingId: {binding.Id}");
        builder.AppendLine($"ConnectorKind: {binding.ConnectorKind}");
        builder.AppendLine($"AccountId: {binding.AccountId}");
        builder.AppendLine($"ExternalThreadId: {binding.ExternalThreadId}");
        builder.AppendLine($"ThreadType: {binding.ThreadType}");
        builder.AppendLine($"SessionKind: {binding.SessionKind}");
        builder.AppendLine($"DisplayTitle: {displayTitle}");
        builder.AppendLine();
        builder.AppendLine("Policy");
        builder.AppendLine($"- AllowDirectReply: {policy.AllowDirectReply}");
        builder.AppendLine($"- RequireExplicitMention: {policy.RequireExplicitMention}");
        builder.AppendLine($"- WorkspaceMuted: {policy.WorkspaceMuted}");
        builder.AppendLine($"- ConnectorMuted: {policy.ConnectorMuted}");
        builder.AppendLine($"- ThreadMuted: {policy.ThreadMuted}");
        builder.AppendLine($"- LoadAgents: {scope.LoadAgents}");
        builder.AppendLine($"- LoadIdentity: {scope.LoadIdentity}");
        builder.AppendLine($"- LoadSoul: {scope.LoadSoul}");
        builder.AppendLine($"- LoadUserProfile: {scope.LoadUserProfile}");
        builder.AppendLine($"- LoadLongTermMemory: {scope.LoadLongTermMemory}");
        builder.AppendLine($"- LoadRecentThreadSummary: {scope.LoadRecentThreadSummary}");
        builder.AppendLine();
        builder.AppendLine("Only use the loaded context files below. Do not assume access to main-session memory or undeclared user profile data.");
        builder.AppendLine();
        builder.AppendLine("Loaded Context Files:");

        if (contextDocuments.Count == 0)
        {
            builder.AppendLine("(none)");
        }
        else
        {
            foreach (var document in contextDocuments)
            {
                builder.AppendLine($"### File: {document.Path}");
                builder.AppendLine(document.Content);
                builder.AppendLine();
            }
        }

        return builder.ToString().Trim();
    }

    private static void ValidateBindingAndPolicy(ThreadBinding binding, ChannelPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(binding.Id))
        {
            throw new ArgumentException("Channel binding id is required.", nameof(binding));
        }

        if (string.IsNullOrWhiteSpace(binding.SessionId))
        {
            throw new ArgumentException("Channel binding session id is required.", nameof(binding));
        }

        var expectedSessionKind = binding.ThreadType switch
        {
            ChannelThreadType.DirectMessage => SessionKind.ChannelDirectMessage,
            ChannelThreadType.Group => SessionKind.ChannelGroup,
            _ => throw new ArgumentOutOfRangeException(nameof(binding), binding.ThreadType, null),
        };

        if (binding.SessionKind != expectedSessionKind)
        {
            throw new ArgumentException(
                $"Binding session kind '{binding.SessionKind}' does not match thread type '{binding.ThreadType}'.",
                nameof(binding));
        }

        if (policy.ThreadType != binding.ThreadType)
        {
            throw new ArgumentException(
                $"Channel policy thread type '{policy.ThreadType}' does not match binding thread type '{binding.ThreadType}'.",
                nameof(policy));
        }
    }

    private static EffectivePolicyScope ResolveEffectiveScope(ChannelPolicy policy)
    {
        var isDirectMessage = policy.ThreadType == ChannelThreadType.DirectMessage;

        // Iteration 5 freeze keeps channel sessions conservative even if a caller toggles
        // wider flags on the stored policy.
        return new EffectivePolicyScope(
            LoadAgents: policy.LoadAgents,
            LoadIdentity: policy.LoadIdentity,
            LoadSoul: policy.LoadSoul,
            LoadUserProfile: isDirectMessage && policy.LoadUserProfile,
            LoadLongTermMemory: false,
            LoadRecentThreadSummary: policy.LoadRecentThreadSummary);
    }

    private static string ToDisplayPath(string workspaceRoot, string absolutePath)
    {
        var relativePath = Path.GetRelativePath(workspaceRoot, absolutePath);
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string FormatResumeFailureMessage(Exception exception)
    {
        var root = exception.GetBaseException();
        return $"Resume failed: {root.GetType().Name}: {root.Message}";
    }

    private static IEqualityComparer<string> GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static ChannelSessionHandle CreateHandle(
        ThreadBinding binding,
        string sessionDirectory,
        bool resumedFromStore,
        string? resumeFailureMessage,
        IAgent agent)
    {
        return new ChannelSessionHandle(
            BindingId: binding.Id,
            SessionId: binding.SessionId,
            SessionKind: binding.SessionKind,
            SessionDirectory: sessionDirectory,
            ResumedFromStore: resumedFromStore,
            ResumeFailureMessage: resumeFailureMessage,
            Agent: agent);
    }

    private sealed record ContextDocument(string Path, string Content);

    private sealed record EffectivePolicyScope(
        bool LoadAgents,
        bool LoadIdentity,
        bool LoadSoul,
        bool LoadUserProfile,
        bool LoadLongTermMemory,
        bool LoadRecentThreadSummary);
}
