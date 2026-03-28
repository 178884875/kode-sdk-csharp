namespace KodaClaw.Contracts;

public sealed record ErrorResponse(string Code, string Message, string? RequestId = null);
