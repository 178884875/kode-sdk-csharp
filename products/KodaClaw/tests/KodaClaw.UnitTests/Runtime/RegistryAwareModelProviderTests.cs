using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Providers;
using KodaClaw.Contracts;
using KodaClaw.ModelHub;
using KodaClaw.Runtime;
using Moq;
using Xunit;

namespace KodaClaw.UnitTests.Runtime;

public sealed class RegistryAwareModelProviderTests
{
    private static ModelEndpoint MakeEndpoint(
        ModelProviderKind provider = ModelProviderKind.Anthropic,
        string modelId = "claude-sonnet-4-6",
        string? secretRef = "platform:models:model-abc",
        string? envVar = null) =>
        new ModelEndpoint(
            Id: "model-abc",
            DisplayName: "Test Model",
            Provider: provider,
            ModelId: modelId,
            BaseUrl: null,
            ApiKeyEnvironmentVariable: envVar,
            ApiKeySecretRef: secretRef,
            Enabled: true,
            Capabilities: ModelCapabilitySet.Text,
            IsDefault: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            ContextWindowSize: 128_000);

    // ── ProviderName ──────────────────────────────────────────────────────────

    [Fact]
    public void ProviderName_returns_registry()
    {
        var (sut, _, _, _) = BuildSut();
        sut.ProviderName.Should().Be("registry");
    }

