using System.Runtime.InteropServices;

namespace KodaClaw.Runtime;

/// <summary>
/// Provides current-machine runtime environment information for injection into session system prompts.
/// Helps the Agent generate platform-appropriate commands (e.g. bash vs cmd.exe, arm64 vs x64 binaries).
/// </summary>
internal static class RuntimeEnvironmentContext
{
    /// <summary>
    /// Builds the prompt lines for the "Runtime Environment" section.
    /// </summary>
    /// <param name="workspaceRoot">Optional workspace root path to include.</param>
    public static IReadOnlyList<string> BuildLines(string? workspaceRoot = null)
    {
        var lines = new List<string>
        {
            $"Platform: {OsPlatformName()} {OsVersion()} ({ArchName()})",
            $"Shell: {DefaultShell()}",
            $"Home: {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}",
        };

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            lines.Add($"Workspace: {workspaceRoot}");
        }

        return lines;
    }

    private static string OsPlatformName()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsLinux()) return "Linux";
        return RuntimeInformation.OSDescription;
    }

    private static string OsVersion()
    {
        // Environment.OSVersion.Version gives the kernel/NT version.
        // On macOS: shows Darwin version (e.g. 15.3), not the marketing version (15.x).
        // On Windows: gives the NT build (e.g. 10.0.22621).
        // Two components (major.minor) are enough context for the Agent.
        return Environment.OSVersion.Version.ToString(2);
    }

    private static string ArchName()
        => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(); // "arm64", "x64", etc.

    private static string DefaultShell()
    {
        if (OperatingSystem.IsWindows())
        {
            return "cmd.exe";
        }

        // $SHELL reflects the user's login shell (bash, zsh, fish…).
        // Fall back to /bin/bash which is what LocalSandbox uses.
        return Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
    }
}
