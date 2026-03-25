using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KodaClaw.Cli;

/// <summary>
/// Lightweight HTTP client for the KodaClaw Gateway API.
/// </summary>
public sealed class HttpGatewayClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public HttpGatewayClient(string baseUrl, string? token = null)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public static string ResolveGatewayUrl() => KcConfig.ResolveGatewayUrl();
    public static string? ResolveToken() => KcConfig.ResolveToken();

    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    public async Task<T?> PostAsync<T>(string path, object? body = null, CancellationToken ct = default)
    {
        using HttpContent content = body != null
            ? JsonContent.Create(body, options: JsonOptions)
            : new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(path, content, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength == 0) return default;
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    public async Task PatchAsync(string path, object body, CancellationToken ct = default)
    {
        using var content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.PatchAsync(path, content, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task PutAsync(string path, object body, CancellationToken ct = default)
    {
        using var content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.PutAsync(path, content, ct);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
