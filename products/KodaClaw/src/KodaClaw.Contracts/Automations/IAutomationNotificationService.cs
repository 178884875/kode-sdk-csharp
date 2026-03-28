namespace KodaClaw.Contracts;

public interface IAutomationNotificationService
{
    Task<IReadOnlyList<ChannelPushResult>> PushAsync(
        IReadOnlyList<string> bindingIds,
        string text,
        CancellationToken cancellationToken = default);
}

public sealed record ChannelPushResult(
    string BindingId,
    bool Ok,
    string? ErrorMessage,
    DateTimeOffset? SentAt);
