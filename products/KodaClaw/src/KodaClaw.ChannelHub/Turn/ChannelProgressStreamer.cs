using Kode.Agent.Sdk.Core.Abstractions;

namespace KodaClaw.ChannelHub.Turn;

/// <summary>
/// Consumes EventBus progress events and pushes intermediate thinking text to the channel.
///
/// Strategy: buffer TextChunkEndEvent text and flush only when a non-channel_send tool starts,
/// signalling that the buffered text was genuine "thinking before tool call" progress.
/// Text immediately before a channel_send tool call is cleared without sending — channel_send
/// itself will deliver the final reply. Text remaining on DoneEvent is also cleared, handled
/// by the Orchestrator's fallback delivery.
/// </summary>
internal static class ChannelProgressStreamer
{
    private const string ChannelSendToolName = "channel_send";

    /// <returns>True if at least one intermediate progress message was pushed.</returns>
    public static async Task<bool> StreamAsync(
        IAsyncEnumerable<EventEnvelope> events,
        Func<string, CancellationToken, Task> sendAsync,
        CancellationToken cancellationToken)
    {
        var sent = false;
        string? pending = null;

        await foreach (var envelope in events.WithCancellation(cancellationToken))
        {
            switch (envelope.Event)
            {
                case TextChunkEndEvent end when !string.IsNullOrWhiteSpace(end.Text):
                    pending = end.Text.Trim();
                    break;

                case ToolStartEvent tool:
                    if (string.Equals(tool.Call.Name, ChannelSendToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        // The buffered text is the same content channel_send will deliver — discard it.
                        pending = null;
                    }
                    else if (pending is not null)
                    {
                        // Genuine thinking text before a tool call — push as progress.
                        var text = pending;
                        pending = null;
                        _ = Task.Run(() => sendAsync(text, CancellationToken.None), CancellationToken.None);
                        sent = true;
                    }
                    break;

                case DoneEvent:
                    // Remaining text is the final reply — let Orchestrator fallback handle it.
                    return sent;
            }
        }

        return sent;
    }
}
