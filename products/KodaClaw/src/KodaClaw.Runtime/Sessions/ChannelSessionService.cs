using KodaClaw.Contracts;
using KodaClaw.McpHub;
using KodaClaw.ModelHub;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Context;
using Kode.Agent.Sdk.Core.Skills;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Store.Json;
using AgentRuntime = Kode.Agent.Sdk.Core.Agent.Agent;
using System.Collections.Concurrent;

namespace KodaClaw.Runtime;

public sealed class ChannelSessionService : IChannelSessionService, IAsyncDisposable
{
    private const string ThreadSummaryFileName = "SUMMARY.md";

    private const string DiagnosticSource = "channel_session";

    private readonly IWorkspaceService _workspaceService;
    private readonly IMainSessionAgentDependenciesFactory _dependenciesFactory;
    private readonly ChannelSessionOptions _options;
    private readonly IRuntimeConfigurationResolver? _runtimeConfigurationResolver;
    private readonly IModelRegistryRepository? _modelRegistryRepository;
    private readonly IMcpHubService? _mcpHubService;
    private readonly IThreadBindingRepository? _threadBindingRepository;
    private readonly IMemorySessionSummaryService? _sessionSummaryService;
    private readonly IDiagnosticsService? _diagnosticsService;
    private readonly ISettingsRepository? _settingsRepository;
    private readonly Dictionary<string, IAgent> _agents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new(StringComparer.Ordinal);

    public ChannelSessionService(
        IWorkspaceService workspaceService,
        IMainSessionAgentDependenciesFactory dependenciesFactory,
        ChannelSessionOptions? options = null,
        IRuntimeConfigurationResolver? runtimeConfigurationResolver = null,
        IModelRegistryRepository? modelRegistryRepository = null,
        IMcpHubService? mcpHubService = null,
        IThreadBindingRepository? threadBindingRepository = null,
        IMemorySessionSummaryService? sessionSummaryService = null,
        IDiagnosticsService? diagnosticsService = null,
        ISettingsRepository? settingsRepository = null)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _dependenciesFactory = dependenciesFactory ?? throw new ArgumentNullException(nameof(dependenciesFactory));
        _options = options ?? new ChannelSessionOptions();
        _runtimeConfigurationResolver = runtimeConfigurationResolver;
        _modelRegistryRepository = modelRegistryRepository;
        _mcpHubService = mcpHubService;
        _threadBindingRepository = threadBindingRepository;
        _sessionSummaryService = sessionSummaryService;
        _diagnosticsService = diagnosticsService;
        _settingsRepository = settingsRepository;
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
        var promptCharBudget = await ResolvePromptCharacterBudgetAsync(cancellationToken);
        var prompt = BuildSystemPrompt(binding, policy, contextDocuments, promptCharBudget);
        var systemPrompt = prompt.SystemPrompt;
        var sessionDirectory = _workspaceService.GetSessionDirectory(binding.SessionId);

        if (_agents.TryGetValue(binding.SessionId, out var cached))
        {
            await SessionPromptReportStore.WriteAsync(sessionDirectory, prompt, cancellationToken);
            return CreateHandle(
                binding,
                sessionDirectory,
                resumedFromStore: false,
                resumeFailureMessage: null,
                cached);
        }

        Directory.CreateDirectory(sessionDirectory);
        await SessionPromptReportStore.WriteAsync(sessionDirectory, prompt, cancellationToken);

        var dependencies = _dependenciesFactory.Create(binding.SessionId, sessionDirectory);

        // KC-2201: Session timeout policy — skip resume if the thread has been inactive
        // for more than SessionTimeoutDays days. This avoids stale context from old sessions.
        // KC-5002: DM sessions never time out — continuity is the value, same as the main session.
        // Only Group sessions are subject to the timeout, as group context becomes stale.
        var isSessionTimedOut = binding.ThreadType == ChannelThreadType.Group
            && _options.SessionTimeoutDays > 0
            && binding.LastInboundAt.HasValue
            && binding.LastInboundAt.Value < DateTimeOffset.UtcNow.AddDays(-_options.SessionTimeoutDays);

