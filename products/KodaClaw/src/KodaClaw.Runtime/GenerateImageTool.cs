using System.Text.Json;
using KodaClaw.Contracts;
using KodaClaw.ModelHub;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;

namespace KodaClaw.Runtime;

/// <summary>
/// Tool that generates an image using an AI image-generation endpoint (e.g. DALL-E 3),
/// stores the result via IMediaStore, and publishes it as a Canvas artifact of kind Image.
/// </summary>
public sealed class GenerateImageTool : ToolBase<GenerateImageArgs>
{
    private readonly IGenerationService _generationService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ICanvasArtifactRepository _canvasRepository;
    private readonly ICorrelationContextAccessor? _correlationContextAccessor;
    private readonly IDiagnosticsService? _diagnosticsService;

    public GenerateImageTool(
        IGenerationService generationService,
        IWorkspaceService workspaceService,
        ICanvasArtifactRepository canvasRepository,
        ICorrelationContextAccessor? correlationContextAccessor = null,
        IDiagnosticsService? diagnosticsService = null)
    {
        ArgumentNullException.ThrowIfNull(generationService);
        ArgumentNullException.ThrowIfNull(workspaceService);
        ArgumentNullException.ThrowIfNull(canvasRepository);
        _generationService = generationService;
        _workspaceService = workspaceService;
        _canvasRepository = canvasRepository;
        _correlationContextAccessor = correlationContextAccessor;
        _diagnosticsService = diagnosticsService;
    }

    public override string Name => "generate_image";

    public override string Description =>
        "Generate an image using AI (DALL-E 3) based on a text prompt. " +
        "The result is stored and published as a Canvas artifact of kind 'image'. " +
        "Returns the Canvas artifact id and a URL to view the image.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<GenerateImageArgs>();

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = false,
        RequiresApproval = false,
    };

    protected override async Task<ToolResult> ExecuteAsync(
        GenerateImageArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        GenerateImageResult result;
        try
        {
            result = await _generationService.GenerateImageAsync(
                args.Prompt,
                args.Style,
                cancellationToken: cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _diagnosticsService?.Record(new DiagnosticEvent(
                Id: Guid.NewGuid().ToString("N"),
                Source: "runtime",
                EventType: "image.generation_failed",
                Level: "warning",
                Message: $"Image generation failed: {ex.Message}",
                Timestamp: DateTimeOffset.UtcNow,
                CorrelationId: _correlationContextAccessor?.CorrelationId));
            return ToolResult.Fail($"Image generation failed: {ex.Message}");
        }

        // Publish as a Canvas artifact
        var artifactId = $"img-{result.MediaId[..8]}";
        var entryPath = $"media/{result.MediaId}";
        var title = args.Title ?? TruncatePrompt(args.Prompt, 60);
        var now = DateTimeOffset.UtcNow;

        var artifact = new CanvasArtifact(
            Id: artifactId,
            Title: title,
            Kind: CanvasArtifactKind.Image,
            Summary: result.RevisedPrompt ?? args.Prompt,
            Source: "agent",
            EntryPath: entryPath,
            AssetDirectory: "media",
            CreatedAt: now,
            UpdatedAt: now,
            Route: $"/canvas/{artifactId}",
            SessionId: args.SessionId,
            CorrelationId: _correlationContextAccessor?.CorrelationId,
            MetadataJson: JsonSerializer.Serialize(new { mediaId = result.MediaId }));

        await _canvasRepository.UpsertAsync(artifact, cancellationToken);

        Emit(context, "image_generated", new
        {
            artifactId,
            mediaId = result.MediaId,
            revisedPrompt = result.RevisedPrompt,
        });

        _diagnosticsService?.Record(new DiagnosticEvent(
            Id: Guid.NewGuid().ToString("N"),
            Source: "runtime",
            EventType: "image.generated",
            Level: "info",
            Message: $"Image generated: artifactId={artifactId} mediaId={result.MediaId}",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: _correlationContextAccessor?.CorrelationId));

        return ToolResult.Ok(new
        {
            ok = true,
            artifactId,
            mediaId = result.MediaId,
            mediaUrl = $"/api/media/{result.MediaId}",
            revisedPrompt = result.RevisedPrompt,
        });
    }

    private static string TruncatePrompt(string prompt, int maxLength)
    {
        return prompt.Length <= maxLength ? prompt : prompt[..maxLength] + "…";
    }
}

public sealed class GenerateImageArgs
{
    [ToolParameter(Description = "Text description of the image to generate.")]
    public required string Prompt { get; init; }

    [ToolParameter(Description = "Optional style hint: 'vivid' (high-contrast, dramatic) or 'natural' (more realistic). Defaults to 'vivid'.", Required = false)]
    public string? Style { get; init; }

    [ToolParameter(Description = "Display title for the Canvas artifact. Defaults to a truncated version of the prompt.", Required = false)]
    public string? Title { get; init; }

    [ToolParameter(Description = "Session id to associate with the Canvas artifact.", Required = false)]
    public string? SessionId { get; init; }
}
