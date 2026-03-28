using KodaClaw.Contracts;
using KodaClaw.ModelHub;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;

namespace KodaClaw.Runtime;

/// <summary>
/// Tool that synthesizes speech from text using an OpenAI-compatible TTS endpoint
/// (MiMo V2 TTS or OpenAI TTS-1), stores the mp3 via IMediaStore, and returns a mediaId.
/// Deliberately does NOT create a Canvas artifact — audio's primary consumption path
/// is channel_send, not canvas browsing.
/// </summary>
public sealed class GenerateSpeechTool : ToolBase<GenerateSpeechArgs>
{
    private readonly ISpeechService _speechService;
    private readonly IDiagnosticsService? _diagnosticsService;

    public GenerateSpeechTool(
        ISpeechService speechService,
        IDiagnosticsService? diagnosticsService = null)
    {
        ArgumentNullException.ThrowIfNull(speechService);
        _speechService = speechService;
        _diagnosticsService = diagnosticsService;
    }

    public override string Name => "generate_speech";

    public override string Description =>
        "Synthesize speech from text using a TTS model (MiMo V2 or OpenAI TTS-1). " +
        "Stores the generated mp3 and returns a mediaId for use with channel_send. " +
        "Text may contain MiMo style tags, e.g. <style>速度=快,情感=高兴</style>.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<GenerateSpeechArgs>();

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = false,
        RequiresApproval = false,
    };

    protected override async Task<ToolResult> ExecuteAsync(
        GenerateSpeechArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        GenerateSpeechResult result;
        try
        {
            result = await _speechService.GenerateSpeechAsync(
                args.Text,
                args.Voice,
                args.EndpointId,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail($"Speech generation failed: {ex.Message}");
        }

        Emit(context, "speech_generated", new
        {
            mediaId = result.MediaId,
            contentType = result.ContentType,
            voice = args.Voice,
            textLength = args.Text.Length,
            endpointId = args.EndpointId,
        });

        _diagnosticsService?.Record(new DiagnosticEvent(
            Id: Guid.NewGuid().ToString("N"),
            Source: "runtime",
            EventType: "speech.generated",
            Level: "info",
            Message: $"Speech generated: mediaId={result.MediaId} voice={args.Voice ?? "default"} textLength={args.Text.Length}",
            Timestamp: DateTimeOffset.UtcNow));

        return ToolResult.Ok(new
        {
            ok = true,
            mediaId = result.MediaId,
            mediaUrl = $"/api/media/{result.MediaId}",
            contentType = result.ContentType,
            voice = args.Voice,
        });
    }
}

public sealed class GenerateSpeechArgs
{
    [ToolParameter(Description = "要合成的文字，支持 MiMo style 标签。示例：\"今日天气晴好\" 或 \"<style>速度=快,情感=高兴</style>提醒：会议5分钟后开始\"")]
    public required string Text { get; init; }

    [ToolParameter(Description = "音色 preset。可选值：mimo_default | male | female | Nova_default | alloy | echo | fable。缺省使用 endpoint 默认音色。", Required = false)]
    public string? Voice { get; init; }

    [ToolParameter(Description = "指定 TTS endpoint ID，缺省使用 TextToSpeech 默认 endpoint。", Required = false)]
    public string? EndpointId { get; init; }
}
