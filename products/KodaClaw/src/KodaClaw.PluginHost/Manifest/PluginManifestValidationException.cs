namespace KodaClaw.PluginHost.Manifest;

public sealed class PluginManifestValidationException : Exception
{
    public PluginManifestValidationException(string message, IReadOnlyList<string> errors)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
