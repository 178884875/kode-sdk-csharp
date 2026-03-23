using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.ControlPlane;
using Xunit;

namespace KodaClaw.UnitTests.ControlPlane;

public sealed class SqliteSettingsRepositoryTests
{
    [Fact]
    public async Task Repository_should_return_default_when_no_snapshot()
    {
        using var tempRoot = new TempWorkspaceRoot();
        var repository = CreateRepository(tempRoot.Path);

        var settings = await repository.GetAsync();

        settings.Should().Be(KodaClawSettings.Default);
    }

    [Fact]
    public async Task Repository_should_persist_and_round_trip_settings()
    {
        using var tempRoot = new TempWorkspaceRoot();
        var repository = CreateRepository(tempRoot.Path);
        var expected = new KodaClawSettings(
            DefaultLandingRoute: "/home",
            Theme: ThemeMode.Dark,
            RequireApprovalForExternalActions: false,
            NotificationsEnabled: false,
            QuietHoursEnabled: true,
            QuietHoursStartLocalTime: "07:30",
            QuietHoursEndLocalTime: "22:15",
            UpdatedAt: new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero));

        await repository.SaveAsync(expected);

        var actual = await repository.GetAsync();
        var databasePath = Path.Combine(
            tempRoot.Path,
            KodaClawWorkspaceLayout.ConfigDirectory,
            KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);

        actual.Should().Be(expected);
        File.Exists(databasePath).Should().BeTrue();
    }

    [Fact]
    public async Task Repository_should_overwrite_existing_snapshot()
    {
        using var tempRoot = new TempWorkspaceRoot();
        var repository = CreateRepository(tempRoot.Path);
        var first = KodaClawSettings.Default with
        {
            NotificationsEnabled = true,
            QuietHoursEnabled = true,
            QuietHoursStartLocalTime = "08:00",
            QuietHoursEndLocalTime = "20:00",
            UpdatedAt = new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero),
        };
        var second = first with
        {
            NotificationsEnabled = false,
            QuietHoursEnabled = false,
            UpdatedAt = first.UpdatedAt.AddHours(1),
        };

        await repository.SaveAsync(first);
        await repository.SaveAsync(second);

        var actual = await repository.GetAsync();

        actual.Should().Be(second);
    }

    [Theory]
    [InlineData("dent", nameof(KodaClawSettings.QuietHoursStartLocalTime))]
    [InlineData("25:00", nameof(KodaClawSettings.QuietHoursEndLocalTime))]
    public async Task Repository_should_reject_invalid_quiet_hours(string value, string parameterName)
    {
        using var tempRoot = new TempWorkspaceRoot();
        var repository = CreateRepository(tempRoot.Path);
        var settings = new KodaClawSettings(
            DefaultLandingRoute: "/home",
            Theme: ThemeMode.Light,
            RequireApprovalForExternalActions: true,
            NotificationsEnabled: true,
            QuietHoursEnabled: true,
            QuietHoursStartLocalTime: parameterName == nameof(KodaClawSettings.QuietHoursStartLocalTime) ? value : "07:00",
            QuietHoursEndLocalTime: parameterName == nameof(KodaClawSettings.QuietHoursEndLocalTime) ? value : "18:00",
            UpdatedAt: new DateTimeOffset(2026, 3, 18, 14, 0, 0, TimeSpan.Zero));

        var action = () => repository.SaveAsync(settings);

        await action.Should().ThrowAsync<ArgumentException>().Where(ex => ex.ParamName == parameterName);
    }

    private static SqliteSettingsRepository CreateRepository(string rootPath)
    {
        return new SqliteSettingsRepository(new FakeWorkspaceService(rootPath));
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
            return Task.FromResult(CreateSnapshot());
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

        private WorkspaceSnapshot CreateSnapshot(bool initialized = false)
        {
            return new WorkspaceSnapshot(
                RootPath,
                KodaClawWorkspaceLayout.CurrentWorkspaceVersion,
                initialized,
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
                "kodaclaw-settings-unit",
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
