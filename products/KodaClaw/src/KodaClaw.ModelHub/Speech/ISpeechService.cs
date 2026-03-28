using KodaClaw.Contracts;

namespace KodaClaw.ModelHub;

public record GenerateSpeechResult(string MediaId, string ContentType);

public interface ISpeechService
{
    /// <summary>
    /// 将文字合成为音频，存入 MediaStore，返回 mediaId 和 content-type。
    /// text 可包含 MiMo style 标签（如 &lt;style&gt;速度=快,情感=高兴&lt;/style&gt;）。
    /// </summary>
    Task<GenerateSpeechResult> GenerateSpeechAsync(
        string text,
        string? voice = null,
        string? endpointId = null,
        CancellationToken cancellationToken = default);
}
