using FluentAssertions;
using KodaClaw.ChannelHub;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KodaClaw.UnitTests.ChannelHub;

public sealed class SqliteThreadBindingRepositoryTests
{
    [Fact]
    public async Task Repository_should_round_trip_binding_and_lookup_by_external_thread()
    {
        using var workspace = new TempWorkspaceRoot("kodaclaw-thread-binding-unit");
        var repository = CreateRepository(workspace.Path);
        var binding = BuildBinding(
            id: "binding-dm-001",
            connectorKind: ChannelConnectorKind.Telegram,
            threadType: ChannelThreadType.DirectMessage,
            timestamp: new DateTimeOffset(2026, 3, 19, 11, 0, 0, TimeSpan.Zero));

        await repository.UpsertAsync(binding);

        var stored = await repository.GetByIdAsync(binding.Id);
        var externalLookup = await repository.GetByExternalThreadAsync(
            binding.ConnectorKind,
            binding.AccountId,
            binding.ExternalThreadId);

        stored.Should().Be(binding);
        externalLookup.Should().Be(binding);
    }

    [Fact]
    public async Task List_should_apply_account_thread_type_and_session_filters()
    {
        using var workspace = new TempWorkspaceRoot("kodaclaw-thread-binding-unit");
        var repository = CreateRepository(workspace.Path);
        var baseTime = new DateTimeOffset(2026, 3, 19, 12, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(BuildBinding(
            id: "binding-group-1",
            connectorKind: ChannelConnectorKind.Telegram,
            threadType: ChannelThreadType.Group,
            accountId: "account-telegram",
            timestamp: baseTime));
        await repository.UpsertAsync(BuildBinding(
            id: "binding-dm-2",
            connectorKind: ChannelConnectorKind.Telegram,
            threadType: ChannelThreadType.DirectMessage,
            accountId: "account-telegram",
            timestamp: baseTime.AddMinutes(1)));
        await repository.UpsertAsync(BuildBinding(
            id: "binding-webhook-3",
            connectorKind: ChannelConnectorKind.GenericWebhook,
            threadType: ChannelThreadType.DirectMessage,
            accountId: "account-webhook",
            timestamp: baseTime.AddMinutes(2)));

        var telegramBindings = await repository.ListAsync(new ChannelQuery(
            ConnectorKind: ChannelConnectorKind.Telegram,
            Limit: 10));
        var groupBindings = await repository.ListAsync(new ChannelQuery(
            ThreadType: ChannelThreadType.Group,
            Limit: 10));
        var dmSessionBindings = await repository.ListAsync(new ChannelQuery(
            SessionKind: SessionKind.ChannelDirectMessage,
            SessionId: "session-binding-dm-2",
            Limit: 10));

        telegramBindings.Select(static item => item.Id).Should().Equal("binding-dm-2", "binding-group-1");
        groupBindings.Should().ContainSingle().Which.Id.Should().Be("binding-group-1");
        dmSessionBindings.Should().ContainSingle().Which.Id.Should().Be("binding-dm-2");
    }

    [Fact]
    public async Task Repository_should_reject_binding_that_targets_non_channel_session()
    {
        using var workspace = new TempWorkspaceRoot("kodaclaw-thread-binding-unit");
        var repository = CreateRepository(workspace.Path);
        var invalidBinding = new ThreadBinding(
            Id: "binding-invalid",
            ConnectorKind: ChannelConnectorKind.Telegram,
            AccountId: "account-telegram",
            ExternalThreadId: "thread-invalid",
            ThreadType: ChannelThreadType.DirectMessage,
            SessionId: "session-main",
            SessionKind: SessionKind.Main,
            ChannelIdentity: new ChannelIdentity(Id: "user-123", DisplayName: "Invalid"),
            PolicyId: "policy-default",
            DeliveryRuleId: "delivery-default",
            CreatedAt: new DateTimeOffset(2026, 3, 19, 13, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 3, 19, 13, 1, 0, TimeSpan.Zero));

        var action = () => repository.UpsertAsync(invalidBinding);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Repository_should_initialize_thread_bindings_table_in_control_plane_db()
    {
        using var workspace = new TempWorkspaceRoot("kodaclaw-thread-binding-unit");
        var repository = CreateRepository(workspace.Path);
        await repository.UpsertAsync(BuildBinding(
            id: "binding-init",
            connectorKind: ChannelConnectorKind.Telegram,
            threadType: ChannelThreadType.DirectMessage,
            timestamp: new DateTimeOffset(2026, 3, 19, 14, 0, 0, TimeSpan.Zero)));

        var databasePath = Path.Combine(
            workspace.Path,
            KodaClawWorkspaceLayout.ConfigDirectory,
            KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        };

        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table' AND name = 'thread_bindings'
            LIMIT 1;
            """;

        var tableName = await command.ExecuteScalarAsync();
        tableName.Should().Be("thread_bindings");
    }

    private static SqliteThreadBindingRepository CreateRepository(string rootPath)
    {
        return new SqliteThreadBindingRepository(new TestWorkspaceService(rootPath));
    }

    private static ThreadBinding BuildBinding(
        string id,
        ChannelConnectorKind connectorKind,
        ChannelThreadType threadType,
        DateTimeOffset timestamp,
        string accountId = "account-telegram")
    {
        var sessionKind = threadType == ChannelThreadType.DirectMessage
            ? SessionKind.ChannelDirectMessage
            : SessionKind.ChannelGroup;

        return new ThreadBinding(
            Id: id,
            ConnectorKind: connectorKind,
            AccountId: accountId,
            ExternalThreadId: $"thread-{id}",
            ThreadType: threadType,
            SessionId: $"session-{id}",
            SessionKind: sessionKind,
            ChannelIdentity: new ChannelIdentity(
                Id: $"identity-{id}",
                Username: $"user_{id}",
                DisplayName: $"User {id}"),
            PolicyId: $"policy-{threadType}",
            DeliveryRuleId: $"delivery-{threadType}",
            CreatedAt: timestamp,
            UpdatedAt: timestamp.AddMinutes(1),
            LastInboundAt: timestamp.AddMinutes(2),
            LastOutboundAt: timestamp.AddMinutes(3),
            LastMessagePreview: $"preview:{id}");
    }
}
