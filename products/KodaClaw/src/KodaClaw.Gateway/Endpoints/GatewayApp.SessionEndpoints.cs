using KodaClaw.Contracts;
using KodaClaw.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

public static partial class GatewayApp
{
    private static void MapSessionEndpoints(WebApplication app)
    {
        var sessions = app.MapGroup("/api/sessions");
        sessions.MapGet(string.Empty, async (
            HttpContext context,
            IConfiguration configuration,
            IWorkspaceService workspaceService,
            IDiagnosticsService diagnosticsService,
            [FromQuery] int? limit,
            CancellationToken cancellationToken) =>
        {
            if (!TryAuthorize(context, configuration))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.auth",
                    eventType: "gateway.auth.failed",
                    level: "warning",
                    message: "Unauthorized access to sessions list endpoint.");
                return Results.Unauthorized();
            }

            await workspaceService.EnsureInitializedAsync(cancellationToken);
            var appConfig = await workspaceService.LoadAppConfigAsync(cancellationToken);
            var sessions = await LoadSessionsAsync(
                workspaceService.RootPath,
                appConfig.ActiveMainSessionId,
                NormalizeSessionsLimit(limit),
                cancellationToken);

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.sessions",
                eventType: "gateway.sessions.listed",
                level: "info",
                message: $"Sessions query returned {sessions.Count} items.",
                attributes: new Dictionary<string, string?>
                {
                    ["count"] = sessions.Count.ToString(),
                    ["activeMainSessionId"] = appConfig.ActiveMainSessionId,
                });

            return Results.Ok(new SessionsQueryResponse(
                sessions
                    .Select(static session => new SessionSummary(
                        SessionId: session.SessionId,
                        SessionKind: session.SessionKind,
                        Status: session.Status,
                        CreatedAt: session.CreatedAt,
                        LastEventAt: session.LastEventAt,
                        Title: session.Title))
                    .ToArray()));
        });

        sessions.MapPost("/rotate", async (
            HttpContext context,
            IConfiguration configuration,
            IMainSessionService mainSessionService,
            IDiagnosticsService diagnosticsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryAuthorize(context, configuration))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.auth",
                    eventType: "gateway.auth.failed",
                    level: "warning",
                    message: "Unauthorized access to session rotate endpoint.");
                return Results.Unauthorized();
            }

            var previousSessionId = await mainSessionService.RotateMainSessionAsync(cancellationToken);

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.sessions",
                eventType: "gateway.sessions.rotated",
                level: "info",
                message: "Main session rotated.",
                attributes: new Dictionary<string, string?>
                {
                    ["previousSessionId"] = previousSessionId,
                });

            return Results.Ok(new RotateSessionResponse(Ok: true, PreviousSessionId: previousSessionId));
        });

        sessions.MapPost("/{id}/resume", async (
            HttpContext context,
            string id,
            IConfiguration configuration,
            IMainSessionService mainSessionService,
            IWorkspaceService workspaceService,
            IDiagnosticsService diagnosticsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryAuthorize(context, configuration))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.auth",
                    eventType: "gateway.auth.failed",
                    level: "warning",
                    message: "Unauthorized access to session resume endpoint.");
                return Results.Unauthorized();
            }

            await workspaceService.EnsureInitializedAsync(cancellationToken);
            var appConfig = await workspaceService.LoadAppConfigAsync(cancellationToken);

            var session = await LoadSessionDetailAsync(
                workspaceService.RootPath,
                id,
                appConfig.ActiveMainSessionId,
                cancellationToken);

            if (session is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.sessions",
                    eventType: "gateway.sessions.resume.not_found",
                    level: "warning",
                    message: "Requested session for resume was not found.",
                    attributes: new Dictionary<string, string?> { ["sessionId"] = id });
                return Results.NotFound(new ErrorResponse(
                    Code: "session.not_found",
                    Message: "Session was not found."));
            }

            var response = await mainSessionService.ResumeSessionAsync(id, cancellationToken);

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.sessions",
                eventType: "gateway.sessions.resumed",
                level: "info",
                message: "Main session resumed.",
                attributes: new Dictionary<string, string?> { ["resumedSessionId"] = id });

            return Results.Ok(response);
        });

        sessions.MapGet("/{id}/messages", async (
            HttpContext context,
            string id,
            IConfiguration configuration,
            IWorkspaceService workspaceService,
            IDiagnosticsService diagnosticsService,
            [FromQuery] int? limit,
            [FromQuery] int? skip,
            CancellationToken cancellationToken) =>
        {
            if (!TryAuthorize(context, configuration))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.auth",
                    eventType: "gateway.auth.failed",
                    level: "warning",
                    message: "Unauthorized access to session messages endpoint.");
                return Results.Unauthorized();
            }

            await workspaceService.EnsureInitializedAsync(cancellationToken);
            var appConfig = await workspaceService.LoadAppConfigAsync(cancellationToken);

            var session = await LoadSessionDetailAsync(
                workspaceService.RootPath,
                id,
                appConfig.ActiveMainSessionId,
                cancellationToken);

            if (session is null)
            {
                return Results.NotFound(new ErrorResponse(
                    Code: "session.not_found",
                    Message: "Session was not found."));
            }

            var pageLimit = limit.HasValue ? Math.Clamp(limit.Value, 1, 100) : 20;
            var pageSkip = skip.HasValue ? Math.Max(skip.Value, 0) : 0;
            var response = await LoadSessionMessagesAsync(
                workspaceService.RootPath,
                id,
                pageLimit,
                pageSkip,
                cancellationToken);

            return Results.Ok(response);
        });

        sessions.MapGet("/{id}", async (
            HttpContext context,
            string id,
            IConfiguration configuration,
            IWorkspaceService workspaceService,
            IDiagnosticsService diagnosticsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryAuthorize(context, configuration))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.auth",
                    eventType: "gateway.auth.failed",
                    level: "warning",
                    message: "Unauthorized access to sessions detail endpoint.");
                return Results.Unauthorized();
            }

            await workspaceService.EnsureInitializedAsync(cancellationToken);
            var appConfig = await workspaceService.LoadAppConfigAsync(cancellationToken);

            var session = await LoadSessionDetailAsync(
                workspaceService.RootPath,
                id,
                appConfig.ActiveMainSessionId,
                cancellationToken);

            if (session is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.sessions",
                    eventType: "gateway.sessions.not_found",
                    level: "warning",
                    message: "Requested session was not found.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["sessionId"] = id,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "session.not_found",
                    Message: "Session was not found."));
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.sessions",
                eventType: "gateway.sessions.fetched",
                level: "info",
                message: "Fetched session detail.",
                sessionId: session.SessionId,
                attributes: new Dictionary<string, string?>
                {
                    ["pendingApprovalCount"] = session.Status.PendingApprovalCount.ToString(),
                    ["breakpointState"] = session.Status.BreakpointState,
                });

            return Results.Ok(session);
        });

    }
}