        if (isSessionTimedOut)
        {
            RecordDiagnosticEvent(
                eventType: "channel_session.timeout",
                level: "warning",
                message: $"Channel session timed out (inactive >{_options.SessionTimeoutDays}d), will create fresh: bindingId={binding.Id}",
                sessionId: binding.SessionId);
        }

        var maxIterations = await ResolveMaxIterationsAsync(cancellationToken);

        if (!isSessionTimedOut && await dependencies.Store.ExistsAsync(binding.SessionId, cancellationToken))
        {
            var configuredModel = await ResolveConfiguredModelAsync(cancellationToken);
            var resumeTools = await BuildSessionToolsAsync(binding.SessionId, dependencies.ToolRegistry, binding.ThreadType, cancellationToken);
            var skillsPaths = _workspaceService.GetSkillsPaths();
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
                        Tools = resumeTools,
                        Permissions = (_options.Permissions ?? new PermissionConfig()) with { SchemaHiddenTools = BuiltinSkills.SkillGatedTools },
                        SandboxOptions = new SandboxOptions
                        {
                            // KC-5003: DM sessions use workspace root; Group sessions stay isolated.
                            WorkingDirectory = binding.ThreadType == ChannelThreadType.DirectMessage
                                ? _workspaceService.RootPath
                                : sessionDirectory,
                            EnforceBoundary = true,
                            AllowPaths = skillsPaths,
                        },
                        Skills = new SkillsConfig
                        {
                            Paths = skillsPaths,
                            ValidateOnLoad = false,
                            AutoActivate = BuiltinSkills.ChannelAutoActivate,
                        },
                        Context = new ContextManagerOptions
                        {
                            MaxTokens = (int)(_options.DefaultContextWindowSize * _options.ContextCompressionTriggerRatio),
                            CompressToTokens = (int)(_options.DefaultContextWindowSize * _options.ContextCompressionTargetRatio),
                        },
                    },
                    cancellationToken: cancellationToken);

                _agents[binding.SessionId] = resumed;
                RecordDiagnosticEvent(
                    eventType: "channel_session.resumed",
                    level: "info",
                    message: $"Channel session resumed from store: bindingId={binding.Id}",
                    sessionId: binding.SessionId);
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
                    CreateAgentConfig(sessionDirectory, systemPrompt, configuredModel, resumeTools, isDirectMessage: binding.ThreadType == ChannelThreadType.DirectMessage, maxIterations: maxIterations),
                    dependencies,
                    cancellationToken);

                _agents[binding.SessionId] = createdAfterFallback;
                RecordDiagnosticEvent(
                    eventType: "channel_session.resume_fallback",
                    level: "warning",
                    message: $"Channel session resume failed, created fresh: bindingId={binding.Id} error={ex.GetBaseException().Message}",
                    sessionId: binding.SessionId);
                return CreateHandle(
                    binding,
                    sessionDirectory,
                    resumedFromStore: false,
                    resumeFailureMessage: FormatResumeFailureMessage(ex),
                    createdAfterFallback);
            }
        }

        var initialModel = await ResolveConfiguredModelAsync(cancellationToken);
        var sessionTools = await BuildSessionToolsAsync(binding.SessionId, dependencies.ToolRegistry, binding.ThreadType, cancellationToken);
        var created = await AgentRuntime.CreateAsync(
            binding.SessionId,
            CreateAgentConfig(sessionDirectory, systemPrompt, initialModel, sessionTools, isDirectMessage: binding.ThreadType == ChannelThreadType.DirectMessage, maxIterations: maxIterations),
            dependencies,
            cancellationToken);

        _agents[binding.SessionId] = created;
        RecordDiagnosticEvent(
            eventType: "channel_session.created",
            level: "info",
            message: $"Channel session created: bindingId={binding.Id}",
            sessionId: binding.SessionId);
        return CreateHandle(
            binding,
            sessionDirectory,
            resumedFromStore: false,
            resumeFailureMessage: null,
            created);
    }

    public async Task<ChannelTurnExecutionResult> RunInboundTurnAsync(
        ThreadBinding binding,
        ChannelPolicy policy,
        ChannelEventEnvelope envelope,
        bool hasExplicitMention,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var handle = await EnsureChannelSessionAsync(binding, policy, cancellationToken);
        var prompt = BuildInboundTurnPrompt(binding, envelope, hasExplicitMention);
        AgentRunResult runResult;
        var sessionLock = _sessionLocks.GetOrAdd(handle.SessionId, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(cancellationToken);
        try
        {
            runResult = await handle.Agent.RunAsync(prompt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
        {
            RecordDiagnosticEvent(
                eventType: "channel_session.turn_failed",
                level: "error",
                message: $"Channel turn failed: bindingId={binding.Id} error={ex.GetBaseException().Message}",
                sessionId: binding.SessionId);
            throw;
        }
        finally
        {
            sessionLock.Release();
        }

        var turnLevel = runResult.StopReason == StopReason.Error ? "warning" : runResult.StopReason == StopReason.EndTurn ? "debug" : "info";
        RecordDiagnosticEvent(
            eventType: "channel_session.turn_completed",
            level: turnLevel,
            message: $"Channel turn completed: bindingId={binding.Id} stopReason={runResult.StopReason}",
            sessionId: binding.SessionId);

        return new ChannelTurnExecutionResult(
            Session: handle,
            RunResult: runResult,
            RawResponse: runResult.Response ?? string.Empty,
            Proposal: null,
            HasExplicitMention: hasExplicitMention);
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

        foreach (var semaphore in _sessionLocks.Values)
        {
            semaphore.Dispose();
        }

        _sessionLocks.Clear();
    }

    private async Task<IReadOnlyList<PromptContextDocument>> LoadContextDocumentsAsync(
        string workspaceRoot,
        ThreadBinding binding,
        ChannelPolicy policy,
        CancellationToken cancellationToken)
    {
        var scope = ResolveEffectiveScope(policy);
        var documents = new List<PromptContextDocument>();
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
            await TryAddContextDocumentAsync(
                Path.Combine(workspaceDirectory, KodaClawWorkspaceLayout.OntologyFile),
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

        // KC-5001: DM sessions load long-term memory (MEMORY.md) — same as main session.
        if (scope.LoadLongTermMemory)
        {
            await TryAddContextDocumentAsync(
                Path.Combine(workspaceDirectory, KodaClawWorkspaceLayout.MemoryFile),
                workspaceRoot,
                seenPaths,
                documents,
                cancellationToken);

            // Load yesterday's and today's daily memory for session continuity.
            var today = DateTimeOffset.Now.Date;
            await TryAddContextDocumentAsync(
                Path.Combine(workspaceDirectory, "memory", $"{today.AddDays(-1):yyyy-MM-dd}.md"),
                workspaceRoot,
                seenPaths,
                documents,
                cancellationToken);
            await TryAddContextDocumentAsync(
                Path.Combine(workspaceDirectory, "memory", $"{today:yyyy-MM-dd}.md"),
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
        ICollection<PromptContextDocument> documents,
        CancellationToken cancellationToken)
    {
        if (!seenPaths.Add(absolutePath) || !File.Exists(absolutePath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(absolutePath, cancellationToken);
        documents.Add(new PromptContextDocument(ToDisplayPath(workspaceRoot, absolutePath), content));
    }

    public async Task<string> RotateSessionAsync(ThreadBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (_agents.TryGetValue(binding.SessionId, out var agent))
        {
            await TryGenerateChannelSessionSummaryAsync(binding, cancellationToken);
            _agents.Remove(binding.SessionId);
            if (_sessionLocks.TryRemove(binding.SessionId, out var removedLock))
            {
                removedLock.Dispose();
            }
            await agent.DisposeAsync();
        }

        var newSessionId = GenerateChannelSessionId(binding.ConnectorKind, binding.ThreadType);

        if (_threadBindingRepository is not null)
        {
            await _threadBindingRepository.UpsertAsync(
                binding with { SessionId = newSessionId, UpdatedAt = DateTimeOffset.UtcNow },
                cancellationToken);
        }

        return newSessionId;
    }

    private async Task TryGenerateChannelSessionSummaryAsync(
        ThreadBinding binding,
        CancellationToken cancellationToken)
    {
        if (_sessionSummaryService is null)
        {
            return;
        }

        try
        {
            var sessionDirectory = _workspaceService.GetSessionDirectory(binding.SessionId);
            var sessionsRoot = Directory.GetParent(sessionDirectory)?.FullName ?? sessionDirectory;
            var store = new JsonAgentStore(sessionsRoot);

            var messages = await store.LoadMessagesAsync(binding.SessionId, cancellationToken);
            if (messages.Count == 0)
            {
                return;
            }

            var context = new MemorySessionSummaryContext(
                SessionId: binding.SessionId,
                SessionType: "channel",
                BindingId: binding.Id,
                Messages: messages);

            await _sessionSummaryService.GenerateSummaryAsync(context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Let cancellation propagate
        }
        catch
        {
            // Summary generation failure must not block session rotation
        }
    }

    private static string GenerateChannelSessionId(ChannelConnectorKind connectorKind, ChannelThreadType threadType)
    {
        var kind = connectorKind.ToString().ToLowerInvariant();
        var thread = threadType == ChannelThreadType.DirectMessage ? "dm" : "group";
        return $"channel-{kind}-{thread}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    private async Task<IReadOnlyList<string>> BuildSessionToolsAsync(
        string sessionId,
        IToolRegistry? toolRegistry,
        ChannelThreadType threadType,
        CancellationToken cancellationToken)
    {
        var tools = new List<string>(_options.Tools);

        // KC-5003: DM sessions get workspace write-back tools — owner trust level equals main session.
        if (threadType == ChannelThreadType.DirectMessage)
        {
            var dmTools = new HashSet<string>(tools, StringComparer.OrdinalIgnoreCase);
            if (dmTools.Add("workspace_protocol_update")) tools.Add("workspace_protocol_update");
            if (dmTools.Add("workspace_memory_append")) tools.Add("workspace_memory_append");
        }

        if (_mcpHubService is null || toolRegistry is null)
        {
            return tools;
        }

        var merged = new HashSet<string>(tools, StringComparer.OrdinalIgnoreCase);
        var sessionKind = threadType == ChannelThreadType.DirectMessage
            ? SessionKind.ChannelDirectMessage
            : SessionKind.ChannelGroup;
        var mcpResult = await _mcpHubService.InjectToolsAsync(sessionId, sessionKind, toolRegistry, cancellationToken);
        foreach (var toolName in mcpResult.InjectedToolNames)
        {
            if (merged.Add(toolName))
            {
                tools.Add(toolName);
            }
        }

        return tools;
    }

    private async Task<int> ResolveMaxIterationsAsync(CancellationToken cancellationToken)
    {
        if (_settingsRepository is null) return _options.MaxIterations;
        var settings = await _settingsRepository.GetAsync(cancellationToken);
        return settings.ChannelMaxIterations ?? _options.MaxIterations;
    }

    private AgentConfig CreateAgentConfig(
        string sessionDirectory,
        string systemPrompt,
        string model,
        IReadOnlyList<string>? tools = null,
        bool isDirectMessage = false,
        int? maxIterations = null)
    {
        var skillsPaths = _workspaceService.GetSkillsPaths();
        // KC-5003: DM sessions use workspace root as sandbox — same trust boundary as the main session.
        // Group sessions remain isolated to their sessionDirectory.
        var workingDirectory = isDirectMessage ? _workspaceService.RootPath : sessionDirectory;
        return new AgentConfig
        {
            Model = model,
            SystemPrompt = systemPrompt,
            MaxIterations = maxIterations ?? _options.MaxIterations,
            Tools = tools ?? _options.Tools,
            Permissions = _options.Permissions,
            SandboxOptions = new SandboxOptions
            {
                WorkingDirectory = workingDirectory,
                EnforceBoundary = true,
                AllowPaths = skillsPaths,
            },
            Skills = new SkillsConfig
            {
                Paths = skillsPaths,
                ValidateOnLoad = false,
                AutoActivate = BuiltinSkills.ChannelAutoActivate,
            },
            Context = new ContextManagerOptions
            {
                MaxTokens = (int)(_options.DefaultContextWindowSize * _options.ContextCompressionTriggerRatio),
                CompressToTokens = (int)(_options.DefaultContextWindowSize * _options.ContextCompressionTargetRatio),
            },
        };
    }

    private Task<string> ResolveConfiguredModelAsync(CancellationToken cancellationToken) =>
        RuntimeProviderSelector.ResolveModelOrFallbackAsync(
            _runtimeConfigurationResolver,
            _options.Model,
            _modelRegistryRepository,
            cancellationToken);

    private async Task<int> ResolvePromptCharacterBudgetAsync(CancellationToken cancellationToken)
    {
        if (_modelRegistryRepository is null) return _options.MaxPromptCharacters;
        try
        {
            var endpoint = await _modelRegistryRepository.ResolveDefaultForAsync(
                ModelCapabilitySet.TextChat | ModelCapabilitySet.ToolCalling, cancellationToken);
            if (endpoint is null) return _options.MaxPromptCharacters;
            var usableTokens = Math.Max(endpoint.ContextWindowSize - endpoint.MaxOutputTokens, 0);
            return Math.Max(usableTokens / 5 * 4, _options.MaxPromptCharacters);
        }
        catch
        {
            return _options.MaxPromptCharacters;
        }
    }

    private PromptBuildResult BuildSystemPrompt(
        ThreadBinding binding,
        ChannelPolicy policy,
        IReadOnlyList<PromptContextDocument> contextDocuments,
        int promptCharBudget)
    {
        var scope = ResolveEffectiveScope(policy);
        var displayTitle = binding.ChannelIdentity.DisplayName
            ?? binding.ChannelIdentity.Username
            ?? binding.ExternalThreadId;

        var sessionStartedAt = DateTimeOffset.Now;
        var prompt = new PromptBuilder(PromptProfiles.Channel(binding.ThreadType, _options.SystemPrompt))
            .WithCharacterBudget(promptCharBudget)
            .AddBody($"Session started at: {sessionStartedAt:yyyy-MM-dd HH:mm:ss zzz} ({sessionStartedAt.DayOfWeek}).")
            .AddSection("Runtime Environment", RuntimeEnvironmentContext.BuildLines(_workspaceService.RootPath))
            .AddSection(
                "Channel Session",
                [
                    $"BindingId: {binding.Id}",
                    $"ConnectorKind: {binding.ConnectorKind}",
                    $"AccountId: {binding.AccountId}",
                    $"ExternalThreadId: {binding.ExternalThreadId}",
                    $"ThreadType: {binding.ThreadType}",
                    $"SessionKind: {binding.SessionKind}",
                    $"DisplayTitle: {displayTitle}",
                ])
            .AddSection(
                "Policy",
                [
                    $"- AllowDirectReply: {policy.AllowDirectReply}",
                    $"- RequireExplicitMention: {policy.RequireExplicitMention}",
                    $"- WorkspaceMuted: {policy.WorkspaceMuted}",
                    $"- ConnectorMuted: {policy.ConnectorMuted}",
                    $"- ThreadMuted: {policy.ThreadMuted}",
                    $"- LoadAgents: {scope.LoadAgents}",
                    $"- LoadIdentity: {scope.LoadIdentity}",
                    $"- LoadSoul: {scope.LoadSoul}",
                    $"- LoadUserProfile: {scope.LoadUserProfile}",
                    $"- LoadLongTermMemory: {scope.LoadLongTermMemory}",
                    $"- LoadRecentThreadSummary: {scope.LoadRecentThreadSummary}",
                ])
            .AddBody("Only use the loaded context files below. Do not assume access to main-session memory or undeclared user profile data.")
            .AddContextDocuments(contextDocuments)
            .Build();

        return prompt;
    }

    private static string BuildInboundTurnPrompt(
        ThreadBinding binding,
        ChannelEventEnvelope envelope,
        bool hasExplicitMention)
    {
        var senderLabel = envelope.Sender?.DisplayName
            ?? envelope.Sender?.Username
            ?? envelope.Sender?.Id
            ?? "(unknown)";
        var recipientLabel = envelope.Recipient?.DisplayName
            ?? envelope.Recipient?.Username
            ?? envelope.Recipient?.Id
            ?? "(unknown)";
        var messageText = string.IsNullOrWhiteSpace(envelope.Text)
            ? "(no text)"
            : envelope.Text.Trim();
        var threadGuidance = binding.ThreadType switch
        {
            ChannelThreadType.DirectMessage => """
- This is a direct message. Process the user's request using available tools, then use channel_send to reply.
- Use the loaded user profile only when it is explicitly present in the session context.
""",
            ChannelThreadType.Group when hasExplicitMention => """
- This is a group thread and Koda was explicitly mentioned.
- Process the request using available tools, then use channel_send if a reply is useful.
- Keep the reply brief, public-safe, and grounded only in the loaded context.
""",
            ChannelThreadType.Group => """
- This is a group thread without an explicit mention of Koda.
- Do not send a reply unless the message clearly requires Koda's intervention.
""",
            _ => string.Empty,
        };

        return $$"""
Process the inbound channel event below. You are in Full Agent Mode — you can use all available tools.

{{threadGuidance}}
To send a reply, use the channel_send tool with:
  bindingId: {{binding.Id}}
  text: <your reply text>

Use available tools (fs_read, fs_list, bash_run, etc.) before replying if needed to answer the request.
Do not output the reply as plain text — always use channel_send to deliver it.

Inbound Event:
- BindingId: {{binding.Id}}
- EventType: {{envelope.EventType}}
- ThreadType: {{binding.ThreadType}}
- ExternalMessageId: {{envelope.ExternalMessageId ?? "(none)"}}
- Sender: {{senderLabel}}
- Recipient: {{recipientLabel}}
- HasExplicitMention: {{hasExplicitMention}}
- MessageText:
{{messageText}}
""";
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

    internal static EffectivePolicyScope ResolveEffectiveScope(ChannelPolicy policy)
    {
        var isDirectMessage = policy.ThreadType == ChannelThreadType.DirectMessage;

        // KC-5001/5002/5003: DM sessions are owner-only and trust-equivalent to the main session.
        // They load full workspace context (including long-term memory and daily memory),
        // have no session timeout, and can write back to workspace files.
        // Group sessions remain conservative — external members are not trusted.
        return new EffectivePolicyScope(
            LoadAgents: policy.LoadAgents,
            LoadIdentity: policy.LoadIdentity,
            LoadSoul: policy.LoadSoul,
            LoadUserProfile: isDirectMessage && policy.LoadUserProfile,
            LoadLongTermMemory: isDirectMessage,
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

    private static StringComparer GetPathComparer()
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
    private void RecordDiagnosticEvent(string eventType, string level, string message, string sessionId)
    {
        if (_diagnosticsService == null) return;
        _diagnosticsService.Record(new DiagnosticEvent(
            Id: Guid.NewGuid().ToString("N"),
            Source: DiagnosticSource,
            EventType: eventType,
            Level: level,
            Message: message,
            Timestamp: DateTimeOffset.UtcNow,
            SessionId: sessionId));
    }

    internal sealed record EffectivePolicyScope(
        bool LoadAgents,
        bool LoadIdentity,
        bool LoadSoul,
        bool LoadUserProfile,
        bool LoadLongTermMemory,
        bool LoadRecentThreadSummary);
}
