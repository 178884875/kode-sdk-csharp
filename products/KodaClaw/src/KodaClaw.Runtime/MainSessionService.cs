using System.Collections.Concurrent;
using System.Text.Json;
using KodaClaw.Contracts;
using KodaClaw.PluginHost.Hosting;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using AgentRuntime = Kode.Agent.Sdk.Core.Agent.Agent;

namespace KodaClaw.Runtime;

public sealed class MainSessionService : IMainSessionService, IAsyncDisposable
{
    private const string DiagnosticSource = "koda.runtime.main_session";
    private const string ApprovalSource = "runtime.main_session.approval";
    private const string StaleApprovalDecisionBy = "runtime.resume";
    private const int ApprovalDecisionPollAttempts = 100;
    private const int ApprovalDecisionPollDelayMs = 50;
    private const int MaxPluginInjectionCandidates = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkspaceService _workspaceService;
    private readonly IMainSessionAgentDependenciesFactory _dependenciesFactory;
    private readonly MainSessionOptions _options;
    private readonly Dictionary<string, IAgent> _agents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionControlSubscriptions> _sessionSubscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LiveApprovalContext> _liveApprovals = new(StringComparer.Ordinal);
    private readonly IDiagnosticsService? _diagnosticsService;
    private readonly ICorrelationContextAccessor? _correlationContextAccessor;
    private readonly IApprovalRepository? _approvalRepository;
    private readonly IInboxRepository? _inboxRepository;
    private readonly IPluginRegistryRepository? _pluginRegistryRepository;
    private readonly IPluginLifecycleHost? _pluginLifecycleHost;
    private readonly IRuntimeConfigurationResolver? _runtimeConfigurationResolver;

    public MainSessionService(
        IWorkspaceService workspaceService,
        IMainSessionAgentDependenciesFactory dependenciesFactory,
        MainSessionOptions? options = null,
        IDiagnosticsService? diagnosticsService = null,
        ICorrelationContextAccessor? correlationContextAccessor = null,
        IApprovalRepository? approvalRepository = null,
        IInboxRepository? inboxRepository = null,
        IPluginRegistryRepository? pluginRegistryRepository = null,
        IPluginLifecycleHost? pluginLifecycleHost = null,
        IRuntimeConfigurationResolver? runtimeConfigurationResolver = null)
    {
        _workspaceService = workspaceService;
        _dependenciesFactory = dependenciesFactory;
        _options = options ?? new MainSessionOptions();
        _diagnosticsService = diagnosticsService;
        _correlationContextAccessor = correlationContextAccessor;
        _approvalRepository = approvalRepository;
        _inboxRepository = inboxRepository;
        _pluginRegistryRepository = pluginRegistryRepository;
        _pluginLifecycleHost = pluginLifecycleHost;
        _runtimeConfigurationResolver = runtimeConfigurationResolver;
    }

    public async Task<MainSessionHandle> EnsureMainSessionAsync(CancellationToken cancellationToken = default)
    {
        await _workspaceService.EnsureInitializedAsync(cancellationToken);

        var appConfig = await _workspaceService.LoadAppConfigAsync(cancellationToken);
        MainSessionHandle handle;

        if (string.IsNullOrWhiteSpace(appConfig.ActiveMainSessionId))
        {
            handle = await LoadOrCreateMainSessionAsync(GenerateSessionId(), cancellationToken);
        }
        else
        {
            handle = await LoadExistingOrFallbackAsync(appConfig.ActiveMainSessionId, cancellationToken);
        }

        if (!string.Equals(appConfig.ActiveMainSessionId, handle.SessionId, StringComparison.Ordinal))
        {
            await _workspaceService.SaveAppConfigAsync(
                appConfig with { ActiveMainSessionId = handle.SessionId },
                cancellationToken);
        }

        return handle;
    }

    public Task<ApprovalDecisionDispatchResult> ApproveApprovalAsync(
        string approvalId,
        CancellationToken cancellationToken = default)
    {
        return DispatchApprovalDecisionAsync(approvalId, approve: true, note: null, cancellationToken);
    }

    public Task<ApprovalDecisionDispatchResult> RejectApprovalAsync(
        string approvalId,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        return DispatchApprovalDecisionAsync(approvalId, approve: false, note, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var subscriptions in _sessionSubscriptions.Values)
        {
            subscriptions.Dispose();
        }

        _sessionSubscriptions.Clear();
        _liveApprovals.Clear();

        foreach (var agent in _agents.Values)
        {
            await agent.DisposeAsync();
        }

        _agents.Clear();
    }

