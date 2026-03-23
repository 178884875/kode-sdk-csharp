namespace KodaClaw.Contracts;

public sealed record ChatStreamEvent(
    string Type,
    string SessionId,
    int? Step = null,
    long? Sequence = null,
    long? Timestamp = null,
    string? Delta = null,
    string? Reason = null,
    ErrorResponse? Error = null,
    // Approval events (approval_required / approval_decided)
    string? ApprovalId = null,
    string? CallId = null,
    string? ToolName = null,
    string? InputPreview = null,
    string? Decision = null,
    // Tool activity events (tool_activity / agent_working)
    long? DurationMs = null);
