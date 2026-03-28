namespace KodaClaw.Contracts;

public interface IChatSessionService
{
    IAsyncEnumerable<ChatStreamEvent> StreamMainSessionAsync(
        ChatStreamRequest request,
        CancellationToken cancellationToken = default);
}