    private async Task<ApprovalDecisionDispatchResult> DispatchApprovalDecisionAsync(
        string approvalId,
        bool approve,
        string? note,
        CancellationToken cancellationToken)
    {
        if (_approvalRepository == null || _inboxRepository == null)
        {
            return new ApprovalDecisionDispatchResult(
                ApprovalDecisionDispatchStatus.RuntimeUnavailable,
                Message: "Approval persistence is not configured.");
        }

        var approval = await _approvalRepository.GetByIdAsync(approvalId, cancellationToken);
        if (approval is null)
        {
            return new ApprovalDecisionDispatchResult(
                ApprovalDecisionDispatchStatus.NotFound,
                Message: "Approval was not found.");
        }

        if (approval.Status != ApprovalStatus.Pending)
        {
            return new ApprovalDecisionDispatchResult(
                ApprovalDecisionDispatchStatus.NotPending,
                approval,
                $"Approval is already {approval.Status}.");
        }

        var liveTarget = ResolveLiveApprovalTarget(approval);
        if (liveTarget.Status != ApprovalDecisionDispatchStatus.Completed || liveTarget.Target is null)
        {
            return new ApprovalDecisionDispatchResult(liveTarget.Status, approval, liveTarget.Message);
        }

        try
        {
            if (approve)
            {
                await liveTarget.Target.Agent.ApproveToolCallAsync(liveTarget.Target.CallId);
            }
            else
            {
                await liveTarget.Target.Agent.DenyToolCallAsync(liveTarget.Target.CallId, note);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not TaskCanceledException)
        {
            var errorMessage = ex.GetBaseException().Message;
            RecordDiagnosticEvent(
                eventType: "main_session.approval.dispatch_failed",
                level: "error",
                message: errorMessage,
                sessionId: liveTarget.Target.SessionId,
                correlationId: approval.CorrelationId,
                attributes: new Dictionary<string, string?>
                {
                    ["approvalId"] = approval.Id,
                    ["callId"] = liveTarget.Target.CallId,
                    ["decision"] = approve ? "allow" : "deny",
                });

            return new ApprovalDecisionDispatchResult(
                ApprovalDecisionDispatchStatus.LiveApprovalMissing,
                approval,
                errorMessage);
        }

        var expectedStatus = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        var decidedApproval = await WaitForApprovalDecisionAsync(approvalId, cancellationToken);
        if (decidedApproval is null)
        {
            return new ApprovalDecisionDispatchResult(
                ApprovalDecisionDispatchStatus.DecisionTimedOut,
                approval,
                "Timed out waiting for approval decision to persist.");
        }

        if (decidedApproval.Status != expectedStatus)
        {
            return new ApprovalDecisionDispatchResult(
                ApprovalDecisionDispatchStatus.NotPending,
                decidedApproval,
                $"Approval is already {decidedApproval.Status}.");
        }

        return new ApprovalDecisionDispatchResult(
            ApprovalDecisionDispatchStatus.Completed,
            decidedApproval);
    }

    private (ApprovalDecisionDispatchStatus Status, LiveApprovalTarget? Target, string? Message) ResolveLiveApprovalTarget(Approval approval)
    {
        var callId = TryGetApprovalCallId(approval);
        if (string.IsNullOrWhiteSpace(callId))
        {
            return (
                ApprovalDecisionDispatchStatus.LiveApprovalMissing,
                null,
                "Approval payload did not include a tool call id.");
        }

        if (_liveApprovals.TryGetValue(callId, out var liveContext))
        {
            if (_agents.TryGetValue(liveContext.SessionId, out var agent))
            {
                return (
                    ApprovalDecisionDispatchStatus.Completed,
                    new LiveApprovalTarget(liveContext.SessionId, callId, agent),
                    null);
            }

            return (
                ApprovalDecisionDispatchStatus.LiveSessionRequired,
                null,
                "A live runtime session is required for this approval.");
        }

        if (!string.IsNullOrWhiteSpace(approval.SessionId) && _agents.ContainsKey(approval.SessionId))
        {
            return (
                ApprovalDecisionDispatchStatus.LiveApprovalMissing,
                null,
                "Pending approval is no longer attached to an active approval waiter.");
        }

        return (
            ApprovalDecisionDispatchStatus.LiveSessionRequired,
            null,
            "A live runtime session is required for this approval.");
    }

    private async Task<Approval?> WaitForApprovalDecisionAsync(
        string approvalId,
        CancellationToken cancellationToken)
    {
        if (_approvalRepository == null)
        {
            return null;
        }

        for (var attempt = 0; attempt < ApprovalDecisionPollAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var approval = await _approvalRepository.GetByIdAsync(approvalId, cancellationToken);
            if (approval is not null && approval.Status != ApprovalStatus.Pending)
            {
                return approval;
            }

            if (attempt + 1 < ApprovalDecisionPollAttempts)
            {
                await Task.Delay(ApprovalDecisionPollDelayMs, cancellationToken);
            }
        }

        var finalApproval = await _approvalRepository.GetByIdAsync(approvalId, cancellationToken);
        return finalApproval is not null && finalApproval.Status != ApprovalStatus.Pending
            ? finalApproval
            : null;
    }

    private static string? TryGetApprovalCallId(Approval approval)
    {
        if (!string.IsNullOrWhiteSpace(approval.PayloadJson))
        {
            try
            {
                using var payload = JsonDocument.Parse(approval.PayloadJson);
                if (payload.RootElement.TryGetProperty("callId", out var callIdProperty) &&
                    callIdProperty.ValueKind == JsonValueKind.String)
                {
                    return callIdProperty.GetString();
                }
            }
            catch (JsonException)
            {
                // Fall back to the deterministic approval id mapping if payload JSON is malformed.
            }
        }

        const string prefix = "approval-";
        return approval.Id.StartsWith(prefix, StringComparison.Ordinal)
            ? approval.Id[prefix.Length..]
            : null;
    }

    private async Task<MainSessionHandle> LoadExistingOrFallbackAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await LoadOrCreateMainSessionAsync(sessionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not TaskCanceledException)
        {
            return await CreateFreshMainSessionAsync(
                GenerateSessionId(),
                FormatResumeFailureMessage(ex),
                sessionId,
                cancellationToken);
        }
    }

