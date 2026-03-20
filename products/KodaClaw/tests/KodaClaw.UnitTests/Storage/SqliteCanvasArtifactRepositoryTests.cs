using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KodaClaw.UnitTests.Storage;

public sealed class SqliteCanvasArtifactRepositoryTests
{
    [Fact]
    public async Task Repository_should_round_trip_metadata()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var createdAt = new DateTimeOffset(2026, 3, 18, 9, 30, 0, TimeSpan.Zero);
        var expected = new CanvasArtifact(
            Id: "canvas-report-001",
            Title: "Weekly Report",
            Kind: CanvasArtifactKind.Report,
            Summary: "Weekly KPI summary",
            Source: "automation.scheduler",
            EntryPath: "workspace/canvas/reports/weekly/index.html",
            AssetDirectory: "workspace/canvas/artifacts/reports/weekly",
            CreatedAt: createdAt,
            UpdatedAt: createdAt.AddMinutes(3),
            Route: "/canvas/reports/canvas-report-001",
            SessionId: "session-main",
            CorrelationId: "corr-001",
            MetadataJson: """{"layout":"2x2","widgets":4}""");

        await repository.UpsertAsync(expected);

        var actual = await repository.GetByIdAsync(expected.Id);

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task List_should_filter_by_kind_source_session_id()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var now = new DateTimeOffset(2026, 3, 18, 10, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(BuildArtifact(
            id: "canvas-001",
            kind: CanvasArtifactKind.Dashboard,
            source: "automation.scheduler",
            sessionId: "session-a",
            now: now));
        await repository.UpsertAsync(BuildArtifact(
            id: "canvas-002",
            kind: CanvasArtifactKind.Report,
            source: "automation.scheduler",
            sessionId: "session-a",
            now: now.AddMinutes(1)));
        await repository.UpsertAsync(BuildArtifact(
            id: "canvas-003",
            kind: CanvasArtifactKind.Dashboard,
            source: "manual",
            sessionId: "session-b",
            now: now.AddMinutes(2)));

        var filtered = await repository.ListAsync(new CanvasArtifactQuery(
            Kind: CanvasArtifactKind.Dashboard,
            Source: "automation.scheduler",
            SessionId: "session-a",
            Limit: 20));

        filtered.Should().ContainSingle();
        filtered[0].Id.Should().Be("canvas-001");
    }

    [Theory]
    [InlineData("/tmp/index.html", "workspace/canvas/artifacts/report")]
    [InlineData("workspace/canvas/reports/index.html", "/tmp/assets")]
    [InlineData("workspace/canvas/../escape/index.html", "workspace/canvas/artifacts/report")]
    [InlineData("workspace/canvas/reports/index.html", "workspace/canvas/artifacts/../../escape")]
    public async Task Upsert_should_reject_invalid_paths(string entryPath, string assetDirectory)
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var artifact = new CanvasArtifact(
            Id: "canvas-invalid-paths",
            Title: "Invalid paths",
            Kind: CanvasArtifactKind.Html,
            Summary: "Should fail path validation",
            Source: "tests",
            EntryPath: entryPath,
            AssetDirectory: assetDirectory,
            CreatedAt: new DateTimeOffset(2026, 3, 18, 11, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 3, 18, 11, 1, 0, TimeSpan.Zero));

        var action = () => repository.UpsertAsync(artifact);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Repository_should_initialize_canvas_artifacts_table_in_control_plane_db()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        await repository.UpsertAsync(BuildArtifact(
            id: "canvas-init-001",
            kind: CanvasArtifactKind.Board,
            source: "tests",
            sessionId: null,
            now: new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero)));

        var databasePath = Path.Combine(
            workspace.Path,
            KodaClawWorkspaceLayout.ConfigDirectory,
            KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);

        File.Exists(databasePath).Should().BeTrue();

        var connectionBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        };

        await using var connection = new SqliteConnection(connectionBuilder.ToString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table' AND name = 'canvas_artifacts'
            LIMIT 1;
            """;

        var tableName = await command.ExecuteScalarAsync();
        tableName.Should().Be("canvas_artifacts");
    }

    private static SqliteCanvasArtifactRepository CreateRepository(string rootPath)
    {
        return new SqliteCanvasArtifactRepository(new FakeWorkspaceService(rootPath));
    }

    private static CanvasArtifact BuildArtifact(
        string id,
        CanvasArtifactKind kind,
        string source,
        string? sessionId,
        DateTimeOffset now)
    {
        return new CanvasArtifact(
            Id: id,
            Title: $"Artifact {id}",
            Kind: kind,
            Summary: "Canvas summary",
            Source: source,
            EntryPath: $"workspace/canvas/{id}/index.html",
            AssetDirectory: $"workspace/canvas/artifacts/{id}",
            CreatedAt: now,
            UpdatedAt: now,
            SessionId: sessionId,
            CorrelationId: null,
            MetadataJson: """{"ok":true}""");
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
                "kodaclaw-canvas-artifacts-unit",
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
