using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KodaClaw.Contracts;
using KodaClaw.Workspace;
using Microsoft.Extensions.Logging;

namespace KodaClaw.ModelHub;

/// <summary>
/// OpenAI DALL-E 3 image generation service.
/// Resolves the ImageGeneration endpoint independently from the current chat session.
/// </summary>
public sealed class OpenAIImageGenerationService : IGenerationService
{
    private static readonly HttpClient SharedClient = new();

    private readonly IModelRegistryRepository _registry;
    private readonly ISecretStore _secretStore;
    private readonly IMediaStore _mediaStore;
    private readonly ILogger<OpenAIImageGenerationService>? _logger;

    public OpenAIImageGenerationService(
        IModelRegistryRepository registry,
        ISecretStore secretStore,
        IMediaStore mediaStore,
        ILogger<OpenAIImageGenerationService>? logger = null)
    {
        _registry = registry;
        _secretStore = secretStore;
        _mediaStore = mediaStore;
        _logger = logger;
    }

    public async Task<GenerateImageResult> GenerateImageAsync(
        string prompt,
        string? style = null,
        string? endpointId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var endpoint = await ResolveEndpointAsync(endpointId, cancellationToken)
            ?? throw new InvalidOperationException("No enabled ImageGeneration endpoint found. Configure a model with ImageGeneration capability.");

        var apiKey = await ResolveApiKeyAsync(endpoint, cancellationToken);

        var (imageUrl, revisedPrompt) = await CallDallE3Async(apiKey, endpoint.BaseUrl, endpoint.ModelId, prompt, style, cancellationToken);

        var mediaId = await DownloadAndStoreAsync(imageUrl, cancellationToken);

        if (_logger?.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information) == true)
            _logger.LogInformation("Image generated: mediaId={MediaId}, endpointId={EndpointId}", mediaId, endpoint.Id);
        return new GenerateImageResult(MediaId: mediaId, RevisedPrompt: revisedPrompt);
    }

    private async Task<ModelEndpoint?> ResolveEndpointAsync(string? endpointId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(endpointId))
            return await _registry.GetByIdAsync(endpointId, cancellationToken);

        return await _registry.ResolveDefaultForAsync(ModelCapabilitySet.ImageGeneration, cancellationToken);
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

        throw new InvalidOperationException($"No API key configured for image generation endpoint '{endpoint.Id}'.");
    }

    private async Task<(string Url, string? RevisedPrompt)> CallDallE3Async(
        string apiKey,
        string? baseUrl,
        string modelId,
        string prompt,
        string? style,
        CancellationToken cancellationToken)
    {
        var endpoint = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.openai.com"
            : baseUrl.TrimEnd('/');

        // If the base URL already ends with /v1, don't append it again.
        var url = endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? $"{endpoint}/images/generations"
            : $"{endpoint}/v1/images/generations";
        var body = new Dictionary<string, object?>
        {
            ["model"] = string.IsNullOrWhiteSpace(modelId) ? "dall-e-3" : modelId,
            ["prompt"] = prompt,
            ["n"] = 1,
            ["size"] = "1024x1024",
            ["response_format"] = "url",
        };
        if (!string.IsNullOrWhiteSpace(style))
            body["style"] = style;

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await SharedClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"DALL-E API error {(int)response.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data")[0];
        var imageUrl = data.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("DALL-E response missing image URL.");
        var revisedPrompt = data.TryGetProperty("revised_prompt", out var rp) ? rp.GetString() : null;

        return (imageUrl, revisedPrompt);
    }

    private async Task<string> DownloadAndStoreAsync(string imageUrl, CancellationToken cancellationToken)
    {
        using var response = await SharedClient.GetAsync(imageUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
        var ext = contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
        var fileName = $"generated.{ext}";

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var meta = await _mediaStore.StoreAsync(fileName, contentType, stream, cancellationToken);
        return meta.Id;
    }
}
