using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.Gateway;
using KodaClaw.Storage.Json.Repositories;
using Xunit;

namespace KodaClaw.UnitTests.Gateway;

/// <summary>
/// KC-DOCKER-005: Unit tests for ConfigBootstrapWriter — the shared writer used by
/// both the ENV-var bootstrap path and the Setup Wizard POST /setup/complete.
/// </summary>
public sealed class ConfigBootstrapWriterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly FakeSecretStoreForBootstrap _secretStore = new();

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private ConfigBootstrapWriter CreateWriter()
    {
        var registry = new JsonModelRegistryRepository(_tempDir);
        return new ConfigBootstrapWriter(registry, _secretStore);
    }

    private JsonModelRegistryRepository CreateRegistry() => new(_tempDir);

    // ── ENV-var path: no-op guards ───────────────────────────────────────────

    [Fact]
    public async Task WriteIfAbsent_skips_when_registry_already_has_endpoints()
    {
        var registry = CreateRegistry();
        var existing = BuildEndpoint("already-exists", ModelProviderKind.Anthropic, isDefault: true);
        await registry.AddAsync(existing);

        var writer = new ConfigBootstrapWriter(registry, _secretStore);
        await writer.WriteIfAbsentAsync("sk-ant-NEW", null);

        var all = await registry.ListAsync();
        all.Should().HaveCount(1, because: "WriteIfAbsent must not add when registry is nonempty");
        all[0].Id.Should().Be("already-exists");
    }

    [Fact]
    public async Task WriteIfAbsent_is_noop_when_both_keys_are_null()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync(null, null);

        var all = await CreateRegistry().ListAsync();
        all.Should().BeEmpty();
        _secretStore.StoredSecrets.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteIfAbsent_is_noop_when_both_keys_are_whitespace()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync("   ", "  ");

        var all = await CreateRegistry().ListAsync();
        all.Should().BeEmpty();
    }

    // ── ENV-var path: Anthropic key ──────────────────────────────────────────

    [Fact]
    public async Task WriteIfAbsent_creates_anthropic_endpoint_and_marks_default()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync("sk-ant-test", null);

        var registry = CreateRegistry();
        var all = await registry.ListAsync();
        all.Should().HaveCount(1);

        var ep = all[0];
        ep.Provider.Should().Be(ModelProviderKind.Anthropic);
        ep.IsDefault.Should().BeTrue();
        ep.Enabled.Should().BeTrue();
        ep.ApiKeySecretRef.Should().NotBeNullOrWhiteSpace();
        ep.ApiKeyEnvironmentVariable.Should().BeNull();
    }

    [Fact]
    public async Task WriteIfAbsent_stores_anthropic_key_in_secret_store()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync("sk-ant-test", null);

        _secretStore.StoredSecrets.Should().ContainValue("sk-ant-test");
    }

    // ── ENV-var path: OpenAI key ─────────────────────────────────────────────

    [Fact]
    public async Task WriteIfAbsent_creates_openai_endpoint_when_only_openai_key_provided()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync(null, "sk-openai-test");

        var all = await CreateRegistry().ListAsync();
        all.Should().HaveCount(1);
        all[0].Provider.Should().Be(ModelProviderKind.OpenAI);
        all[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task WriteIfAbsent_stores_openai_key_in_secret_store()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync(null, "sk-openai-test");

        _secretStore.StoredSecrets.Should().ContainValue("sk-openai-test");
    }

    // ── ENV-var path: precedence ─────────────────────────────────────────────

    [Fact]
    public async Task WriteIfAbsent_prefers_anthropic_when_both_keys_provided()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync("sk-ant-test", "sk-openai-test");

        var all = await CreateRegistry().ListAsync();
        all.Should().HaveCount(1);
        all[0].Provider.Should().Be(ModelProviderKind.Anthropic,
            because: "Anthropic takes precedence over OpenAI when both keys are provided");

        _secretStore.StoredSecrets.Values
            .Should().ContainSingle().Which.Should().Be("sk-ant-test");
    }

    // ── ENV-var path: key trimming ───────────────────────────────────────────

    [Fact]
    public async Task WriteIfAbsent_trims_api_key_before_storing()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync("  sk-ant-padded  ", null);

        _secretStore.StoredSecrets.Values.Should().ContainSingle().Which.Should().Be("sk-ant-padded");
    }

    // ── Setup Wizard path (provider overload) ────────────────────────────────

    [Fact]
    public async Task WriteIfAbsent_provider_creates_endpoint_and_marks_default()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync(
            provider:    ModelProviderKind.OpenAICompatible,
            modelId:     "glm-4-flash",
            apiKey:      "glm-key-test",
            baseUrl:     "https://open.bigmodel.cn/api/paas/v4",
            displayName: "GLM-4 Flash");

        var all = await CreateRegistry().ListAsync();
        all.Should().HaveCount(1);

        var ep = all[0];
        ep.Provider.Should().Be(ModelProviderKind.OpenAICompatible);
        ep.ModelId.Should().Be("glm-4-flash");
        ep.BaseUrl.Should().Be("https://open.bigmodel.cn/api/paas/v4");
        ep.IsDefault.Should().BeTrue();
        ep.DisplayName.Should().Be("GLM-4 Flash");
        _secretStore.StoredSecrets.Should().ContainValue("glm-key-test");
    }

    [Fact]
    public async Task WriteIfAbsent_provider_skips_when_registry_nonempty()
    {
        var registry = CreateRegistry();
        await registry.AddAsync(BuildEndpoint("existing", ModelProviderKind.Anthropic, isDefault: true));

        var writer = new ConfigBootstrapWriter(registry, _secretStore);
        await writer.WriteIfAbsentAsync(
            provider: ModelProviderKind.OpenAI,
            modelId:  "gpt-4o",
            apiKey:   "sk-openai-test",
            baseUrl:  null,
            displayName: null);

        var all = await registry.ListAsync();
        all.Should().HaveCount(1, because: "WriteIfAbsent must not overwrite existing registry");
        all[0].Id.Should().Be("existing");
    }

    [Fact]
    public async Task WriteIfAbsent_provider_accepts_null_api_key_for_local_providers()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync(
            provider:    ModelProviderKind.OpenAICompatible,
            modelId:     "llama3.2",
            apiKey:      null,           // Ollama: no key needed
            baseUrl:     "http://localhost:11434/v1",
            displayName: null);

        var all = await CreateRegistry().ListAsync();
        all.Should().HaveCount(1);
        all[0].ModelId.Should().Be("llama3.2");
        all[0].ApiKeySecretRef.Should().BeNull(because: "no key was provided");
        _secretStore.StoredSecrets.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteIfAbsent_provider_trims_api_key_and_baseurl()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync(
            provider:    ModelProviderKind.OpenAICompatible,
            modelId:     "deepseek-chat",
            apiKey:      "  sk-ds-padded  ",
            baseUrl:     "  https://api.deepseek.com/v1  ",
            displayName: null);

        _secretStore.StoredSecrets.Values.Should().ContainSingle().Which.Should().Be("sk-ds-padded");
        var ep = (await CreateRegistry().ListAsync())[0];
        ep.BaseUrl.Should().Be("https://api.deepseek.com/v1");
    }

    [Fact]
    public async Task WriteIfAbsent_provider_uses_provider_model_as_displayname_when_none_given()
    {
        var writer = CreateWriter();
        await writer.WriteIfAbsentAsync(
            provider:    ModelProviderKind.OpenAI,
            modelId:     "gpt-4o",
            apiKey:      "sk-openai-test",
            baseUrl:     null,
            displayName: null);

        var ep = (await CreateRegistry().ListAsync())[0];
        ep.DisplayName.Should().Contain("OpenAI").And.Contain("gpt-4o");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ModelEndpoint BuildEndpoint(string id, ModelProviderKind provider, bool isDefault = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new ModelEndpoint(
            Id: id,
            DisplayName: "test",
            Provider: provider,
            ModelId: "test-model",
            BaseUrl: null,
            ApiKeyEnvironmentVariable: null,
            ApiKeySecretRef: null,
            Enabled: true,
            Capabilities: ModelCapabilitySet.Text,
            IsDefault: isDefault,
            CreatedAt: now,
            UpdatedAt: now);
    }
}

/// <summary>In-memory ISecretStore for tests that captures written secrets.</summary>
internal sealed class FakeSecretStoreForBootstrap : ISecretStore
{
    public Dictionary<string, string> StoredSecrets { get; } = new();

    public Task<string?> GetAsync(SecretRef secretRef, CancellationToken cancellationToken = default)
    {
        StoredSecrets.TryGetValue(secretRef.ToReferenceString(), out var v);
        return Task.FromResult<string?>(v);
    }

    public Task UpsertAsync(SecretRef secretRef, string secretValue, CancellationToken cancellationToken = default)
    {
        StoredSecrets[secretRef.ToReferenceString()] = secretValue;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(SecretRef secretRef, CancellationToken cancellationToken = default)
    {
        StoredSecrets.Remove(secretRef.ToReferenceString());
        return Task.CompletedTask;
    }

    public Task<SecretDescriptor> DescribeAsync(SecretRef secretRef, CancellationToken cancellationToken = default)
        => Task.FromResult(new SecretDescriptor(secretRef, Exists: StoredSecrets.ContainsKey(secretRef.ToReferenceString()), IsReadOnly: false));
}
