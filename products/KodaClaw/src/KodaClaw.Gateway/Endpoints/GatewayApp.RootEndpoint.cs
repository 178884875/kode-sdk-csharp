using Microsoft.AspNetCore.Builder;

public static partial class GatewayApp
{
    private static void MapRootEndpoint(WebApplication app)
    {
        // In Docker mode the React SPA is bundled into wwwroot/.
        // MapFallbackToFile handles "/" once we don't register a competing route here.
        var webRootIndex = Path.Combine(
            app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            "index.html");
        if (File.Exists(webRootIndex))
            return;  // Let MapFallbackToFile serve the SPA for all non-API paths.

        app.MapGet("/", () => Results.Json(new
                {
                    name = "KodaClaw Gateway",
                    endpoints = new[]
                    {
                        "GET /api/system/health",
                        "GET /api/system/bootstrap-state",
                        "GET /api/system/secret-migration-report",
                        "GET /api/system/startup-repair-report",
                        "GET /api/system/update-state",
                        "POST /api/system/update-check",
                        "POST /api/system/backup-export",
                        "POST /api/system/backup-import/preflight",
                        "POST /api/system/backup-import",
                        "GET /api/diagnostics/recent",
                        "GET /api/diagnostics/timeline",
                        "POST /api/diagnostics/bundle-export",
                        "GET /api/models",
                        "GET /api/models/{id}",
                        "POST /api/models",
                        "PUT /api/models/{id}",
                        "DELETE /api/models/{id}",
                        "POST /api/models/{id}/default",
                        "GET /api/settings",
                        "PUT /api/settings",
                        "GET /api/automations",
                        "GET /api/automations/{id}",
                        "GET /api/automations/{id}/runs",
                        "PATCH /api/automations/{id}",
                        "GET /api/canvas",
                        "GET /api/canvas/default",
                        "GET /api/canvas/{id}/entry",
                        "GET /api/canvas/{id}",
                        "POST /api/canvas",
                        "GET /api/canvas/fs/{**path}",
                        "GET /api/canvas/preview/{previewToken}/{**path}",
                        "GET /api/channels/connectors",
                        "GET /api/channels/accounts",
                        "POST /api/channels/accounts",
                        "GET /api/channels/threads",
                        "GET /api/channels/threads/{bindingId}",
                        "GET /api/channels/threads/{bindingId}/audit",
                        "POST /api/channels/webhook/{accountId}/events",
                        "GET /api/approvals",
                        "GET /api/approvals/{id}",
                        "POST /api/approvals/{id}/approve",
                        "POST /api/approvals/{id}/reject",
                        "GET /api/sessions",
                        "GET /api/sessions/{id}",
                        "GET /api/inbox",
                        "GET /api/inbox/{id}",
                        "PATCH /api/inbox/{id}/status",
                        "POST /api/chat/stream",
                        "POST /api/system/bootstrap-complete"
                    }
                }));
    }
}
