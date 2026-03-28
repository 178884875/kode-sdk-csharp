using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub;

public sealed record ChannelPolicyDecision(
    ChannelThreadType ThreadType,
    SessionKind SessionKind,
    bool IsMuted,
    bool CanDirectReply,
    bool LoadAgents,
    bool LoadIdentity,
    bool LoadSoul,
    bool LoadUserProfile,
    bool LoadLongTermMemory,
    bool LoadRecentThreadSummary);
