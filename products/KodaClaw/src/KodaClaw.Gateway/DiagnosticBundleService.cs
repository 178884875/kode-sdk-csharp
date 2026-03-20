using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using KodaClaw.Contracts;

namespace KodaClaw.Gateway;

internal sealed class DiagnosticBundleService
{
    private const string ProductName = "KodaClaw";
    private const int BundleFormatVersion = 1;
    private const int DefaultTimelineLimit = 120;
    private const int MaxTimelineLimit = 500;
    private const string BundleArchivePrefix = "kodaclaw-diagnostic-bundle-";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex BearerTokenPattern = new(@"(?i)(bearer\s+)[A-Za-z0-9._~-]+", RegexOptions.Compiled);
    private static readonly Regex QuotedSecretAssignmentPattern = new(
        "(?i)([\"']?(api[-_ ]?key|token|secret|password|authorization|cookie|credential)[\"']?\\s*[:=]\\s*[\"'])([^\"']+)([\"'])",
        RegexOptions.Compiled);
    private static readonly Regex SecretAssignmentPattern = new(
        "(?i)\\b(api[-_ ]?key|token|secret|password|authorization|cookie|credential)\\b\\s*[:=]\\s*([^,;\\s\"']+)",
        RegexOptions.Compiled);
    private static readonly string[] IncludeLabels =
    [
        "control-plane settings snapshot",
        "workspace app config snapshot",
        "diagnostics recent/timeline exports",
        "session meta.json files",
        "repair/update evidence files (optional)",
        "desktop runtime context (optional)",
        "log file metadata summary",
    ];
    private static readonly string[] ExcludeLabels =
    [
        "raw secrets and secret values",
        "messages.json and tool-calls.json session payloads",
        "full chat transcript bodies",
        "raw log file contents",
        "plugin binaries and cache artifacts",
    ];
    private static readonly string[] RedactionRules =
    [
        "secret-like diagnostics attribute values are replaced with [REDACTED]",
        "Bearer token plus quoted/unquoted key=value style secret fragments inside messages are redacted",
        "session exports include meta.json only; messages.json and tool-calls.json stay excluded",
        "logs/ are summarized by metadata only; raw file contents stay excluded",
    ];

    private readonly IWorkspaceService _workspaceService;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly ISettingsRepository _settingsRepository;

