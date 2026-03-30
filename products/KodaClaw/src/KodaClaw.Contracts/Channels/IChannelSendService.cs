namespace KodaClaw.Contracts;

public interface IChannelSendService
{
    Task<ChannelSendResult> SendAsync(string bindingId, string text, string? mediaId = null, string? metadataJson = null, CancellationToken cancellationToken = default);
}

public sealed record ChannelSendResult(bool Ok, string BindingId, DateTimeOffset SentAt);