    // ── Registry hit ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_uses_registry_endpoint_when_available()
    {
        var endpoint = MakeEndpoint();
        var (sut, registry, secretStore, factory) = BuildSut();

        registry
            .Setup(r => r.ResolveDefaultForAsync(
                ModelCapabilitySet.Text,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint);

        secretStore
            .Setup(s => s.GetAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("sk-test-key");

        var innerProvider = new Mock<IModelProvider>();
        innerProvider
            .Setup(p => p.CompleteAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelResponse
            {
                Content = [],
                StopReason = ModelStopReason.EndTurn,
                Usage = new TokenUsage { InputTokens = 0, OutputTokens = 0 },
                Model = endpoint.ModelId,
            });

        factory
            .Setup(f => f.Create(RuntimeProviderKind.Anthropic, It.IsAny<RuntimeConfigurationSnapshot>()))
            .Returns(innerProvider.Object);

        var request = new ModelRequest { Model = "any-model", Messages = [] };
        await sut.CompleteAsync(request);

        factory.Verify(f => f.Create(RuntimeProviderKind.Anthropic, It.IsAny<RuntimeConfigurationSnapshot>()), Times.Once);
        innerProvider.Verify(p => p.CompleteAsync(
            It.Is<ModelRequest>(r => r.Model == endpoint.ModelId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_normalizes_model_id_to_endpoint_modelId()
    {
        var endpoint = MakeEndpoint(modelId: "claude-sonnet-4-6");
        var (sut, registry, secretStore, factory) = BuildSut();

        registry
            .Setup(r => r.ResolveDefaultForAsync(It.IsAny<ModelCapabilitySet>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint);
        secretStore
            .Setup(s => s.GetAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("sk-key");

        ModelRequest? capturedRequest = null;
        var innerProvider = new Mock<IModelProvider>();
        innerProvider
            .Setup(p => p.CompleteAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ModelRequest, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync(new ModelResponse
            {
                Content = [],
                StopReason = ModelStopReason.EndTurn,
                Usage = new TokenUsage { InputTokens = 0, OutputTokens = 0 },
                Model = "claude-sonnet-4-6",
            });
        factory
            .Setup(f => f.Create(It.IsAny<RuntimeProviderKind>(), It.IsAny<RuntimeConfigurationSnapshot>()))
            .Returns(innerProvider.Object);

        await sut.CompleteAsync(new ModelRequest { Model = "ignored-model", Messages = [] });

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Model.Should().Be("claude-sonnet-4-6");
    }

    // ── Registry miss → fallback ──────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_falls_back_to_DynamicModelProvider_when_registry_empty()
    {
        var (sut, registry, _, _) = BuildSut(fallbackResult: BuildFallbackResponse());

        registry
            .Setup(r => r.ResolveDefaultForAsync(It.IsAny<ModelCapabilitySet>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModelEndpoint?)null);

        var request = new ModelRequest { Model = "gpt-4o", Messages = [] };
        var act = async () => await sut.CompleteAsync(request);

        // fallback throws because DynamicModelProvider has no env-var config in test
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "DynamicModelProvider requires env-var config that is absent in tests");
    }

    // ── API key resolution order ──────────────────────────────────────────────

    [Fact]
    public async Task ApiKey_resolved_from_secretRef_first()
    {
        var endpoint = MakeEndpoint(secretRef: "platform:models:model-abc", envVar: "ANTHROPIC_API_KEY");
        var (sut, registry, secretStore, factory) = BuildSut();

        registry
            .Setup(r => r.ResolveDefaultForAsync(It.IsAny<ModelCapabilitySet>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint);
        secretStore
            .Setup(s => s.GetAsync(It.Is<SecretRef>(sr => sr.ToReferenceString() == "platform:models:model-abc"), It.IsAny<CancellationToken>()))
            .ReturnsAsync("secret-from-keychain");

        RuntimeConfigurationSnapshot? capturedSnapshot = null;
        var innerProvider = new Mock<IModelProvider>();
        innerProvider
            .Setup(p => p.CompleteAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelResponse { Content = [], StopReason = ModelStopReason.EndTurn, Usage = new TokenUsage { InputTokens = 0, OutputTokens = 0 }, Model = endpoint.ModelId });
        factory
            .Setup(f => f.Create(It.IsAny<RuntimeProviderKind>(), It.IsAny<RuntimeConfigurationSnapshot>()))
            .Callback<RuntimeProviderKind, RuntimeConfigurationSnapshot>((_, s) => capturedSnapshot = s)
            .Returns(innerProvider.Object);

        await sut.CompleteAsync(new ModelRequest { Model = endpoint.ModelId, Messages = [] });

        capturedSnapshot!.AnthropicApiKey.Should().Be("secret-from-keychain");
    }

    [Fact]
    public async Task ApiKey_falls_back_to_env_var_when_secretRef_empty()
    {
        const string envKey = "KODA_TEST_ANTHROPIC_KEY_3502";
        Environment.SetEnvironmentVariable(envKey, "env-key-value");

        try
        {
            var endpoint = MakeEndpoint(secretRef: null, envVar: envKey);
            var (sut, registry, secretStore, factory) = BuildSut();

            registry
                .Setup(r => r.ResolveDefaultForAsync(It.IsAny<ModelCapabilitySet>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(endpoint);

            RuntimeConfigurationSnapshot? capturedSnapshot = null;
            var innerProvider = new Mock<IModelProvider>();
            innerProvider
                .Setup(p => p.CompleteAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModelResponse { Content = [], StopReason = ModelStopReason.EndTurn, Usage = new TokenUsage { InputTokens = 0, OutputTokens = 0 }, Model = endpoint.ModelId });
            factory
                .Setup(f => f.Create(It.IsAny<RuntimeProviderKind>(), It.IsAny<RuntimeConfigurationSnapshot>()))
                .Callback<RuntimeProviderKind, RuntimeConfigurationSnapshot>((_, s) => capturedSnapshot = s)
                .Returns(innerProvider.Object);

            await sut.CompleteAsync(new ModelRequest { Model = endpoint.ModelId, Messages = [] });

            capturedSnapshot!.AnthropicApiKey.Should().Be("env-key-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }

    // ── OpenAI provider mapping ───────────────────────────────────────────────

    [Theory]
    [InlineData(ModelProviderKind.OpenAI)]
    [InlineData(ModelProviderKind.OpenAICompatible)]
    public async Task OpenAI_provider_kinds_create_OpenAI_runtime_provider(ModelProviderKind kind)
    {
        var endpoint = MakeEndpoint(provider: kind, secretRef: "platform:models:model-abc");
        var (sut, registry, secretStore, factory) = BuildSut();

        registry
            .Setup(r => r.ResolveDefaultForAsync(It.IsAny<ModelCapabilitySet>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint);
        secretStore
            .Setup(s => s.GetAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("sk-key");
        var innerProvider = new Mock<IModelProvider>();
        innerProvider
            .Setup(p => p.CompleteAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelResponse { Content = [], StopReason = ModelStopReason.EndTurn, Usage = new TokenUsage { InputTokens = 0, OutputTokens = 0 }, Model = "gpt-4o" });
        factory
            .Setup(f => f.Create(RuntimeProviderKind.OpenAI, It.IsAny<RuntimeConfigurationSnapshot>()))
            .Returns(innerProvider.Object);

        await sut.CompleteAsync(new ModelRequest { Model = "gpt-4o", Messages = [] });

        factory.Verify(f => f.Create(RuntimeProviderKind.OpenAI, It.IsAny<RuntimeConfigurationSnapshot>()), Times.Once);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static (
        RegistryAwareModelProvider Sut,
        Mock<IModelRegistryRepository> Registry,
        Mock<ISecretStore> SecretStore,
        Mock<IRuntimeModelProviderFactory> Factory)
        BuildSut(ModelResponse? fallbackResult = null)
    {
        var registry = new Mock<IModelRegistryRepository>();
        var secretStore = new Mock<ISecretStore>();
        var factory = new Mock<IRuntimeModelProviderFactory>();

        var snapshot = new RuntimeConfigurationSnapshot(
            DefaultModel: null, OpenAIApiKey: null, OpenAIBaseUrl: null,
            AnthropicApiKey: null, AnthropicBaseUrl: null);
        var resolver = new Mock<IRuntimeConfigurationResolver>();
        resolver.Setup(r => r.Resolve()).Returns(snapshot);

        var httpFactory = new Mock<System.Net.Http.IHttpClientFactory>();
        httpFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new System.Net.Http.HttpClient());

        var dynamicFallback = new DynamicModelProvider(resolver.Object, factory.Object);

        var sut = new RegistryAwareModelProvider(
            registry.Object,
            secretStore.Object,
            factory.Object,
            dynamicFallback);

        return (sut, registry, secretStore, factory);
    }

    private static ModelResponse BuildFallbackResponse() => new ModelResponse
    {
        Content = [],
        StopReason = ModelStopReason.EndTurn,
        Usage = new TokenUsage { InputTokens = 0, OutputTokens = 0 },
        Model = "gpt-4o",
    };
}
