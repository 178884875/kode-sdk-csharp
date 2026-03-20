using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub;

public interface IChannelThreadSummaryWriter
{
    Task WriteAsync(
        ThreadBinding binding,
        ChannelTurnOutcome outcome,
        CancellationToken cancellationToken = default);
}
