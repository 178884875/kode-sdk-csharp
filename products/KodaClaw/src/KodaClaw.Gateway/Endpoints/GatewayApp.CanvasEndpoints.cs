using KodaClaw.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

public static partial class GatewayApp
{
    private static void MapCanvasEndpoints(WebApplication app)
    {
        var canvas = app.MapGroup("/api/canvas");
        canvas.MapGet(string.Empty, async (
            HttpContext context,
            IConfiguration configuration,
            ICanvasArtifactRepository canvasRepository,
            IDiagnosticsService diagnosticsService,
            [FromQuery] string? kind,
            [FromQuery] string? source,
            [FromQuery] string? sessionId,
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
                    message: "Unauthorized access to canvas list endpoint.");
                return Results.Unauthorized();
            }

            if (!TryParseEnum(kind, out CanvasArtifactKind? parsedKind))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.canvas",
                    eventType: "gateway.canvas.invalid_request",
                    level: "warning",
                    message: "Canvas list received an invalid kind filter.");
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.canvas_kind_invalid",
                    Message: "Canvas kind filter is invalid."));
            }

            var items = await canvasRepository.ListAsync(
                new CanvasArtifactQuery(
                    Kind: parsedKind,
                    Source: source,
                    SessionId: sessionId,
                    Limit: NormalizeCanvasLimit(limit)),
                cancellationToken);

            var defaultArtifact = items
                .OrderByDescending(static artifact => artifact.UpdatedAt)
                .ThenByDescending(static artifact => artifact.CreatedAt)
                .FirstOrDefault();

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.canvas",
                eventType: "gateway.canvas.listed",
                level: "info",
                message: $"Canvas query returned {items.Count} items.",
                attributes: new Dictionary<string, string?>
                {
                    ["kind"] = parsedKind?.ToString(),
                    ["source"] = source,
                    ["sessionId"] = sessionId,
                    ["count"] = items.Count.ToString(),
                });

            return Results.Ok(new CanvasQueryResponse(
                Items: items,
                DefaultEntryPath: defaultArtifact?.EntryPath ?? DefaultCanvasEntryPath,
                DefaultArtifactId: defaultArtifact?.Id));
        });

        canvas.MapGet("/default", async (
            HttpContext context,
            IConfiguration configuration,
            ICanvasArtifactRepository canvasRepository,
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
                    message: "Unauthorized access to canvas default endpoint.");
                return Results.Unauthorized();
            }

            var items = await canvasRepository.ListAsync(
                new CanvasArtifactQuery(Limit: 1),
                cancellationToken);
            var artifact = items.FirstOrDefault();

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.canvas",
                eventType: "gateway.canvas.default_fetched",
                level: "info",
                message: artifact is null
                    ? "Canvas default entry resolved to workspace fallback."
                    : "Canvas default entry resolved to latest artifact.",
                attributes: new Dictionary<string, string?>
                {
                    ["artifactId"] = artifact?.Id,
                });

            var entryPath = artifact?.EntryPath ?? DefaultCanvasEntryPath;
            return Results.Ok(new CanvasEntryResponse(
                EntryUrl: BuildCanvasEntryUrl(entryPath),
                EntryPath: entryPath,
                ArtifactId: artifact?.Id,
                Route: artifact?.Route,
                Title: artifact?.Title));
        });

        canvas.MapGet("/{id}", async (
            HttpContext context,
            string id,
            IConfiguration configuration,
            ICanvasArtifactRepository canvasRepository,
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
                    message: "Unauthorized access to canvas detail endpoint.");
                return Results.Unauthorized();
            }

            var artifact = await canvasRepository.GetByIdAsync(id, cancellationToken);
            if (artifact is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.canvas",
                    eventType: "gateway.canvas.not_found",
                    level: "warning",
                    message: "Requested canvas artifact was not found.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["artifactId"] = id,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "canvas.not_found",
                    Message: "Canvas artifact was not found."));
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.canvas",
                eventType: "gateway.canvas.fetched",
                level: "info",
                message: "Fetched canvas artifact detail.",
                attributes: new Dictionary<string, string?>
                {
                    ["artifactId"] = artifact.Id,
                    ["kind"] = artifact.Kind.ToString(),
                });

            return Results.Ok(artifact);
        });

        canvas.MapPost(string.Empty, async (
            HttpContext context,
            UpsertCanvasArtifactRequest request,
            IConfiguration configuration,
            ICanvasArtifactRepository canvasRepository,
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
                    message: "Unauthorized access to canvas upsert endpoint.");
                return Results.Unauthorized();
            }

            var existing = await canvasRepository.GetByIdAsync(request.Id, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var artifact = new CanvasArtifact(
                Id: request.Id,
                Title: request.Title,
                Kind: request.Kind,
                Summary: request.Summary,
                Source: request.Source,
                EntryPath: request.EntryPath,
                AssetDirectory: request.AssetDirectory,
                CreatedAt: existing?.CreatedAt ?? now,
                UpdatedAt: now,
                Route: request.Route,
                SessionId: request.SessionId,
                CorrelationId: request.CorrelationId,
                MetadataJson: request.MetadataJson);

            try
            {
                await canvasRepository.UpsertAsync(artifact, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.canvas",
                    eventType: "gateway.canvas.invalid_request",
                    level: "warning",
                    message: ex.Message,
                    attributes: new Dictionary<string, string?>
                    {
                        ["artifactId"] = request.Id,
                    });
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.canvas_invalid",
                    Message: ex.Message));
            }

            var reloaded = await canvasRepository.GetByIdAsync(request.Id, cancellationToken) ?? artifact;
            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.canvas",
                eventType: existing is null ? "gateway.canvas.created" : "gateway.canvas.updated",
                level: "info",
                message: existing is null ? "Created canvas artifact." : "Updated canvas artifact.",
                attributes: new Dictionary<string, string?>
                {
                    ["artifactId"] = reloaded.Id,
                    ["kind"] = reloaded.Kind.ToString(),
                });

            return existing is null
                ? Results.Created($"/api/canvas/{reloaded.Id}", reloaded)
                : Results.Ok(reloaded);
        });

        canvas.MapGet("/fs", async (
            HttpContext context,
            IConfiguration configuration,
            IWorkspaceService workspaceService,
            IDiagnosticsService diagnosticsService,
            CancellationToken cancellationToken) =>
        {
            return await ServeCanvasFileAsync(
                context,
                configuration,
                workspaceService,
                diagnosticsService,
                path: null,
                cancellationToken);
        });

        canvas.MapGet("/fs/{**path}", async (
            HttpContext context,
            string? path,
            IConfiguration configuration,
            IWorkspaceService workspaceService,
            IDiagnosticsService diagnosticsService,
            CancellationToken cancellationToken) =>
        {
            return await ServeCanvasFileAsync(
                context,
                configuration,
                workspaceService,
                diagnosticsService,
                path,
                cancellationToken);
        });

    }
}
