using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace KodaClaw.BrowserHub.Screenshot;

/// <summary>
/// Manages one-time screenshot upload tokens and the on-disk storage of
/// uploaded screenshot files.
///
/// <para>
/// Upload flow:
/// <list type="number">
///   <item><description>Gateway calls <see cref="GenerateUploadToken"/> to produce a
///     short-lived (60 s) token and the reserved file path.</description></item>
///   <item><description>Token is forwarded to the extension inside the
///     <c>screenshot</c> BridgeRequest payload.</description></item>
///   <item><description>Extension HTTP-POSTs the image with the token as Bearer credential.</description></item>
///   <item><description>HTTP handler calls <see cref="ValidateAndConsume"/> to verify and
///     claim the token, receiving the target file path to write the bytes.</description></item>
/// </list>
/// </para>
///
/// <para>
/// Storage directory: <c>{workspaceRoot}/browser/screenshots/</c>.
/// Files are named <c>{id}.{ext}</c> and have a 1-hour TTL; a background
/// cleanup loop removes expired files and enforces the 200 MB capacity cap.
/// </para>
/// </summary>
public sealed class ScreenshotUploadService : IAsyncDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int UploadTokenTtlSeconds = 60;
    private const int FileTtlHours = 1;
    private const long MaxStorageBytes = 200L * 1024 * 1024; // 200 MB
    private const int CleanupIntervalMinutes = 10;
    private const string BrowserDirectoryName = "browser";
    private const string ScreenshotsDirectoryName = "screenshots";

    // ── Private state ─────────────────────────────────────────────────────────

    private readonly string _screenshotsDir;

    /// <summary>
    /// In-flight upload tokens. Key = token; value = (deviceId, filePath, issuedAt, used).
    /// </summary>
    private readonly ConcurrentDictionary<string, UploadTokenRecord> _tokens = new();

    private readonly CancellationTokenSource _cts = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the service, creates the storage directory, and starts the
    /// background cleanup loop.
    /// </summary>
    /// <param name="workspaceRoot">
    /// Absolute path to the workspace root (e.g. <c>~/.kodaclaw</c>).
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="workspaceRoot"/> is null or whitespace.
    /// </exception>
    public ScreenshotUploadService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _screenshotsDir = Path.Combine(
            workspaceRoot,
            BrowserDirectoryName,
            ScreenshotsDirectoryName);

        Directory.CreateDirectory(_screenshotsDir);

        _ = RunCleanupLoopAsync(_cts.Token);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a one-time upload token bound to the specified device and request.
    /// </summary>
    /// <param name="deviceId">The device that will perform the upload.</param>
    /// <param name="requestId">Correlation ID of the originating screenshot request.</param>
    /// <param name="format">Image format (<c>jpeg</c> or <c>png</c>).</param>
    /// <returns>
    /// A tuple of:
    /// <list type="bullet">
    ///   <item><description><c>Token</c> — base64url-encoded random string (32 bytes).</description></item>
    ///   <item><description><c>FilePath</c> — reserved absolute path where the file will be saved.</description></item>
    ///   <item><description><c>ExpiresAt</c> — UTC expiry timestamp (Unix ms).</description></item>
    /// </list>
    /// </returns>
    public (string Token, string FilePath, long ExpiresAt) GenerateUploadToken(
        string deviceId,
        string requestId,
        string format = "jpeg")
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncode(tokenBytes);

        var ext = format.Equals("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
        var fileId = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(_screenshotsDir, $"{fileId}.{ext}");

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddSeconds(UploadTokenTtlSeconds);

        _tokens[token] = new UploadTokenRecord(
            DeviceId: deviceId,
            RequestId: requestId,
            FilePath: filePath,
            IssuedAt: issuedAt,
            ExpiresAt: expiresAt,
            Used: false);

        return (token, filePath, expiresAt.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Validates a token and atomically marks it as consumed.
    /// </summary>
    /// <param name="token">The upload token supplied in the Bearer header.</param>
    /// <returns>
    /// The reserved file path when the token is valid, active, and unused;
    /// <c>null</c> when the token is missing, expired, or already consumed.
    /// </returns>
    public string? ValidateAndConsume(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (!_tokens.TryGetValue(token, out var record))
        {
            return null;
        }

        if (record.Used || DateTimeOffset.UtcNow > record.ExpiresAt)
        {
            _tokens.TryRemove(token, out _);
            return null;
        }

        // Mark as used (one-time token)
        var consumed = record with { Used = true };
        if (!_tokens.TryUpdate(token, consumed, record))
        {
            // Another thread consumed it first
            return null;
        }

        return record.FilePath;
    }

    /// <summary>
    /// Returns the file path for a screenshot identified by its file ID (without extension),
    /// or <c>null</c> if the file does not exist.
    /// </summary>
    /// <param name="fileId">The bare file name stem (UUID without extension).</param>
    public string? GetFilePath(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return null;
        }

        foreach (var ext in new[] { "jpg", "png" })
        {
            var path = Path.Combine(_screenshotsDir, $"{fileId}.{ext}");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    // ── Cleanup loop ──────────────────────────────────────────────────────────

    /// <summary>
    /// Background loop executed every <see cref="CleanupIntervalMinutes"/> minutes.
    /// <list type="bullet">
    ///   <item><description>Removes expired in-memory tokens.</description></item>
    ///   <item><description>Deletes screenshot files older than <see cref="FileTtlHours"/> hour(s).</description></item>
    ///   <item><description>Enforces the <see cref="MaxStorageBytes"/> cap by deleting the oldest files first.</description></item>
    /// </list>
    /// </summary>
    private async Task RunCleanupLoopAsync(CancellationToken ct)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(CleanupIntervalMinutes));

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                CleanupExpiredTokens();
                CleanupExpiredFiles();
                EnforceCapacityCap();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    /// <summary>Removes expired or already-used tokens from the in-memory dictionary.</summary>
    private void CleanupExpiredTokens()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var (key, record) in _tokens)
        {
            if (record.Used || now > record.ExpiresAt)
            {
                _tokens.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Deletes screenshot files whose creation time exceeds the TTL.</summary>
    private void CleanupExpiredFiles()
    {
        try
        {
            var ttl = TimeSpan.FromHours(FileTtlHours);
            var now = DateTime.UtcNow;

            foreach (var file in Directory.EnumerateFiles(_screenshotsDir))
            {
                try
                {
                    if (now - File.GetCreationTimeUtc(file) > ttl)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Individual file deletion errors are non-fatal
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Directory enumeration failure — skip this cycle
        }
    }

    /// <summary>
    /// Deletes the oldest files (by creation time) until total directory size
    /// falls below <see cref="MaxStorageBytes"/>.
    /// </summary>
    private void EnforceCapacityCap()
    {
        try
        {
            var files = Directory
                .EnumerateFiles(_screenshotsDir)
                .Select(f => new FileInfo(f))
                .Where(fi => fi.Exists)
                .OrderBy(fi => fi.CreationTimeUtc)
                .ToList();

            var totalBytes = files.Sum(fi => fi.Length);

            foreach (var fi in files)
            {
                if (totalBytes <= MaxStorageBytes)
                {
                    break;
                }

                try
                {
                    totalBytes -= fi.Length;
                    fi.Delete();
                }
                catch
                {
                    // Non-fatal
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Directory enumeration failure — skip
        }
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}

/// <summary>
/// Represents an in-flight screenshot upload token.
/// </summary>
internal sealed record UploadTokenRecord(
    string DeviceId,
    string RequestId,
    string FilePath,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    bool Used);
