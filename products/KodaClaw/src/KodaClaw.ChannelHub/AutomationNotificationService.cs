using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub;

public sealed class AutomationNotificationService : IAutomationNotificationService
{
    private readonly IChannelSendService _sendService;

    public AutomationNotificationService(IChannelSendService sendService)
    {
        _sendService = sendService ?? throw new ArgumentNullException(nameof(sendService));
    }

    public async Task<IReadOnlyList<ChannelPushResult>> PushAsync(
        IReadOnlyList<string> bindingIds,
        string text,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ChannelPushResult>(bindingIds.Count);
        foreach (var bindingId in bindingIds)
        {
            try
            {
                var sendResult = await _sendService.SendAsync(bindingId, text, cancellationToken: cancellationToken);
                results.Add(new ChannelPushResult(bindingId, Ok: sendResult.Ok, ErrorMessage: null, SentAt: sendResult.SentAt));
            }
            catch (Exception ex)
            {
                results.Add(new ChannelPushResult(bindingId, Ok: false, ErrorMessage: ex.Message, SentAt: null));
            }
        }
        return results;
    }
}
