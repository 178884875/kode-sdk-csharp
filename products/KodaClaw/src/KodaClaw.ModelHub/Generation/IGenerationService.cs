namespace KodaClaw.ModelHub;

/// <summary>
/// Result of an image generation request.
/// </summary>
public record GenerateImageResult(string MediaId, string? RevisedPrompt);

/// <summary>
/// Service for AI content generation (images, etc.) routed independently from chat endpoints.
/// </summary>
public interface IGenerationService
{
    /// <summary>
    /// Generates an image for the given prompt and stores it via <c>IMediaStore</c>.
    /// </summary>
    /// <param name="prompt">Natural language description of the image to generate.</param>
    /// <param name="style">Optional style hint (e.g. "vivid", "natural"). Provider-specific.</param>
    /// <param name="endpointId">Optional endpoint override; if null, uses the default ImageGeneration endpoint.</param>
    Task<GenerateImageResult> GenerateImageAsync(
        string prompt,
        string? style = null,
        string? endpointId = null,
        CancellationToken cancellationToken = default);
}