    public DiagnosticBundleService(
        IWorkspaceService workspaceService,
        IDiagnosticsService diagnosticsService,
        ISettingsRepository settingsRepository)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _diagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
    }

    public async Task<DiagnosticBundleExportResponse> ExportAsync(
        DiagnosticBundleExportRequest? request,
        CancellationToken cancellationToken = default)
    {
        request ??= new DiagnosticBundleExportRequest();

        var snapshot = await _workspaceService.EnsureInitializedAsync(cancellationToken);
        var appConfig = await _workspaceService.LoadAppConfigAsync(cancellationToken);
        var settings = await _settingsRepository.GetAsync(cancellationToken);
        var requestedSessionId = NormalizeSessionId(request.SessionId);
        var timelineLimit = NormalizeTimelineLimit(request.TimelineLimit);
        var generatedAt = DateTimeOffset.UtcNow;
        var stagingRoot = CreateTemporaryDirectory("kodaclaw-diagnostic-bundle-stage");

        try
        {
            var recentEvents = SanitizeDiagnosticsEvents(_diagnosticsService.GetRecent(timelineLimit));
            var timelineEvents = SanitizeDiagnosticsEvents(_diagnosticsService.Query(new DiagnosticsQuery(
                Limit: timelineLimit,
                SessionId: requestedSessionId)));
            var sourceSummary = BuildSourceSummaries(recentEvents, timelineEvents);
            var logSummary = await BuildLogSummaryAsync(snapshot.RootPath, cancellationToken);
            var notes = BuildNotes(requestedSessionId, request.DesktopContext, recentEvents, timelineEvents, logSummary);

            await WriteJsonAsync(
                Path.Combine(stagingRoot, "snapshot", "control-plane", "settings.json"),
                settings,
                cancellationToken);
            await WriteJsonAsync(
                Path.Combine(stagingRoot, "snapshot", "workspace", "app-config.json"),
                appConfig,
                cancellationToken);
            await WriteJsonAsync(
                Path.Combine(stagingRoot, "snapshot", "diagnostics", "recent.json"),
                new DiagnosticsQueryResponse(recentEvents),
                cancellationToken);
            await WriteJsonAsync(
                Path.Combine(stagingRoot, "snapshot", "diagnostics", "timeline.json"),
                new DiagnosticsQueryResponse(timelineEvents),
                cancellationToken);
            await WriteJsonAsync(
                Path.Combine(stagingRoot, "snapshot", "diagnostics", "source-summary.json"),
                sourceSummary,
                cancellationToken);
            await WriteJsonAsync(
                Path.Combine(stagingRoot, "snapshot", "logs", "log-summary.json"),
                logSummary,
                cancellationToken);

            if (request.DesktopContext is not null)
            {
                await WriteJsonAsync(
                    Path.Combine(stagingRoot, "snapshot", "desktop", "runtime-context.json"),
                    request.DesktopContext,
                    cancellationToken);
            }

            await CopyOptionalRelativeFileAsync(
                snapshot.RootPath,
                stagingRoot,
                Path.Combine(KodaClawWorkspaceLayout.ConfigDirectory, KodaClawWorkspaceLayout.SecretMigrationReportFile),
                Path.Combine("snapshot", "evidence", KodaClawWorkspaceLayout.SecretMigrationReportFile),
                cancellationToken);
            await CopyOptionalRelativeFileAsync(
                snapshot.RootPath,
                stagingRoot,
                Path.Combine(KodaClawWorkspaceLayout.ConfigDirectory, KodaClawWorkspaceLayout.ImportRepairReportFile),
                Path.Combine("snapshot", "evidence", KodaClawWorkspaceLayout.ImportRepairReportFile),
                cancellationToken);
            await CopyOptionalRelativeFileAsync(
                snapshot.RootPath,
                stagingRoot,
                Path.Combine(KodaClawWorkspaceLayout.ConfigDirectory, KodaClawWorkspaceLayout.StartupRepairReportFile),
                Path.Combine("snapshot", "evidence", KodaClawWorkspaceLayout.StartupRepairReportFile),
                cancellationToken);
            await CopyOptionalRelativeFileAsync(
                snapshot.RootPath,
                stagingRoot,
                Path.Combine(KodaClawWorkspaceLayout.ConfigDirectory, KodaClawWorkspaceLayout.UpdateStateFile),
                Path.Combine("snapshot", "evidence", KodaClawWorkspaceLayout.UpdateStateFile),
                cancellationToken);

            await CopySessionMetadataAsync(snapshot.RootPath, stagingRoot, requestedSessionId, cancellationToken);

            var redactionSummary = new DiagnosticBundleRedactionSummary(
                IncludesRawSecrets: false,
                IncludesMessageBodies: false,
                AppliedRules: RedactionRules,
                Notes: notes);
            await WriteJsonAsync(Path.Combine(stagingRoot, "redaction-summary.json"), redactionSummary, cancellationToken);

            var archivePath = ResolveArchivePath(request.ArchivePath, snapshot.RootPath, generatedAt);
            var manifest = await BuildManifestAsync(
                stagingRoot,
                archivePath,
                snapshot.RootPath,
                generatedAt,
                requestedSessionId,
                request.DesktopContext,
                redactionSummary,
                notes,
                cancellationToken);
            await WriteJsonAsync(Path.Combine(stagingRoot, "manifest.json"), manifest, cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            ZipFile.CreateFromDirectory(stagingRoot, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);

            return new DiagnosticBundleExportResponse(
                GeneratedAt: generatedAt,
                WorkspaceRootPath: snapshot.RootPath,
                BundlePath: archivePath,
                Manifest: manifest);
        }
        finally
        {
            DeleteDirectoryIfExists(stagingRoot);
        }
    }

    private static string? NormalizeSessionId(string? sessionId)
    {
        var trimmed = sessionId?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static int NormalizeTimelineLimit(int requested)
    {
        if (requested <= 0)
        {
            return DefaultTimelineLimit;
        }

        return Math.Min(requested, MaxTimelineLimit);
    }

    private static IReadOnlyList<DiagnosticEvent> SanitizeDiagnosticsEvents(IReadOnlyList<DiagnosticEvent> events)
    {
        if (events.Count == 0)
        {
            return [];
        }

        return events
            .Select(static item => new DiagnosticEvent(
                item.Id,
                item.Source,
                item.EventType,
                item.Level,
                RedactText(item.Message) ?? string.Empty,
                item.Timestamp,
                item.CorrelationId,
                item.SessionId,
                SanitizeAttributes(item.Attributes)))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string?>? SanitizeAttributes(IReadOnlyDictionary<string, string?>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return attributes;
        }

        var sanitized = new Dictionary<string, string?>(attributes.Count, StringComparer.Ordinal);
        foreach (var pair in attributes)
        {
            sanitized[pair.Key] = IsSensitiveKey(pair.Key)
                ? "[REDACTED]"
                : RedactText(pair.Value);
        }

        return sanitized;
    }

    private static bool IsSensitiveKey(string key)
    {
        return key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || key.Contains("cookie", StringComparison.OrdinalIgnoreCase)
            || key.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("Key", StringComparison.OrdinalIgnoreCase)
            || key.Contains("apiKey", StringComparison.OrdinalIgnoreCase);
    }

    private static string? RedactText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = BearerTokenPattern.Replace(value, "$1[REDACTED]");
        redacted = QuotedSecretAssignmentPattern.Replace(redacted, static match =>
        {
            var prefix = match.Groups[1].Value;
            var suffix = match.Groups[4].Value;
            return $"{prefix}[REDACTED]{suffix}";
        });
        redacted = SecretAssignmentPattern.Replace(redacted, static match =>
        {
            var label = match.Groups[1].Value;
            return $"{label}=[REDACTED]";
        });
        return redacted;
    }

    private static IReadOnlyList<DiagnosticSourceSummary> BuildSourceSummaries(
        IReadOnlyList<DiagnosticEvent> recentEvents,
        IReadOnlyList<DiagnosticEvent> timelineEvents)
    {
        return recentEvents
            .Concat(timelineEvents)
            .GroupBy(static item => ResolveSourceArea(item.Source), StringComparer.OrdinalIgnoreCase)
            .Select(group => new DiagnosticSourceSummary(
                Area: group.Key,
                EventCount: group.Count(),
                LastEventAt: group.Max(static item => item.Timestamp),
                Sources: group.Select(static item => item.Source).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderByDescending(static item => item.EventCount)
            .ThenBy(static item => item.Area, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveSourceArea(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "unknown";
        }

        var separatorIndex = source.IndexOf('.', StringComparison.Ordinal);
        return separatorIndex <= 0 ? source : source[..separatorIndex];
    }

    private static IReadOnlyList<string> BuildNotes(
        string? requestedSessionId,
        DiagnosticBundleDesktopContext? desktopContext,
        IReadOnlyList<DiagnosticEvent> recentEvents,
        IReadOnlyList<DiagnosticEvent> timelineEvents,
        LogSummary logSummary)
    {
        var notes = new List<string>
        {
            "This bundle is redacted by default and excludes raw secret values, full chat bodies, and raw log file contents.",
        };

        if (requestedSessionId is not null)
        {
            notes.Add($"Timeline export is scoped to session '{requestedSessionId}'.");
        }
        else
        {
            notes.Add("Timeline export is not session-scoped; it contains the latest cross-session diagnostics events.");
        }

        if (desktopContext is null)
        {
            notes.Add("Desktop runtime context was not supplied, so desktop metadata is omitted from this bundle.");
        }

        if (recentEvents.Count == 0 && timelineEvents.Count == 0)
        {
            notes.Add("No diagnostics events were present in memory at export time.");
        }

        if (logSummary.FileCount == 0)
        {
            notes.Add("Workspace logs directory does not contain any files to summarize.");
        }

        return notes;
    }

    private static async Task<LogSummary> BuildLogSummaryAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var logsRoot = Path.Combine(workspaceRoot, KodaClawWorkspaceLayout.LogsDirectory);
        if (!Directory.Exists(logsRoot))
        {
            return new LogSummary(logsRoot, 0, 0, []);
        }

        var files = Directory
            .EnumerateFiles(logsRoot, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(path);
                return new LogFileSummary(
                    Path.GetRelativePath(logsRoot, path).Replace('\\', '/'),
                    info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
            })
            .ToArray();

        var totalBytes = files.Sum(static item => item.SizeBytes);
        await Task.CompletedTask;
        return new LogSummary(logsRoot, files.Length, totalBytes, files);
    }

    private static async Task CopySessionMetadataAsync(
        string workspaceRoot,
        string destinationRoot,
        string? requestedSessionId,
        CancellationToken cancellationToken)
    {
        var sessionsRoot = Path.Combine(workspaceRoot, KodaClawWorkspaceLayout.SessionsDirectory);
        if (!Directory.Exists(sessionsRoot))
        {
            return;
        }

        IEnumerable<string> sessionDirectories = requestedSessionId is null
            ? Directory.EnumerateDirectories(sessionsRoot)
            : [Path.Combine(sessionsRoot, requestedSessionId)];

        foreach (var sessionDirectory in sessionDirectories.OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metaPath = Path.Combine(sessionDirectory, "meta.json");
            if (!File.Exists(metaPath))
            {
                continue;
            }

            var sessionId = Path.GetFileName(sessionDirectory);
            var destinationPath = Path.Combine(destinationRoot, "snapshot", "sessions", sessionId, "meta.json");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var sourceStream = File.OpenRead(metaPath);
            await using var destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }
    }

    private static async Task CopyOptionalRelativeFileAsync(
        string workspaceRoot,
        string destinationRoot,
        string sourceRelativePath,
        string destinationRelativePath,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(workspaceRoot, sourceRelativePath);
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var destinationPath = Path.Combine(destinationRoot, destinationRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var sourceStream = File.OpenRead(sourcePath);
        await using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string path, T payload, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken);
    }

    private static async Task<DiagnosticBundleManifest> BuildManifestAsync(
        string stagingRoot,
        string archivePath,
        string workspaceRoot,
        DateTimeOffset generatedAt,
        string? requestedSessionId,
        DiagnosticBundleDesktopContext? desktopContext,
        DiagnosticBundleRedactionSummary redactionSummary,
        IReadOnlyList<string> notes,
        CancellationToken cancellationToken)
    {
        var files = Directory
            .EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories)
            .Where(static path => !path.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        var entries = new List<DiagnosticBundleManifestEntry>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(new DiagnosticBundleManifestEntry(
                Path: Path.GetRelativePath(stagingRoot, file).Replace('\\', '/'),
                Sha256: await ComputeSha256Async(file, cancellationToken),
                SizeBytes: new FileInfo(file).Length,
                Category: ResolveCategory(file, stagingRoot)));
        }

        return new DiagnosticBundleManifest(
            Product: ProductName,
            FormatVersion: BundleFormatVersion,
            GeneratedAt: generatedAt,
            ArchiveName: Path.GetFileName(archivePath),
            SourceWorkspaceRoot: workspaceRoot,
            RequestedSessionId: requestedSessionId,
            DesktopContext: desktopContext,
            RedactionSummary: redactionSummary,
            Entries: entries,
            Includes: IncludeLabels,
            Excludes: ExcludeLabels,
            Notes: notes);
    }

    private static string ResolveCategory(string filePath, string stagingRoot)
    {
        var relativePath = Path.GetRelativePath(stagingRoot, filePath).Replace('\\', '/');
        if (relativePath.StartsWith("snapshot/diagnostics/", StringComparison.Ordinal))
        {
            return "diagnostics";
        }

        if (relativePath.StartsWith("snapshot/sessions/", StringComparison.Ordinal))
        {
            return "sessionMeta";
        }

        if (relativePath.StartsWith("snapshot/evidence/", StringComparison.Ordinal))
        {
            return "evidence";
        }

        if (relativePath.StartsWith("snapshot/logs/", StringComparison.Ordinal))
        {
            return "logSummary";
        }

        if (relativePath.StartsWith("snapshot/desktop/", StringComparison.Ordinal))
        {
            return "desktop";
        }

        if (relativePath.StartsWith("snapshot/control-plane/", StringComparison.Ordinal))
        {
            return "controlPlane";
        }

        if (relativePath.StartsWith("snapshot/workspace/", StringComparison.Ordinal))
        {
            return "workspace";
        }

        return "bundle";
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ResolveArchivePath(string? requestedArchivePath, string workspaceRoot, DateTimeOffset generatedAt)
    {
        var fullWorkspaceRoot = NormalizePath(workspaceRoot);
        if (!string.IsNullOrWhiteSpace(requestedArchivePath))
        {
            var candidatePath = requestedArchivePath.Trim();
            var resolvedPath = Path.IsPathRooted(candidatePath)
                ? NormalizePath(candidatePath)
                : NormalizePath(Path.Combine(fullWorkspaceRoot, candidatePath));
            if (!IsPathInsideRoot(fullWorkspaceRoot, resolvedPath))
            {
                throw new ArgumentException(
                    "ArchivePath must resolve inside the workspace root.",
                    nameof(requestedArchivePath));
            }

            return resolvedPath;
        }

        var cacheDirectory = Path.Combine(fullWorkspaceRoot, KodaClawWorkspaceLayout.CacheDirectory, "diagnostics");
        var fileName = $"{BundleArchivePrefix}{generatedAt:yyyyMMdd-HHmmss}.zip";
        return Path.Combine(cacheDirectory, fileName);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsPathInsideRoot(string rootPath, string candidatePath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(rootPath, candidatePath, comparison))
        {
            return true;
        }

        return candidatePath.StartsWith(rootPath + Path.DirectorySeparatorChar, comparison);
    }

    private static string CreateTemporaryDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record DiagnosticSourceSummary(
        string Area,
        int EventCount,
        DateTimeOffset LastEventAt,
        IReadOnlyList<string> Sources);

    private sealed record LogSummary(
        string LogsRootPath,
        int FileCount,
        long TotalBytes,
        IReadOnlyList<LogFileSummary> Files);

    private sealed record LogFileSummary(
        string Path,
        long SizeBytes,
        DateTimeOffset LastWriteAtUtc);
}
