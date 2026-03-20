using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using System.Text.Json;
using AgentRuntime = Kode.Agent.Sdk.Core.Agent.Agent;

namespace KodaClaw.Runtime;

public sealed class ChannelSessionService : IChannelSessionService, IAsyncDisposable
{
    private const string ThreadSummaryFileName = "SUMMARY.md";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        var prompt = BuildSystemPrompt(binding, policy, contextDocuments);
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
        var runResult = await handle.Agent.RunAsync(prompt, cancellationToken);

        if (string.IsNullOrWhiteSpace(runResult.Response))
        {
            throw new InvalidOperationException("Channel turn did not return a structured response.");
        }

        return new ChannelTurnExecutionResult(
            Session: handle,
            RunResult: runResult,
            RawResponse: runResult.Response,
            Proposal: ParseProposal(runResult.Response),
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

    private PromptBuildResult BuildSystemPrompt(
        ThreadBinding binding,
        ChannelPolicy policy,
        IReadOnlyList<PromptContextDocument> contextDocuments)
    {
        var scope = ResolveEffectiveScope(policy);
        var displayTitle = binding.ChannelIdentity.DisplayName
            ?? binding.ChannelIdentity.Username
            ?? binding.ExternalThreadId;

        var prompt = new PromptBuilder(PromptProfiles.Channel(binding.ThreadType, _options.SystemPrompt))
            .WithCharacterBudget(_options.MaxPromptCharacters)
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
- This is a direct message. If the sender is clearly asking for help or a response, you may propose a concise reply draft.
- Use the loaded user profile only when it is explicitly present in the session context.
""",
            ChannelThreadType.Group when hasExplicitMention => """
- This is a group thread and Koda was explicitly mentioned.
- If a reply is useful, keep it brief, public-safe, and grounded only in the loaded context.
""",
            ChannelThreadType.Group => """
- This is a group thread without an explicit mention of Koda.
- Prefer action "no_reply" unless the message clearly requires Koda's intervention or a direct response on Koda's behalf.
""",
            _ => string.Empty,
        };

        return $$"""
Process the inbound channel event and return JSON only.

Return this exact schema:
{
  "action": "no_reply | propose_reply",
  "replyText": "string or null",
  "reason": "short reason",
  "confidence": 0.0
}

Rules:
- If no outward response should be sent, return action "no_reply".
- If a reply is justified, keep it concise and channel-safe.
- Use only the loaded session context and the inbound event below.
- Never invent facts, commitments, approvals, or deliveries that did not happen.
- Never wrap the JSON in markdown fences.
{{threadGuidance}}

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

    private static ChannelReplyProposal ParseProposal(string rawResponse)
    {
        var json = ExtractJson(rawResponse);
        var proposal = JsonSerializer.Deserialize<ChannelReplyProposal>(json, JsonOptions);
        if (proposal is null)
        {
            throw new InvalidOperationException("Channel turn response could not be parsed.");
        }

        var action = proposal.Action?.Trim();
        if (!string.Equals(action, "no_reply", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action, "propose_reply", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Channel turn response contained an unknown action.");
        }

        var reason = string.IsNullOrWhiteSpace(proposal.Reason)
            ? "No reason supplied."
            : proposal.Reason.Trim();
        var confidence = Math.Clamp(proposal.Confidence, 0d, 1d);

        if (string.Equals(action, "propose_reply", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(proposal.ReplyText))
        {
            throw new InvalidOperationException("Channel turn reply proposal is missing reply text.");
        }

        return proposal with
        {
            Action = action!,
            ReplyText = string.IsNullOrWhiteSpace(proposal.ReplyText) ? null : proposal.ReplyText.Trim(),
            Reason = reason,
            Confidence = confidence,
        };
    }

    private static string ExtractJson(string rawResponse)
    {
        var trimmed = rawResponse.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                trimmed = trimmed[(firstLineBreak + 1)..];
            }

            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                trimmed = trimmed[..closingFence].Trim();
            }
        }

        try
        {
            JsonDocument.Parse(trimmed);
            return trimmed;
        }
        catch (JsonException)
        {
            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                throw new InvalidOperationException("Channel turn response did not contain JSON.");
            }

            var candidate = trimmed[start..(end + 1)];
            JsonDocument.Parse(candidate);
            return candidate;
        }
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
    private sealed record EffectivePolicyScope(
        bool LoadAgents,
        bool LoadIdentity,
        bool LoadSoul,
        bool LoadUserProfile,
        bool LoadLongTermMemory,
        bool LoadRecentThreadSummary);
}
