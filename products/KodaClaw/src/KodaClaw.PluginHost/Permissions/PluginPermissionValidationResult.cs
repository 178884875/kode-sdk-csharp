namespace KodaClaw.PluginHost.Permissions;

public sealed record PluginPermissionValidationResult(
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static PluginPermissionValidationResult Success { get; } =
        new([]);
}
