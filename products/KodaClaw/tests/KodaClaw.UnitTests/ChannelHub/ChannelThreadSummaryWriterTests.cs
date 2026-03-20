using FluentAssertions;
using KodaClaw.ChannelHub;
using KodaClaw.Contracts;
using Moq;
using Xunit;

namespace KodaClaw.UnitTests.ChannelHub;

public sealed class ChannelThreadSummaryWriterTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly Mock<IWorkspaceService> _workspaceMock;
    private readonly ChannelThreadSummaryWriter _writer;

    public ChannelThreadSummaryWriterTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"ctsw-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspaceRoot);

        _workspaceMock = new Mock<IWorkspaceService>();
        _workspaceMock.SetupGet(w => w.RootPath).Returns(_workspaceRoot);

        _writer = new ChannelThreadSummaryWriter(_workspaceMock.Object);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspaceRoot, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Write_should_create_summary_file_under_workspace_channels()
    {
        var binding = BuildBinding("binding-001");
        var outcome = BuildOutcome(ChannelTurnOutcomeKind.Delivered, "Reply sent.");

        await _writer.WriteAsync(binding, outcome);

        var expectedPath = Path.Combine(
            _workspaceRoot, "workspace", "channels", "binding-001", "SUMMARY.md");
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Fact]
    public async Task Write_should_append_entry_with_kind_and_summary()
    {
        var binding = BuildBinding("binding-002");
        var outcome = BuildOutcome(ChannelTurnOutcomeKind.Delivered, "Hello back.");

        await _writer.WriteAsync(binding, outcome);

        var filePath = Path.Combine(
            _workspaceRoot, "workspace", "channels", "binding-002", "SUMMARY.md");
        var content = await File.ReadAllTextAsync(filePath);

        content.Should().Contain("delivered");
        content.Should().Contain("Hello back.");
    }

    [Fact]
    public async Task Write_should_append_multiple_entries()
    {
        var binding = BuildBinding("binding-003");
        var outcome1 = BuildOutcome(ChannelTurnOutcomeKind.Delivered, "First reply.");
        var outcome2 = BuildOutcome(ChannelTurnOutcomeKind.DraftCreated, "Draft for approval.");

        await _writer.WriteAsync(binding, outcome1);
        await _writer.WriteAsync(binding, outcome2);

        var filePath = Path.Combine(
            _workspaceRoot, "workspace", "channels", "binding-003", "SUMMARY.md");
        var content = await File.ReadAllTextAsync(filePath);

        content.Should().Contain("delivered");
        content.Should().Contain("draft_created");
        content.Should().Contain("First reply.");
        content.Should().Contain("Draft for approval.");
    }

    [Fact]
    public async Task Write_should_truncate_long_summary_preview()
    {
        var binding = BuildBinding("binding-004");
        var longSummary = new string('x', 200);
        var outcome = BuildOutcome(ChannelTurnOutcomeKind.Delivered, longSummary);

        await _writer.WriteAsync(binding, outcome);

        var filePath = Path.Combine(
            _workspaceRoot, "workspace", "channels", "binding-004", "SUMMARY.md");
        var content = await File.ReadAllTextAsync(filePath);

        content.Should().Contain("...");
        var line = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).First();
        line.Length.Should().BeLessThan(200);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ThreadBinding BuildBinding(string id)
    {
        var now = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);
        return new ThreadBinding(
            Id: id,
            ConnectorKind: ChannelConnectorKind.Telegram,
            AccountId: "account-001",
            ExternalThreadId: "ext-thread-001",
            ThreadType: ChannelThreadType.DirectMessage,
            SessionId: $"session-{id}",
            SessionKind: SessionKind.ChannelDirectMessage,
            ChannelIdentity: new ChannelIdentity("tg-001", "TestBot", null),
            PolicyId: "policy-001",
            DeliveryRuleId: "rule-001",
            CreatedAt: now,
            UpdatedAt: now);
    }

    private static ChannelTurnOutcome BuildOutcome(ChannelTurnOutcomeKind kind, string summary)
    {
        return new ChannelTurnOutcome(
            Kind: kind,
            Summary: summary,
            OccurredAt: new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero));
    }
}
