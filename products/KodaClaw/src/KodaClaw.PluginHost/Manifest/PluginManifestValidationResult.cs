namespace KodaClaw.PluginHost.Manifest;

public sealed record PluginManifestValidationResult(
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static PluginManifestValidationResult Success { get; } =
        new([]);
}
