namespace KodaClaw.Contracts;

/// <summary>
/// A reference to a stored media file, used as an outbound attachment.
/// </summary>
public record MediaReference(string MediaId, string ContentType, int? DurationMs = null);
