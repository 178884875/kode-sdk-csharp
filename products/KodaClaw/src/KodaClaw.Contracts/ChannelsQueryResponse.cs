namespace KodaClaw.Contracts;

public sealed record ChannelsQueryResponse(
    IReadOnlyList<ChannelThreadSummary> Items);
