using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.ModelHub;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KodaClaw.UnitTests.ModelHub;

public sealed class ModelRegistryRepositoryTests
{
    [Fact]
    public async Task List_should_start_empty()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);

        var list = await repository.ListAsync();

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_should_store_endpoint()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var endpoint = BuildEndpoint("endpoint-001", isDefault: true);

        await repository.AddAsync(endpoint);
        var stored = await repository.GetByIdAsync(endpoint.Id);

        stored.Should().Be(endpoint);
    }

    [Fact]
    public async Task Update_should_replace_existing_endpoint()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var endpoint = BuildEndpoint("endpoint-002");
        await repository.AddAsync(endpoint);
        var updated = endpoint with
        {
            DisplayName = "Updated",
            Provider = ModelProviderKind.AnthropicCompatible,
            ModelId = "claude-3.7-sonnet",
            BaseUrl = "https://anthropic.proxy.test",
            SupportsToolCalling = false,
            UpdatedAt = endpoint.UpdatedAt.AddMinutes(5),
        };

        var updatedResult = await repository.UpdateAsync(updated);
        var stored = await repository.GetByIdAsync(endpoint.Id);

        updatedResult.Should().BeTrue();
        stored.Should().Be(updated);
    }

    [Fact]
    public async Task SetDefault_should_clear_previous_and_mark_new()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var first = BuildEndpoint("endpoint-003", isDefault: true);
        var second = BuildEndpoint("endpoint-004");
        await repository.AddAsync(first);
        await repository.AddAsync(second);

        var marker = DateTimeOffset.UtcNow;
        var result = await repository.SetDefaultAsync(second.Id, marker);

        var list = await repository.ListAsync();
        result.Should().BeTrue();
        list.Single(endpoint => endpoint.Id == first.Id).IsDefault.Should().BeFalse();
        list.Single(endpoint => endpoint.Id == second.Id).IsDefault.Should().BeTrue();
        list.Single(endpoint => endpoint.Id == second.Id).UpdatedAt.Should().Be(marker);
    }

    [Fact]
    public async Task Delete_should_remove_endpoint()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var endpoint = BuildEndpoint("endpoint-005", isDefault: true);
        await repository.AddAsync(endpoint);

        var deleted = await repository.DeleteAsync(endpoint.Id);
        var list = await repository.ListAsync();

        deleted.Should().BeTrue();
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Repository_should_upgrade_existing_database_with_api_key_secret_ref_column()
    {
        using var workspace = new TempWorkspaceRoot();
        Directory.CreateDirectory(Path.Combine(workspace.Path, KodaClawWorkspaceLayout.ConfigDirectory));
        var databasePath = Path.Combine(
            workspace.Path,
            KodaClawWorkspaceLayout.ConfigDirectory,
            KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);

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
                CREATE TABLE model_endpoints (
                    id TEXT PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    provider TEXT NOT NULL,
                    model_id TEXT NOT NULL,
                    base_url TEXT,
                    api_key_env TEXT,
                    enabled INTEGER NOT NULL,
                    supports_tool_calling INTEGER NOT NULL,
                    is_default INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = CreateRepository(workspace.Path);
        var endpoint = BuildEndpoint("endpoint-legacy");

        await repository.AddAsync(endpoint);
        var stored = await repository.GetByIdAsync(endpoint.Id);

        stored.Should().Be(endpoint);
    }

    private static SqliteModelRegistryRepository CreateRepository(string rootPath)
    {
        return new SqliteModelRegistryRepository(new FakeWorkspaceService(rootPath));
    }

    private static ModelEndpoint BuildEndpoint(string id, bool isDefault = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new ModelEndpoint(
            Id: id,
            DisplayName: $"Model {id}",
            Provider: ModelProviderKind.OpenAICompatible,
            ModelId: "o3",
            BaseUrl: "https://proxy",
            ApiKeyEnvironmentVariable: "MODEL_KEY",
            ApiKeySecretRef: "env:models:MODEL_KEY",
            Enabled: true,
            SupportsToolCalling: true,
            IsDefault: isDefault,
            CreatedAt: now,
            UpdatedAt: now);
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
                "kodaclaw-model-hub-unit",
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
