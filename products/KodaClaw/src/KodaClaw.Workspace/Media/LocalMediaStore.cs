using System.Text.Json;
using KodaClaw.Contracts;

namespace KodaClaw.Workspace;

/// <summary>
/// File-system-backed media store under <c>{workspace}/media/</c>.
/// Each media object is stored as two files:
/// <list type="bullet">
///   <item><c>{id}.bin</c> — raw binary data</item>
///   <item><c>{id}.meta.json</c> — serialized <see cref="MediaMeta"/></item>
/// </list>
/// </summary>
public sealed class LocalMediaStore : IMediaStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _mediaDir;

    public LocalMediaStore(KodaClawWorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _mediaDir = Path.Combine(options.ResolveRootPath(), KodaClawWorkspaceLayout.MediaDirectory);
    }

    public async Task<MediaMeta> StoreAsync(
        string fileName,
        string contentType,
        Stream data,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_mediaDir);

        var id = Guid.NewGuid().ToString("N");
        var binPath = Path.Combine(_mediaDir, $"{id}.bin");
        var metaPath = Path.Combine(_mediaDir, $"{id}.meta.json");

        long size;
        await using (var fs = File.Create(binPath))
        {
            await data.CopyToAsync(fs, cancellationToken);
            size = fs.Length;
        }

        var meta = new MediaMeta(
            Id: id,
            FileName: fileName,
            ContentType: contentType,
            SizeBytes: size,
            StoredAt: DateTimeOffset.UtcNow);

        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, JsonOptions), cancellationToken);
        return meta;
    }

    public async Task<MediaMeta?> GetMetaAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(id)) return null;
        var metaPath = Path.Combine(_mediaDir, $"{id}.meta.json");
        if (!File.Exists(metaPath)) return null;
        var json = await File.ReadAllTextAsync(metaPath, cancellationToken);
        return JsonSerializer.Deserialize<MediaMeta>(json);
    }

    public Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(id)) return Task.FromResult<Stream?>(null);
        var binPath = Path.Combine(_mediaDir, $"{id}.bin");
        if (!File.Exists(binPath)) return Task.FromResult<Stream?>(null);
        Stream stream = File.OpenRead(binPath);
        return Task.FromResult<Stream?>(stream);
    }

    private static bool IsValidId(string id) =>
        !string.IsNullOrWhiteSpace(id) &&
        id.Length <= 64 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
}