    private async Task<MainSessionHandle> LoadOrCreateMainSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_agents.TryGetValue(sessionId, out var cached))
        {
            return CreateHandle(sessionId, resumedFromStore: false, resumeFailureMessage: null, cached);
        }

        var sessionDirectory = _workspaceService.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);

        var dependencies = _dependenciesFactory.Create(sessionId, sessionDirectory);
        if (await dependencies.Store.ExistsAsync(sessionId, cancellationToken))
        {
            var configuredModel = ResolveConfiguredModel();
            try
            {
                var resumed = await AgentRuntime.ResumeFromStoreAsync(
                    sessionId,
                    dependencies,
                    options: new ResumeOptions { Strategy = RecoveryStrategy.Crash },
                    overrides: new AgentConfigOverrides
                    {
                        Model = configuredModel,
                        SystemPrompt = _options.SystemPrompt,
                        Tools = _options.Tools,
                        Permissions = _options.Permissions,
                    },
                    cancellationToken: cancellationToken);

                TrackSession(sessionId, resumed);
                await CancelStalePendingApprovalsAsync(
                    sessionId,
                    "Canceled stale approvals after session resume.",
                    cancellationToken);
                RecordSessionResumed(sessionId);
                return CreateHandle(sessionId, resumedFromStore: true, resumeFailureMessage: null, resumed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not TaskCanceledException)
            {
                return await CreateFreshMainSessionAsync(
                    GenerateSessionId(),
                    FormatResumeFailureMessage(ex),
                    sessionId,
                    cancellationToken);
            }
        }

        return await CreateFreshMainSessionAsync(sessionId, null, null, cancellationToken);
    }

    private async Task<MainSessionHandle> CreateFreshMainSessionAsync(
        string sessionId,
        string? resumeFailureMessage,
        string? attemptedSessionId,
        CancellationToken cancellationToken)
    {
        var sessionDirectory = _workspaceService.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);

        var dependencies = _dependenciesFactory.Create(sessionId, sessionDirectory);
        var sessionTools = await BuildSessionToolsAsync(sessionId, dependencies.ToolRegistry, cancellationToken);
        var configuredModel = ResolveConfiguredModel();
        var created = await AgentRuntime.CreateAsync(
            sessionId,
            CreateAgentConfig(sessionDirectory, sessionTools, configuredModel),
            dependencies,
            cancellationToken);

        TrackSession(sessionId, created);
        if (!string.IsNullOrWhiteSpace(attemptedSessionId))
        {
            await CancelStalePendingApprovalsAsync(
                attemptedSessionId,
                "Canceled stale approvals after session recovery fallback.",
                cancellationToken);
        }

        if (resumeFailureMessage is null)
        {
            RecordSessionCreated(sessionId);
        }
        else
        {
            RecordResumeFallback(sessionId, attemptedSessionId, resumeFailureMessage);
        }

        return CreateHandle(sessionId, resumedFromStore: false, resumeFailureMessage: resumeFailureMessage, created);
    }

    private async Task<IReadOnlyList<string>> BuildSessionToolsAsync(
        string sessionId,
        IToolRegistry? toolRegistry,
        CancellationToken cancellationToken)
    {
        var tools = new List<string>(_options.Tools);
        if (_pluginRegistryRepository == null || _pluginLifecycleHost == null || toolRegistry == null)
        {
            return tools;
        }

        IReadOnlyList<PluginRecord> candidates;
        try
        {
            candidates = await _pluginRegistryRepository.ListAsync(
                new PluginQuery(
                    Type: PluginType.Tool,
                    Enabled: true,
                    RuntimeState: PluginRuntimeState.Running,
                    Limit: MaxPluginInjectionCandidates),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
        {
            RecordDiagnosticEvent(
                eventType: "main_session.plugin_tools.query_failed",
                level: "warning",
                message: ex.GetBaseException().Message,
                sessionId: sessionId);
            return tools;
        }

        var merged = new HashSet<string>(tools, StringComparer.OrdinalIgnoreCase);
        var injectedPluginCount = 0;
        var injectedToolCount = 0;

        foreach (var candidate in candidates)
        {
            if (candidate.TrustState is not (PluginTrustState.Trusted or PluginTrustState.Signed))
            {
                continue;
            }

            IReadOnlyList<ITool> pluginTools;
            try
            {
                pluginTools = await _pluginLifecycleHost.GetToolsAsync(candidate.Id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
            {
                RecordDiagnosticEvent(
                    eventType: "main_session.plugin_tools.fetch_failed",
                    level: "warning",
                    message: ex.GetBaseException().Message,
                    sessionId: sessionId,
                    attributes: new Dictionary<string, string?>
                    {
                        ["pluginId"] = candidate.Id,
                    });
                continue;
            }

            var injectedForCurrentPlugin = false;
            foreach (var pluginTool in pluginTools)
            {
                if (string.IsNullOrWhiteSpace(pluginTool.Name) || !merged.Add(pluginTool.Name))
                {
                    continue;
                }

                toolRegistry.Register(pluginTool);
                tools.Add(pluginTool.Name);
                injectedToolCount++;
                injectedForCurrentPlugin = true;
            }

            if (injectedForCurrentPlugin)
            {
                injectedPluginCount++;
            }
        }

        if (injectedToolCount > 0)
        {
            RecordDiagnosticEvent(
                eventType: "main_session.plugin_tools.injected",
                level: "info",
                message: $"Injected {injectedToolCount} plugin tool(s) into a new main session.",
                sessionId: sessionId,
                attributes: new Dictionary<string, string?>
                {
                    ["pluginCount"] = injectedPluginCount.ToString(),
                    ["toolCount"] = injectedToolCount.ToString(),
                });
        }

        return tools;
    }

    private AgentConfig CreateAgentConfig(
        string sessionDirectory,
        IReadOnlyList<string> tools,
        string model)
    {
        return new AgentConfig
        {
            Model = model,
            SystemPrompt = _options.SystemPrompt,
            MaxIterations = _options.MaxIterations,
            Tools = tools,
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

    private void TrackSession(string sessionId, IAgent agent)
    {
        _agents[sessionId] = agent;
        AttachApprovalSubscriptions(sessionId, agent);
    }

    private void AttachApprovalSubscriptions(string sessionId, IAgent agent)
    {
        if (_approvalRepository == null || _inboxRepository == null || _sessionSubscriptions.ContainsKey(sessionId))
        {
            return;
        }

        var permissionRequired = agent.EventBus.OnControl<PermissionRequiredEvent>(evt =>
        {
            var correlationId = _correlationContextAccessor?.CorrelationId;
            var context = BuildLiveApprovalContext(sessionId, evt.Call, correlationId);
            _liveApprovals[evt.Call.Id] = context;
            _ = PersistPendingApprovalAsync(context);
        });

        var permissionDecided = agent.EventBus.OnControl<PermissionDecidedEvent>(evt =>
        {
            var correlationId = _correlationContextAccessor?.CorrelationId;
            _ = PersistApprovalDecisionAsync(sessionId, evt, correlationId);
        });

        _sessionSubscriptions[sessionId] = new SessionControlSubscriptions(permissionRequired, permissionDecided);
    }

    private async Task PersistPendingApprovalAsync(LiveApprovalContext context)
    {
        if (_approvalRepository == null || _inboxRepository == null)
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            await _approvalRepository.UpsertAsync(new Approval(
                Id: context.ApprovalId,
                Kind: ApprovalKind.ExternalAction,
                Status: ApprovalStatus.Pending,
                Title: context.Title,
                Summary: context.Summary,
                Source: ApprovalSource,
                RequestedAt: now,
                UpdatedAt: now,
                SessionId: context.SessionId,
                CorrelationId: context.CorrelationId,
                InboxItemId: context.InboxItemId,
                PayloadJson: context.PayloadJson));

            await _inboxRepository.UpsertAsync(new InboxItem(
                Id: context.InboxItemId,
                Kind: InboxItemKind.Approval,
                Status: InboxItemStatus.Open,
                Title: context.Title,
                Summary: context.Summary,
                Source: ApprovalSource,
                CreatedAt: now,
                UpdatedAt: now,
                RequiresAction: true,
                Route: $"/approvals/{context.ApprovalId}",
                SessionId: context.SessionId,
                CorrelationId: context.CorrelationId,
                ApprovalId: context.ApprovalId,
                PayloadJson: context.PayloadJson));

            RecordDiagnosticEvent(
                eventType: "main_session.approval.requested",
                level: "info",
                message: $"Approval requested for tool '{context.ToolName}'.",
                sessionId: context.SessionId,
                correlationId: context.CorrelationId,
                attributes: new Dictionary<string, string?>
                {
                    ["approvalId"] = context.ApprovalId,
                    ["inboxItemId"] = context.InboxItemId,
                    ["callId"] = context.CallId,
                    ["toolName"] = context.ToolName,
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordDiagnosticEvent(
                eventType: "main_session.approval.persistence_failed",
                level: "error",
                message: ex.GetBaseException().Message,
                sessionId: context.SessionId,
                correlationId: context.CorrelationId,
                attributes: new Dictionary<string, string?>
                {
                    ["approvalId"] = context.ApprovalId,
                    ["callId"] = context.CallId,
                    ["toolName"] = context.ToolName,
                });
        }
    }

    private async Task PersistApprovalDecisionAsync(
        string sessionId,
        PermissionDecidedEvent evt,
        string? correlationId)
    {
        if (_approvalRepository == null || _inboxRepository == null)
        {
            return;
        }

        try
        {
            var context = _liveApprovals.TryRemove(evt.CallId, out var liveContext)
                ? liveContext
                : CreateFallbackApprovalContext(sessionId, evt.CallId, correlationId);

            var status = string.Equals(evt.Decision, "allow", StringComparison.OrdinalIgnoreCase)
                ? ApprovalStatus.Approved
                : ApprovalStatus.Rejected;
            var now = DateTimeOffset.UtcNow;

            var transitioned = await _approvalRepository.TransitionAsync(
                context.ApprovalId,
                status,
                now,
                decidedBy: evt.DecidedBy,
                decisionNote: evt.Note);

            if (!transitioned)
            {
                var existing = await _approvalRepository.GetByIdAsync(context.ApprovalId);
                if (existing == null)
                {
                    await _approvalRepository.UpsertAsync(new Approval(
                        Id: context.ApprovalId,
                        Kind: ApprovalKind.ExternalAction,
                        Status: ApprovalStatus.Pending,
                        Title: context.Title,
                        Summary: context.Summary,
                        Source: ApprovalSource,
                        RequestedAt: now,
                        UpdatedAt: now,
                        SessionId: context.SessionId,
                        CorrelationId: context.CorrelationId,
                        InboxItemId: context.InboxItemId,
                        PayloadJson: context.PayloadJson));

                    await _inboxRepository.UpsertAsync(new InboxItem(
                        Id: context.InboxItemId,
                        Kind: InboxItemKind.Approval,
                        Status: InboxItemStatus.Open,
                        Title: context.Title,
                        Summary: context.Summary,
                        Source: ApprovalSource,
                        CreatedAt: now,
                        UpdatedAt: now,
                        RequiresAction: true,
                        Route: $"/approvals/{context.ApprovalId}",
                        SessionId: context.SessionId,
                        CorrelationId: context.CorrelationId,
                        ApprovalId: context.ApprovalId,
                        PayloadJson: context.PayloadJson));

                    transitioned = await _approvalRepository.TransitionAsync(
                        context.ApprovalId,
                        status,
                        now,
                        decidedBy: evt.DecidedBy,
                        decisionNote: evt.Note);
                }
            }

            if (transitioned)
            {
                await _inboxRepository.UpdateStatusAsync(
                    context.InboxItemId,
                    InboxItemStatus.Resolved,
                    now,
                    resolvedAt: now);
            }

            RecordDiagnosticEvent(
                eventType: "main_session.approval.decided",
                level: status == ApprovalStatus.Approved ? "info" : "warning",
                message: $"Approval {status} for tool call '{evt.CallId}'.",
                sessionId: context.SessionId,
                correlationId: correlationId ?? context.CorrelationId,
                attributes: new Dictionary<string, string?>
                {
                    ["approvalId"] = context.ApprovalId,
                    ["inboxItemId"] = context.InboxItemId,
                    ["callId"] = evt.CallId,
                    ["decision"] = evt.Decision,
                    ["decidedBy"] = evt.DecidedBy,
                    ["decisionNote"] = evt.Note,
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordDiagnosticEvent(
                eventType: "main_session.approval.decision_persistence_failed",
                level: "error",
                message: ex.GetBaseException().Message,
                sessionId: sessionId,
                correlationId: correlationId,
                attributes: new Dictionary<string, string?>
                {
                    ["callId"] = evt.CallId,
                    ["decision"] = evt.Decision,
                });
        }
    }

    private async Task CancelStalePendingApprovalsAsync(
        string sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (_approvalRepository == null || _inboxRepository == null || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var pendingApprovals = await _approvalRepository.ListAsync(
            new ApprovalQuery(
                Status: ApprovalStatus.Pending,
                SessionId: sessionId,
                Limit: 200),
            cancellationToken);

        foreach (var approval in pendingApprovals)
        {
            var now = DateTimeOffset.UtcNow;
            var transitioned = await _approvalRepository.TransitionAsync(
                approval.Id,
                ApprovalStatus.Canceled,
                now,
                decidedBy: StaleApprovalDecisionBy,
                decisionNote: reason,
                cancellationToken);

            if (!transitioned)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(approval.InboxItemId))
            {
                await _inboxRepository.UpdateStatusAsync(
                    approval.InboxItemId,
                    InboxItemStatus.Resolved,
                    now,
                    resolvedAt: now,
                    cancellationToken);
            }

            RecordDiagnosticEvent(
                eventType: "main_session.approval.stale_canceled",
                level: "warning",
                message: reason,
                sessionId: sessionId,
                correlationId: approval.CorrelationId,
                attributes: new Dictionary<string, string?>
                {
                    ["approvalId"] = approval.Id,
                    ["inboxItemId"] = approval.InboxItemId,
                });
        }
    }

    private MainSessionHandle CreateHandle(string sessionId, bool resumedFromStore, string? resumeFailureMessage, IAgent agent)
    {
        return new MainSessionHandle(
            SessionId: sessionId,
            SessionKind: SessionKind.Main,
            SessionDirectory: _workspaceService.GetSessionDirectory(sessionId),
            ResumedFromStore: resumedFromStore,
            ResumeFailureMessage: resumeFailureMessage,
            Agent: agent);
    }

    private static LiveApprovalContext BuildLiveApprovalContext(
        string sessionId,
        ToolCallSnapshot call,
        string? correlationId)
    {
        var approvalId = GetApprovalId(call.Id);
        var inboxItemId = GetInboxItemId(call.Id);
        var inputPreview = BuildInputPreview(call.InputPreview);
        var payload = new ApprovalPayload(
            CallId: call.Id,
            ToolName: call.Name,
            InputPreview: inputPreview,
            PermissionMode: TryGetApprovalMeta(call.Approval.Meta, "mode"),
            Reason: TryGetApprovalMeta(call.Approval.Meta, "reason"));
        var title = $"Approval required for {call.Name}";
        var summary = string.IsNullOrWhiteSpace(inputPreview)
            ? $"Tool '{call.Name}' is waiting for approval."
            : $"Tool '{call.Name}' is waiting for approval: {Truncate(inputPreview, 180)}";

        return new LiveApprovalContext(
            SessionId: sessionId,
            CallId: call.Id,
            ApprovalId: approvalId,
            InboxItemId: inboxItemId,
            ToolName: call.Name,
            Title: title,
            Summary: summary,
            CorrelationId: correlationId,
            PayloadJson: JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static LiveApprovalContext CreateFallbackApprovalContext(
        string sessionId,
        string callId,
        string? correlationId)
    {
        var toolName = "unknown";
        return new LiveApprovalContext(
            SessionId: sessionId,
            CallId: callId,
            ApprovalId: GetApprovalId(callId),
            InboxItemId: GetInboxItemId(callId),
            ToolName: toolName,
            Title: $"Approval required for {toolName}",
            Summary: $"Tool call '{callId}' was waiting for approval.",
            CorrelationId: correlationId,
            PayloadJson: JsonSerializer.Serialize(
                new ApprovalPayload(
                    CallId: callId,
                    ToolName: toolName,
                    InputPreview: null,
                    PermissionMode: null,
                    Reason: null),
                JsonOptions));
    }

    private static string? BuildInputPreview(object? inputPreview)
    {
        if (inputPreview == null)
        {
            return null;
        }

        return inputPreview switch
        {
            string text => text,
            JsonElement json => json.GetRawText(),
            _ => inputPreview.ToString(),
        };
    }

    private static string? TryGetApprovalMeta(JsonElement? meta, string key)
    {
        if (meta is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(key, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "…";
    }

    private static string GetApprovalId(string callId) => $"approval-{callId}";

    private static string GetInboxItemId(string callId) => $"inbox-approval-{callId}";

    private static string FormatResumeFailureMessage(Exception exception)
    {
        var root = exception.GetBaseException();
        return $"Resume failed: {root.GetType().Name}: {root.Message}";
    }

    private void RecordSessionCreated(string sessionId)
    {
        RecordDiagnosticEvent(
            eventType: "main_session.created",
            level: "info",
            message: "Created a fresh main session.",
            sessionId: sessionId);
    }

    private void RecordSessionResumed(string sessionId)
    {
        RecordDiagnosticEvent(
            eventType: "main_session.resumed",
            level: "info",
            message: "Resumed main session from store.",
            sessionId: sessionId);
    }

    private void RecordResumeFallback(
        string sessionId,
        string? attemptedSessionId,
        string resumeFailureMessage)
    {
        var attributes = new Dictionary<string, string?>
        {
            ["failedSessionId"] = attemptedSessionId,
            ["failureReason"] = resumeFailureMessage,
        };

        RecordDiagnosticEvent(
            eventType: "main_session.resume_fallback",
            level: "warning",
            message: resumeFailureMessage,
            sessionId: sessionId,
            attributes: attributes);
    }

    private void RecordDiagnosticEvent(
        string eventType,
        string level,
        string message,
        string sessionId,
        string? correlationId = null,
        IReadOnlyDictionary<string, string?>? attributes = null)
    {
        if (_diagnosticsService == null)
        {
            return;
        }

        var diagnosticEvent = new DiagnosticEvent(
            Id: Guid.NewGuid().ToString("N"),
            Source: DiagnosticSource,
            EventType: eventType,
            Level: level,
            Message: message,
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: correlationId ?? _correlationContextAccessor?.CorrelationId,
            SessionId: sessionId,
            Attributes: attributes);

        _diagnosticsService.Record(diagnosticEvent);
    }

    private static string GenerateSessionId()
    {
        return $"main-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32];
    }

    private sealed record LiveApprovalTarget(string SessionId, string CallId, IAgent Agent);

    private sealed record SessionControlSubscriptions(
        IDisposable PermissionRequired,
        IDisposable PermissionDecided) : IDisposable
    {
        public void Dispose()
        {
            PermissionRequired.Dispose();
            PermissionDecided.Dispose();
        }
    }

    private sealed record LiveApprovalContext(
        string SessionId,
        string CallId,
        string ApprovalId,
        string InboxItemId,
        string ToolName,
        string Title,
        string Summary,
        string? CorrelationId,
        string PayloadJson);

    private sealed record ApprovalPayload(
        string CallId,
        string ToolName,
        string? InputPreview,
        string? PermissionMode,
        string? Reason);
}
