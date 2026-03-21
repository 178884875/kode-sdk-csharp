using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KodaClaw.Contracts;

namespace KodaClaw.Gateway;

public sealed class ModelConnectionTestService(
    ModelPresetService presetService,
    IHttpClientFactory httpClientFactory)
{
    private static readonly Dictionary<string, string> ProviderBaseUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Anthropic"] = "https://api.anthropic.com/v1",
        ["OpenAI"] = "https://api.openai.com/v1",
        ["DeepSeek"] = "https://api.deepseek.com/v1",
        ["Google"] = "https://generativelanguage.googleapis.com/v1beta/openai",
        ["Ollama"] = "http://localhost:11434/v1",
    };

    public async Task<ModelConnectionTestResponse> TestAsync(ModelConnectionTestRequest request, CancellationToken ct)
    {
        string? modelId = request.ModelId;
        string? baseUrl = request.BaseUrl;
        string? provider = null;

        if (!string.IsNullOrEmpty(request.PresetId))
        {
            var preset = presetService.GetById(request.PresetId);
            if (preset != null)
            {
                modelId ??= preset.ModelId;
                baseUrl ??= preset.BaseUrl;
                provider = preset.Provider;
            }
        }

        if (string.IsNullOrEmpty(modelId))
            return new ModelConnectionTestResponse(false, 0, null, "model_id_required");

        // Determine base URL
        if (string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(provider))
            ProviderBaseUrls.TryGetValue(provider, out baseUrl);

        if (string.IsNullOrEmpty(baseUrl))
            baseUrl = "https://api.openai.com/v1";

        var sw = Stopwatch.StartNew();
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var requestBody = JsonSerializer.Serialize(new
            {
                model = modelId,
                messages = new[] { new { role = "user", content = "Hi" } },
                max_tokens = 1
            });

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
            httpReq.Headers.Add("Authorization", $"Bearer {request.ApiKey}");
            httpReq.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(httpReq, ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
                return new ModelConnectionTestResponse(true, (int)sw.ElapsedMilliseconds, modelId, null);

            var errorCode = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "authentication_error",
                System.Net.HttpStatusCode.Forbidden => "permission_denied",
                System.Net.HttpStatusCode.TooManyRequests => "rate_limit_exceeded",
                _ => $"http_{(int)response.StatusCode}"
            };
            return new ModelConnectionTestResponse(false, (int)sw.ElapsedMilliseconds, modelId, errorCode);
        }
        catch (TaskCanceledException)
        {
            return new ModelConnectionTestResponse(false, (int)sw.ElapsedMilliseconds, modelId, "timeout");
        }
        catch (Exception)
        {
            return new ModelConnectionTestResponse(false, (int)sw.ElapsedMilliseconds, modelId, "network_error");
        }
    }
}
