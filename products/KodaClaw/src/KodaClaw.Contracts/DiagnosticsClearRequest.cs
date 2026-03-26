namespace KodaClaw.Contracts;

public sealed record DiagnosticsClearRequest(DateTimeOffset? Before = null);
