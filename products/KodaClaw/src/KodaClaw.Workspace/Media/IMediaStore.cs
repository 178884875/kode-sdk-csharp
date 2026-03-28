using KodaClaw.Contracts;

namespace KodaClaw.Workspace;

public interface IMediaStore
{
    /// <summary>Saves a media file and returns its metadata.</summary>
    Task<MediaMeta> StoreAsync(string fileName, string contentType, Stream data, CancellationToken cancellationToken = default);

    /// <summary>Returns metadata for the given media ID, or null if not found.</summary>
    Task<MediaMeta?> GetMetaAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Opens the media file for reading. Returns null if not found.</summary>
    Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default);
}
