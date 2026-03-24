using System.Text.Json;
using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.PluginHost.Registry;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KodaClaw.UnitTests.PluginHost;

public sealed class SqlitePluginRegistryRepositoryTests
{
    [Fact]
    public async Task List_should_start_empty()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);

        var records = await repository.ListAsync();

        records.Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_and_get_should_round_trip()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var record = BuildRecord(
            id: "plugin.todo",
            type: PluginType.Tool,
            trustState: PluginTrustState.Trusted,
            enabled: true,
            runtimeState: PluginRuntimeState.Running);

        await repository.UpsertAsync(record);
        var stored = await repository.GetByIdAsync(record.Id);

        stored.Should().NotBeNull();
        var storedRecord = stored ?? throw new InvalidOperationException("Stored record was unexpectedly null.");
        storedRecord.Should().BeEquivalentTo(record, options => options
            .Excluding(value => value.Manifest.ConfigSchema));

        storedRecord.Manifest.ConfigSchema.HasValue.Should().Be(record.Manifest.ConfigSchema.HasValue);
        storedRecord.Manifest.ConfigSchema?.GetRawText().Should().Be(record.Manifest.ConfigSchema?.GetRawText());
    }

    [Fact]
    public async Task List_should_apply_filters_for_trust_enabled_runtime_and_type()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var now = new DateTimeOffset(2026, 3, 19, 2, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(BuildRecord(
            id: "plugin.todo",
            type: PluginType.Tool,
            trustState: PluginTrustState.Trusted,
            enabled: true,
            runtimeState: PluginRuntimeState.Running,
            timestamp: now));
        await repository.UpsertAsync(BuildRecord(
            id: "plugin.memory",
            type: PluginType.Memory,
            trustState: PluginTrustState.Untrusted,
            enabled: false,
            runtimeState: PluginRuntimeState.Stopped,
            timestamp: now.AddMinutes(1)));
        await repository.UpsertAsync(BuildRecord(
            id: "plugin.bridge",
            type: PluginType.Channel,
            trustState: PluginTrustState.Signed,
            enabled: true,
            runtimeState: PluginRuntimeState.Degraded,
            timestamp: now.AddMinutes(2)));

        var strictFiltered = await repository.ListAsync(new PluginQuery(
            Type: PluginType.Tool,
            TrustState: PluginTrustState.Trusted,
            Enabled: true,
            RuntimeState: PluginRuntimeState.Running,
            Limit: 20));

        strictFiltered.Should().ContainSingle();
        strictFiltered[0].Id.Should().Be("plugin.todo");

        var typeFiltered = await repository.ListAsync(new PluginQuery(
            Type: PluginType.Channel,
            Limit: 20));

        typeFiltered.Should().ContainSingle();
        typeFiltered[0].Id.Should().Be("plugin.bridge");

        var signedFiltered = await repository.ListAsync(new PluginQuery(
            TrustState: PluginTrustState.Signed,
            Limit: 20));

        signedFiltered.Should().ContainSingle();
        signedFiltered[0].Id.Should().Be("plugin.bridge");
    }

    [Fact]
    public async Task Delete_should_remove_record()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var record = BuildRecord(
            id: "plugin.delete",
            type: PluginType.Tool,
            trustState: PluginTrustState.Trusted,
            enabled: false,
            runtimeState: PluginRuntimeState.Stopped);
        await repository.UpsertAsync(record);

        var deleted = await repository.DeleteAsync(record.Id);
        var list = await repository.ListAsync();

        deleted.Should().BeTrue();
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Repository_should_initialize_plugins_table_in_control_plane_db()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        await repository.UpsertAsync(BuildRecord(
            id: "plugin.table-init",
            type: PluginType.Tool,
            trustState: PluginTrustState.Trusted,
            enabled: true,
            runtimeState: PluginRuntimeState.Running));

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
            WHERE type = 'table' AND name = 'plugins'
            LIMIT 1;
            """;

        var tableName = await command.ExecuteScalarAsync();
        tableName.Should().Be("plugins");
    }

    [Fact]
    public async Task Repository_should_add_trust_evidence_column_for_existing_database()
    {
        using var workspace = new TempWorkspaceRoot();
        var databasePath = Path.Combine(
            workspace.Path,
            KodaClawWorkspaceLayout.ConfigDirectory,
            KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        };

        await using (var connection = new SqliteConnection(builder.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE plugins (
                    id TEXT NOT NULL PRIMARY KEY,
                    manifest_json TEXT NOT NULL,
                    types_csv TEXT NOT NULL,
                    install_source TEXT NOT NULL,
                    root_path TEXT NOT NULL,
                    trust_state TEXT NOT NULL,
                    enabled INTEGER NOT NULL,
                    runtime_state TEXT NOT NULL,
                    discovered_at TEXT NOT NULL,
                    installed_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    last_started_at TEXT NULL,
                    last_stopped_at TEXT NULL,
                    last_health_at TEXT NULL,
                    restart_count INTEGER NOT NULL,
                    last_error TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = CreateRepository(workspace.Path);
        var record = BuildRecord(
            id: "plugin.migrated",
            type: PluginType.Tool,
            trustState: PluginTrustState.Signed,
            enabled: true,
            runtimeState: PluginRuntimeState.Running);

        await repository.UpsertAsync(record);

        await using var verificationConnection = new SqliteConnection(builder.ToString());
        await verificationConnection.OpenAsync();

        await using var pragma = verificationConnection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(plugins);";
        await using var reader = await pragma.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        columns.Should().Contain("trust_evidence_json");

        var stored = await repository.GetByIdAsync("plugin.migrated");
        stored.Should().NotBeNull();
        stored!.TrustEvidence.Should().NotBeNull();
        stored.TrustEvidence!.VerificationState.Should().Be(PluginTrustVerificationState.Verified);
    }

    private static SqlitePluginRegistryRepository CreateRepository(string rootPath)
    {
        return new SqlitePluginRegistryRepository(new FakeWorkspaceService(rootPath));
    }

    private static PluginRecord BuildRecord(
        string id,
        PluginType type,
        PluginTrustState trustState,
        bool enabled,
        PluginRuntimeState runtimeState,
        DateTimeOffset? timestamp = null)
    {
        var now = timestamp ?? DateTimeOffset.UtcNow;
        using var document = JsonDocument.Parse("""{"type":"object","title":"PluginConfig"}""");

        var manifest = new PluginManifest(
            Id: id,
            Name: $"Plugin {id}",
            Version: "0.1.0",
            Types: [type],
            Runtime: new PluginRuntimeSpec(
                Transport: PluginTransportKind.Stdio,
                Command: "node",
                Args: ["index.js"],
                Environment: new Dictionary<string, string> { ["PLUGIN_ENV"] = "test" },
                Url: null,
                Headers: new Dictionary<string, string> { ["X-Plugin-Header"] = "static" },
                EnvironmentReferences: new Dictionary<string, string> { ["PLUGIN_SECRET"] = "keychain:plugins:test-secret" },
                HeaderReferences: new Dictionary<string, string> { ["Authorization"] = "env:PLUGIN_HEADER_TOKEN" }),
            Permissions: new PluginPermissionSet(
                Filesystem: ["workspace/plugins"],
                Network: true,
                Notifications: false,
                Background: true,
                Channels: null,
                UiPanels: null,
                Secrets: ["plugin.secret"]),
            Capabilities: new PluginCapabilitySet(
                Tools: ["run_task"],
                Channels: null,
                UiPanels: null,
                MemoryProviders: null),
            Display: new PluginDisplaySpec(
                Description: "Test plugin",
                Icon: "icon.png",
                AccentColor: "#333333"),
            Healthcheck: new PluginHealthcheckSpec(
                ToolName: "run_task",
                IntervalSeconds: 30,
                TimeoutSeconds: 5),
            ConfigSchema: document.RootElement.Clone());

        return new PluginRecord(
            Id: id,
            Manifest: manifest,
            InstallSource: PluginInstallSource.LocalDirectory,
            RootPath: $"workspace/plugins/{id}",
            TrustState: trustState,
            Enabled: enabled,
            RuntimeState: runtimeState,
            DiscoveredAt: now,
            InstalledAt: now,
            UpdatedAt: now,
            LastStartedAt: enabled ? now.AddMinutes(1) : null,
            LastStoppedAt: enabled ? null : now.AddMinutes(1),
            LastHealthAt: now.AddMinutes(2),
            RestartCount: enabled ? 1 : 0,
            LastError: runtimeState is PluginRuntimeState.Degraded ? "healthcheck failed" : null,
            TrustEvidence: new PluginTrustEvidence(
                Source: trustState == PluginTrustState.Signed
                    ? PluginTrustEvidenceSource.SignatureSidecar
                    : PluginTrustEvidenceSource.LocalDigest,
                VerificationState: trustState == PluginTrustState.Signed
                    ? PluginTrustVerificationState.Verified
                    : PluginTrustVerificationState.DigestOnly,
                Summary: trustState == PluginTrustState.Signed
                    ? "Signature sidecar matched the current manifest and package digests."
                    : "Local digest captured. No signature sidecar was found.",
                VerifiedAt: now.AddMinutes(3),
                ManifestDigestSha256: $"{id}-manifest",
                PackageDigestSha256: $"{id}-package",
                Signer: trustState == PluginTrustState.Signed ? "Fixture Publisher" : null,
                SignatureFilePath: trustState == PluginTrustState.Signed
                    ? $"workspace/plugins/{id}/plugin.signature.json"
                    : null));
    }

    private sealed class FakeWorkspaceService : IWorkspaceService
    {
        public FakeWorkspaceService(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public Task<WorkspaceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateSnapshot(initialized: true));
        }

        public Task<WorkspaceSnapshot> EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(Path.Combine(RootPath, KodaClawWorkspaceLayout.ConfigDirectory));
            return Task.FromResult(CreateSnapshot(initialized: true));
        }

        public Task<WorkspaceAppConfig> LoadAppConfigAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkspaceAppConfig());
        }

        public Task SaveAppConfigAsync(WorkspaceAppConfig appConfig, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public string GetSessionDirectory(string sessionId)
        {
            return Path.Combine(RootPath, KodaClawWorkspaceLayout.SessionsDirectory, sessionId);
        }

        public IReadOnlyList<string> GetSkillsPaths() => [];

        public Task<WorkspaceMcpConfig> ReadMcpConfigAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceMcpConfig());

        public Task SaveMcpConfigAsync(WorkspaceMcpConfig config, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<bool> TryCommitWorkspaceAsync(string message, CancellationToken cancellationToken = default) => Task.FromResult(false);


        private WorkspaceSnapshot CreateSnapshot(bool initialized)
        {
            return new WorkspaceSnapshot(
                RootPath: RootPath,
                WorkspaceVersion: KodaClawWorkspaceLayout.CurrentWorkspaceVersion,
                WorkspaceInitialized: initialized,
                RequiresBootstrap: !initialized,
                ActiveMainSessionId: null,
                DeviceId: null);
        }
    }

    private sealed class TempWorkspaceRoot : IDisposable
    {
        public TempWorkspaceRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "kodaclaw-plugin-registry-unit",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
