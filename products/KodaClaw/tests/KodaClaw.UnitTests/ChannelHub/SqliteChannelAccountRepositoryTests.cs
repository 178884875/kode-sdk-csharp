using FluentAssertions;
using KodaClaw.ChannelHub;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KodaClaw.UnitTests.ChannelHub;

public sealed class SqliteChannelAccountRepositoryTests
{
    [Fact]
    public async Task Repository_should_round_trip_channel_account()
    {
        using var workspace = new TempWorkspaceRoot("kodaclaw-channel-account-unit");
        var repository = CreateRepository(workspace.Path);
        var account = BuildAccount(
            id: "telegram-main",
            connectorKind: ChannelConnectorKind.Telegram,
            state: ChannelAccountState.Connected,
            timestamp: new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.Zero));

        await repository.UpsertAsync(account);

        var stored = await repository.GetByIdAsync(account.Id);
        var list = await repository.ListAsync(new ChannelAccountQuery(
            ConnectorKind: ChannelConnectorKind.Telegram,
            State: ChannelAccountState.Connected,
            Limit: 10));

        stored.Should().Be(account);
        list.Should().ContainSingle().Which.Should().Be(account);
    }

    [Fact]
    public async Task List_should_apply_connector_and_state_filters()
    {
        using var workspace = new TempWorkspaceRoot("kodaclaw-channel-account-unit");
        var repository = CreateRepository(workspace.Path);
        var baseTime = new DateTimeOffset(2026, 3, 19, 9, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(BuildAccount(
            id: "telegram-1",
            connectorKind: ChannelConnectorKind.Telegram,
            state: ChannelAccountState.Connected,
            timestamp: baseTime));
        await repository.UpsertAsync(BuildAccount(
            id: "telegram-2",
            connectorKind: ChannelConnectorKind.Telegram,
            state: ChannelAccountState.Degraded,
            timestamp: baseTime.AddMinutes(1)));
        await repository.UpsertAsync(BuildAccount(
            id: "webhook-1",
            connectorKind: ChannelConnectorKind.GenericWebhook,
            state: ChannelAccountState.Connected,
            timestamp: baseTime.AddMinutes(2)));

        var telegramAccounts = await repository.ListAsync(new ChannelAccountQuery(
            ConnectorKind: ChannelConnectorKind.Telegram,
            Limit: 10));
        var degradedAccounts = await repository.ListAsync(new ChannelAccountQuery(
            State: ChannelAccountState.Degraded,
            Limit: 10));

        telegramAccounts.Select(static item => item.Id).Should().Equal("telegram-2", "telegram-1");
        degradedAccounts.Should().ContainSingle().Which.Id.Should().Be("telegram-2");
    }

    [Fact]
    public async Task Repository_should_initialize_channel_accounts_table_in_control_plane_db()
    {
        using var workspace = new TempWorkspaceRoot("kodaclaw-channel-account-unit");
        var repository = CreateRepository(workspace.Path);
        await repository.UpsertAsync(BuildAccount(
            id: "telegram-init",
            connectorKind: ChannelConnectorKind.Telegram,
            state: ChannelAccountState.Disconnected,
            timestamp: new DateTimeOffset(2026, 3, 19, 10, 0, 0, TimeSpan.Zero)));

        var databasePath = Path.Combine(
            workspace.Path,
            KodaClawWorkspaceLayout.ConfigDirectory,
            KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);
        File.Exists(databasePath).Should().BeTrue();

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
            WHERE type = 'table' AND name = 'channel_accounts'
            LIMIT 1;
            """;

        var tableName = await command.ExecuteScalarAsync();
        tableName.Should().Be("channel_accounts");
    }

    private static SqliteChannelAccountRepository CreateRepository(string rootPath)
    {
        return new SqliteChannelAccountRepository(new TestWorkspaceService(rootPath));
    }

    private static ChannelAccount BuildAccount(
        string id,
        ChannelConnectorKind connectorKind,
        ChannelAccountState state,
        DateTimeOffset timestamp)
    {
        return new ChannelAccount(
            Id: id,
            ConnectorKind: connectorKind,
            DisplayName: $"Account {id}",
            State: state,
            CreatedAt: timestamp,
            UpdatedAt: timestamp.AddMinutes(1),
            ExternalAccountId: $"ext-{id}",
            CredentialReference: $"env:{id.ToUpperInvariant()}_TOKEN",
            Description: "Fixture channel account",
            ConfigurationJson: """{"parseMode":"markdown"}""",
            InboundEnabled: true,
            LastConnectedAt: state is ChannelAccountState.Connected ? timestamp.AddMinutes(2) : null,
            LastDisconnectedAt: state is ChannelAccountState.Disconnected ? timestamp.AddMinutes(3) : null,
            LastError: state is ChannelAccountState.Degraded ? "polling failed" : null);
    }
}
