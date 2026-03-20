using System.Runtime.CompilerServices;
using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;
using AgentRuntime = Kode.Agent.Sdk.Core.Agent.Agent;

namespace KodaClaw.Runtime;

public sealed class ChatSessionService : IChatSessionService
{
    private const string RuntimeErrorCode = "runtime.error";
    private readonly IMainSessionService _mainSessionService;

    public ChatSessionService(IMainSessionService mainSessionService)
    {
        _mainSessionService = mainSessionService ?? throw new ArgumentNullException(nameof(mainSessionService));
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamMainSessionAsync(
        ChatStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Message is required.", nameof(request));
        }

        MainSessionHandle? handle;
        AgentRuntime agent;
        string? startupError = null;

        try
        {
            handle = await _mainSessionService.EnsureMainSessionAsync(cancellationToken);
            agent = handle.Agent as AgentRuntime
                ?? throw new InvalidOperationException(
                    $"Main session agent must be a {typeof(AgentRuntime).FullName} instance.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            startupError = ex.Message;
            handle = null;
            agent = null!;
        }

        if (startupError is not null)
        {
            yield return CreateErrorEvent(request.SessionId ?? "main-unavailable", startupError);
            yield break;
        }

        var sessionId = handle!.SessionId;

        var stream = agent.Subscribe(
            channels: ["progress", "monitor"],
            opts: new AgentRuntime.SubscribeOptions
            {
                Since = agent.EventBus.GetLastBookmark(),
                Kinds = ["text_chunk", "done", "error"],
            },
            cancellationToken: cancellationToken);

        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        agent.Send(request.Message);

        while (true)
        {
            bool hasNext;
            string? streamError = null;
            try
            {
                hasNext = await moveNextTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                hasNext = false;
                streamError = ex.Message;
            }

            if (streamError is not null)
            {
                yield return CreateErrorEvent(sessionId, streamError);
                yield break;
            }

            if (!hasNext)
            {
                yield break;
            }

            var envelope = enumerator.Current;
            switch (envelope.Event)
            {
                case TextChunkEvent textChunk:
                    yield return new ChatStreamEvent(
                        Type: "text_chunk",
                        SessionId: sessionId,
                        Step: textChunk.Step,
                        Sequence: envelope.Bookmark.Seq,
                        Timestamp: envelope.Bookmark.Timestamp,
                        Delta: textChunk.Delta);
                    break;

                case DoneEvent done:
                    yield return new ChatStreamEvent(
                        Type: "done",
                        SessionId: sessionId,
                        Step: done.Step,
                        Sequence: envelope.Bookmark.Seq,
                        Timestamp: envelope.Bookmark.Timestamp,
                        Reason: done.Reason);
                    yield break;

                case ErrorEvent error:
                    yield return CreateErrorEvent(sessionId, error.Message);
                    yield break;
            }

            moveNextTask = enumerator.MoveNextAsync().AsTask();
        }
    }

    private static ChatStreamEvent CreateErrorEvent(string sessionId, string message)
    {
        return new ChatStreamEvent(
            Type: "error",
            SessionId: sessionId,
            Reason: message,
            Error: new ErrorResponse(
                Code: RuntimeErrorCode,
                Message: message));
    }
}
