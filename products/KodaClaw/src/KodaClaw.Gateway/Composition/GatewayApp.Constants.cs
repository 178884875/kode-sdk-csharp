using Microsoft.AspNetCore.StaticFiles;

public static partial class GatewayApp
{
    private const string CorrelationHeaderName = "X-KodaClaw-Correlation-Id";
    private const string GatewayCorsPolicyName = "KodaClawGatewayCors";
    private const int DefaultDiagnosticsLimit = 50;
    private const int MaxDiagnosticsLimit = 100;
    private const int DefaultInboxLimit = 50;
    private const int MaxInboxLimit = 200;
    private const int DefaultApprovalsLimit = 50;
    private const int MaxApprovalsLimit = 200;
    private const int DefaultSessionsLimit = 50;
    private const int MaxSessionsLimit = 200;
    private const int DefaultAutomationDefinitionsLimit = 50;
    private const int MaxAutomationDefinitionsLimit = 200;
    private const int DefaultAutomationRunsLimit = 50;
    private const int MaxAutomationRunsLimit = 200;
    private const int DefaultPluginsLimit = 50;
    private const int MaxPluginsLimit = 200;
    private const int DefaultPluginLogsLimit = 50;
    private const int MaxPluginLogsLimit = 200;
    private const int DefaultCanvasLimit = 50;
    private const int MaxCanvasLimit = 200;
    private const int DefaultChannelsLimit = 50;
    private const int MaxChannelsLimit = 200;
    private const string WebhookSecretHeaderName = "X-KodaClaw-Webhook-Secret";
    private const string DefaultCanvasEntryPath = "workspace/canvas/index.html";
    private static readonly FileExtensionContentTypeProvider CanvasContentTypeProvider = CreateCanvasContentTypeProvider();
}
