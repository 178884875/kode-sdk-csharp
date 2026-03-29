using System.Diagnostics;

namespace Kode.Agent.Sdk.Diagnostics;

/// <summary>
/// .NET built-in ActivitySource for Agent distributed tracing.
/// Consumers: OpenTelemetry (.AddSource("Kode.Agent")), DiagnosticListener.
/// </summary>
public static class KodeAgentActivitySource
{
    public const string SourceName = "Kode.Agent";

    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
}
