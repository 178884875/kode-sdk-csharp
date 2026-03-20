using KodaClaw.Contracts;

namespace KodaClaw.Workspace;

public sealed class KodaClawWorkspaceOptions
{
    public string? RootPath { get; set; }

    public string ResolveRootPath()
    {
        if (!string.IsNullOrWhiteSpace(RootPath))
        {
            return Path.GetFullPath(RootPath);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, KodaClawWorkspaceLayout.RootDirectoryName);
    }
}
