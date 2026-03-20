using System.Globalization;
using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub;

public sealed class ChannelThreadSummaryWriter : IChannelThreadSummaryWriter
{
    private const string ChannelsSubPath = "workspace/channels";
    private const string SummaryFileName = "SUMMARY.md";

    private readonly IWorkspaceService _workspaceService;

    public ChannelThreadSummaryWriter(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task WriteAsync(
        ThreadBinding binding,
        ChannelTurnOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var channelDir = Path.Combine(
            _workspaceService.RootPath,
            ChannelsSubPath,
            SanitizePathSegment(binding.Id));
        Directory.CreateDirectory(channelDir);

        var filePath = Path.Combine(channelDir, SummaryFileName);
        var entry = FormatEntry(binding, outcome);

        await File.AppendAllTextAsync(filePath, entry, cancellationToken);
    }

    private static string FormatEntry(ThreadBinding binding, ChannelTurnOutcome outcome)
    {
        var timestamp = outcome.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss UTC", CultureInfo.InvariantCulture);
        var kindLabel = outcome.Kind switch
        {
            ChannelTurnOutcomeKind.Delivered => "delivered",
            ChannelTurnOutcomeKind.DraftCreated => "draft_created",
            ChannelTurnOutcomeKind.ApprovalRequested => "approval_requested",
            _ => outcome.Kind.ToString().ToLowerInvariant(),
        };

        var preview = NormalizePreview(outcome.Summary);
        return $"- [{timestamp}] {kindLabel}: {preview}{Environment.NewLine}";
    }

    private static string NormalizePreview(string text)
    {
        const int maxLength = 120;
        var normalized = text.Trim().Replace('\n', ' ').Replace('\r', ' ');
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength]}...";
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        return new string(chars);
    }
}
