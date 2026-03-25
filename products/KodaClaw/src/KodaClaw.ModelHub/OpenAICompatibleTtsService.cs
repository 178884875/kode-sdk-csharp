using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KodaClaw.Contracts;
using KodaClaw.Workspace;
using Microsoft.Extensions.Logging;

namespace KodaClaw.ModelHub;

/// <summary>
/// OpenAI-compatible TTS service.
/// Supports MiMo V2 TTS and OpenAI TTS-1 (both expose /v1/audio/speech).
/// </summary>
public sealed class OpenAICompatibleTtsService : ISpeechService
{
    private static readonly HttpClient SharedClient = new();

    private readonly IModelRegistryRepository _registry;
    private readonly ISecretStore _secretStore;
    private readonly IMediaStore _mediaStore;
    private readonly ILogger<OpenAICompatibleTtsService>? _logger;

    public OpenAICompatibleTtsService(
        IModelRegistryRepository registry,
        ISecretStore secretStore,
        IMediaStore mediaStore,
        ILogger<OpenAICompatibleTtsService>? logger = null)
    {
        _registry = registry;
        _secretStore = secretStore;
        _mediaStore = mediaStore;
        _logger = logger;
    }

    public async Task<GenerateSpeechResult> GenerateSpeechAsync(
        string text,
        string? voice = null,
        string? endpointId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var endpoint = await ResolveEndpointAsync(endpointId, cancellationToken)
            ?? throw new InvalidOperationException(
                "No enabled TextToSpeech endpoint found. Please configure a TTS model in ModelsSettingsDesk.");

        var apiKey = await ResolveApiKeyAsync(endpoint, cancellationToken);
        var effectiveVoice = ResolveVoice(voice, endpoint);
        var mediaId = await CallTtsApiAsync(apiKey, endpoint.BaseUrl, endpoint.ModelId, text, effectiveVoice, cancellationToken);

        _logger?.LogInformation("Speech generated: mediaId={MediaId}, endpointId={EndpointId}, voice={Voice}",
            mediaId, endpoint.Id, effectiveVoice);

        return new GenerateSpeechResult(MediaId: mediaId, ContentType: "audio/mpeg");
    }

    private async Task<ModelEndpoint?> ResolveEndpointAsync(string? endpointId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(endpointId))
            return await _registry.GetByIdAsync(endpointId, cancellationToken);

        return await _registry.ResolveDefaultForAsync(ModelCapabilitySet.TextToSpeech, cancellationToken);
    }

    private async Task<string> ResolveApiKeyAsync(ModelEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKeySecretRef) &&
            SecretRef.TryParse(endpoint.ApiKeySecretRef, out var secretRef))
        {
            var secret = await _secretStore.GetAsync(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(secret))
                return secret;
        }

        if (!string.IsNullOrWhiteSpace(endpoint.ApiKeyEnvironmentVariable))
        {
            var envVal = Environment.GetEnvironmentVariable(endpoint.ApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(envVal))
                return envVal;
        }

        throw new InvalidOperationException($"No API key configured for TTS endpoint '{endpoint.Id}'.");
    }

    private static string ResolveVoice(string? requestedVoice, ModelEndpoint endpoint)
    {
        if (!string.IsNullOrWhiteSpace(requestedVoice))
            return requestedVoice;

        // 根据 endpoint baseUrl 推断默认音色
        var baseUrl = endpoint.BaseUrl ?? string.Empty;
        return baseUrl.Contains("xiaomimimo", StringComparison.OrdinalIgnoreCase)
            ? "mimo_default"
            : "alloy";
    }

    private async Task<string> CallTtsApiAsync(
        string apiKey,
        string? baseUrl,
        string modelId,
        string text,
        string voice,
        CancellationToken cancellationToken)
    {
        var baseEndpoint = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.openai.com/v1"
            : baseUrl.TrimEnd('/');

        // 兼容两种 baseUrl 格式：含 /v1 后缀或不含
        if (!baseEndpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            baseEndpoint += "/v1";

        var url = $"{baseEndpoint}/audio/speech";

        var body = new Dictionary<string, object>
        {
            ["model"] = string.IsNullOrWhiteSpace(modelId) ? "tts-1" : modelId,
            ["input"] = text,
            ["voice"] = voice,
            ["response_format"] = "mp3",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"TTS API error {(int)response.StatusCode}: {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var meta = await _mediaStore.StoreAsync("speech.mp3", "audio/mpeg", stream, cancellationToken);
        return meta.Id;
    }
}
