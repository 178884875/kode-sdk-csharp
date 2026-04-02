using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.Storage.Json.Repositories;
using Xunit;

namespace KodaClaw.UnitTests.ModelHub;

public sealed class JsonModelRegistryRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private JsonModelRegistryRepository CreateRepository() => new(_tempDir);

    [Fact]
    public async Task List_should_start_empty()
    {
        var repository = CreateRepository();

        var list = await repository.ListAsync();

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_should_store_endpoint()
    {
        var repository = CreateRepository();
        var endpoint = BuildEndpoint("endpoint-001", isDefault: true);

        await repository.AddAsync(endpoint);
        var stored = await repository.GetByIdAsync(endpoint.Id);

        stored.Should().Be(endpoint);
    }

    [Fact]
    public async Task Update_should_replace_existing_endpoint()
    {
        var repository = CreateRepository();
        var endpoint = BuildEndpoint("endpoint-002");
        await repository.AddAsync(endpoint);
        var updated = endpoint with
        {
            DisplayName = "Updated",
            Provider = ModelProviderKind.AnthropicCompatible,
            ModelId = "claude-3.7-sonnet",
            BaseUrl = "https://anthropic.proxy.test",
            Capabilities = ModelCapabilitySet.Text,
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
        var repository = CreateRepository();
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
        var repository = CreateRepository();
        var endpoint = BuildEndpoint("endpoint-005", isDefault: true);
        await repository.AddAsync(endpoint);

        var deleted = await repository.DeleteAsync(endpoint.Id);
        var list = await repository.ListAsync();

        deleted.Should().BeTrue();
        list.Should().BeEmpty();
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
            Capabilities: ModelCapabilitySet.Text,
            IsDefault: isDefault,
            CreatedAt: now,
            UpdatedAt: now);
    }
}
