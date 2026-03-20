namespace KodaClaw.UnitTests.ChannelHub;

internal sealed class TempWorkspaceRoot : IDisposable
{
    public TempWorkspaceRoot(string prefix)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            prefix,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
