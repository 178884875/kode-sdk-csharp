using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.Runtime;
using KodaClaw.Workspace;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using AutomationDefinitionContract = KodaClaw.Contracts.AutomationDefinition;
using Xunit;

namespace KodaClaw.IntegrationTests.Runtime;

public sealed class AutomationSessionServiceIntegrationTests
{
    [Fact]
    public async Task Start_automation_session_creates_handle_with_automation_kind_and_ids()
    {
        using var fixture = new AutomationRuntimeFixture();
        await using var service = fixture.CreateService();

        var definition = CreateDefinition(
            id: "auto-daily-ops",
            title: "Daily Ops",
            prompt: "Check workspace health and produce a concise summary.");

        var handle = await service.StartAutomationSessionAsync(definition);

        handle.SessionKind.Should().Be(SessionKind.Automation);
        handle.AutomationId.Should().Be(definition.Id);
        handle.SessionId.Should().NotBeNullOrWhiteSpace();
        handle.SessionDirectory.Should().Be(fixture.Workspace.GetSessionDirectory(handle.SessionId));
        Directory.Exists(handle.SessionDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task Start_automation_session_injects_definition_prompt_and_workspace_context_into_model_request()
    {
        using var fixture = new AutomationRuntimeFixture();
        await fixture.PrepareWorkspaceContextAsync();
        await using var service = fixture.CreateService();

        var definition = CreateDefinition(
            id: "auto-heartbeat-review",
            title: "Heartbeat Review",
            prompt: "Summarize today's priorities from heartbeat and notes.",
            inputPaths:
            [
                "tasks/daily-note.md",
            ]);

        var handle = await service.StartAutomationSessionAsync(definition);
        var runResult = await handle.Agent.RunAsync("run");
        runResult.Success.Should().BeTrue();

        var request = fixture.ModelProvider.LastRequest;
        request.Should().NotBeNull();
        request!.SystemPrompt.Should().Contain("Summarize today's priorities from heartbeat and notes.");
        request.SystemPrompt.Should().Contain("Heartbeat anchor: review QA and send noon digest.");
        request.SystemPrompt.Should().Contain("Daily note anchor: collect incident digests.");
        request.SystemPrompt.Should().Contain("### File: workspace/HEARTBEAT.md");
        request.SystemPrompt.Should().Contain("### File: workspace/tasks/daily-note.md");
    }

    [Fact]
    public async Task Start_automation_session_resolves_workspace_relative_input_paths_under_workspace_directory()
    {
        using var fixture = new AutomationRuntimeFixture();
        await fixture.PrepareWorkspaceContextAsync();
        await using var service = fixture.CreateService();

        var definition = CreateDefinition(
            id: "auto-relative-inputs",
            title: "Relative Inputs",
            prompt: "Read daily note through normalized relative path.",
            inputPaths:
            [
                "tasks/daily-note.md",
            ]);

        var handle = await service.StartAutomationSessionAsync(definition);
        var runResult = await handle.Agent.RunAsync("run");

        runResult.Success.Should().BeTrue();
        fixture.ModelProvider.LastRequest.Should().NotBeNull();
        fixture.ModelProvider.LastRequest!.SystemPrompt.Should().Contain("### File: workspace/tasks/daily-note.md");
    }

    [Fact]
    public async Task Start_automation_session_creates_session_directory_and_returns_handle_identity()
    {
        using var fixture = new AutomationRuntimeFixture();
        await fixture.PrepareWorkspaceContextAsync();
        await using var service = fixture.CreateService();

        var definition = CreateDefinition(
            id: "automation-identity-check",
            title: "Identity Check",
            prompt: "Read context and report identity.");

        var handle = await service.StartAutomationSessionAsync(definition);

        Directory.Exists(handle.SessionDirectory).Should().BeTrue();
        handle.SessionId.Should().Contain("auto-");
        handle.AutomationId.Should().Be("automation-identity-check");
    }

    private sealed class AutomationRuntimeFixture : IDisposable
    {
        public AutomationRuntimeFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "kodaclaw-automation-runtime-tests", Guid.NewGuid().ToString("N"));
            Workspace = new WorkspaceService(new KodaClawWorkspaceOptions
            {
                RootPath = RootPath,
            });
            ModelProvider = new CapturingModelProvider();
        }

        public string RootPath { get; }

        public WorkspaceService Workspace { get; }

        public CapturingModelProvider ModelProvider { get; }

        public async Task PrepareWorkspaceContextAsync()
        {
            await Workspace.EnsureInitializedAsync();

            var heartbeatPath = Path.Combine(
                RootPath,
                KodaClawWorkspaceLayout.WorkspaceDirectory,
                KodaClawWorkspaceLayout.HeartbeatFile);
            await File.WriteAllTextAsync(
                heartbeatPath,
                """
                # Heartbeat

                - Heartbeat anchor: review QA and send noon digest.
                """);

            var inputPath = Path.Combine(
                RootPath,
                KodaClawWorkspaceLayout.WorkspaceDirectory,
                "tasks",
                "daily-note.md");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            await File.WriteAllTextAsync(
                inputPath,
                """
                # Daily Note

                - Daily note anchor: collect incident digests.
                """);
        }

        public AutomationSessionService CreateService()
        {
            var dependencyFactory = new DefaultMainSessionAgentDependenciesFactory(new MainSessionDependencies
            {
                ModelProvider = ModelProvider,
            });

            return new AutomationSessionService(
                Workspace,
                dependencyFactory,
                new AutomationSessionOptions
                {
                    Model = "automation-capture-model",
                    MaxIterations = 4,
                });
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class CapturingModelProvider : IModelProvider
    {
        public string ProviderName => "capturing";

        public ModelRequest? LastRequest { get; private set; }

        public async IAsyncEnumerable<StreamChunk> StreamAsync(
            ModelRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            await Task.Yield();

            yield return new StreamChunk
            {
                Type = StreamChunkType.TextDelta,
                TextDelta = "automation-ok",
            };
            yield return new StreamChunk
            {
                Type = StreamChunkType.MessageStop,
                StopReason = ModelStopReason.EndTurn,
                Usage = new TokenUsage
                {
                    InputTokens = 0,
                    OutputTokens = 0,
                },
            };
        }

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ModelResponse
            {
                Content =
                [
                    new TextContent
                    {
                        Text = "automation-ok",
                    },
                ],
                StopReason = ModelStopReason.EndTurn,
                Usage = new TokenUsage
                {
                    InputTokens = 0,
                    OutputTokens = 0,
                },
                Model = request.Model,
            });
        }

        public Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    private static AutomationDefinitionContract CreateDefinition(
        string id,
        string title,
        string prompt,
        IReadOnlyList<string>? inputPaths = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new AutomationDefinitionContract(
            Id: id,
            Title: title,
            Prompt: prompt,
            Source: AutomationDefinitionSource.Manual,
            SourcePath: null,
            Schedule: new KodaClaw.Contracts.AutomationSchedule(
                Kind: KodaClaw.Contracts.AutomationScheduleKind.Daily,
                Interval: null,
                LocalTime: "09:00",
                DaysOfWeek: null),
            Enabled: true,
            InputPaths: inputPaths,
            CreatedAt: now,
            UpdatedAt: now,
            LastRunAt: null,
            NextRunAt: null,
            LastRunStatus: null,
            LastError: null);
    }
}
