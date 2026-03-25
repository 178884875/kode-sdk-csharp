using KodaClaw.Automation;
using KodaClaw.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

public static partial class GatewayApp
{
    private static void MapAutomationEndpoints(WebApplication app)
    {
        var automations = app.MapGroup("/api/automations");
        automations.MapGet(string.Empty, async (
            HttpContext context,
            IConfiguration configuration,
            IAutomationDefinitionRepository definitionRepository,
            IDiagnosticsService diagnosticsService,
            [FromQuery] bool? enabled,
            [FromQuery] string? source,
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
                    message: "Unauthorized access to automations list endpoint.");
                return Results.Unauthorized();
            }

            if (!TryParseEnum(source, out AutomationDefinitionSource? parsedSource))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.automations",
                    eventType: "gateway.automations.invalid_request",
                    level: "warning",
                    message: "Automations list received an invalid source filter.");
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.automation_source_invalid",
                    Message: "Automation source filter is invalid."));
            }

            var items = await definitionRepository.ListAsync(
                new AutomationDefinitionQuery(
                    Enabled: enabled,
                    Source: parsedSource,
                    Limit: NormalizeAutomationDefinitionsLimit(limit)),
                cancellationToken);

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.automations",
                eventType: "gateway.automations.listed",
                level: "info",
                message: $"Automations query returned {items.Count} items.",
                attributes: new Dictionary<string, string?>
                {
                    ["enabled"] = enabled?.ToString(),
                    ["source"] = parsedSource?.ToString(),
                    ["count"] = items.Count.ToString(),
                });

            // Recompute NextRunAt for display: the stored value may reflect an older timezone
            // assumption or a scheduler-internal override (claim lock / failure-retry delay).
            // The frontend always wants "next time this cron fires" in local time.
            var now = DateTimeOffset.UtcNow;
            var displayItems = items
                .Select(d => d with
                {
                    NextRunAt = AutomationCronComputer.ComputeNextRunAt(
                        d.CronExpression,
                        d.LastRunAt ?? now),
                })
                .ToArray();

            return Results.Ok(new AutomationDefinitionsQueryResponse(displayItems));
        });

        automations.MapGet("/{id}", async (
            HttpContext context,
            string id,
            IConfiguration configuration,
            IAutomationDefinitionRepository definitionRepository,
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
                    message: "Unauthorized access to automation detail endpoint.");
                return Results.Unauthorized();
            }

            var definition = await definitionRepository.GetByIdAsync(id, cancellationToken);
            if (definition is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.automations",
                    eventType: "gateway.automations.not_found",
                    level: "warning",
                    message: "Requested automation was not found.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["automationId"] = id,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "automation.not_found",
                    Message: "Automation was not found."));
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.automations",
                eventType: "gateway.automations.fetched",
                level: "info",
                message: "Fetched automation detail.",
                attributes: new Dictionary<string, string?>
                {
                    ["automationId"] = definition.Id,
                    ["enabled"] = definition.Enabled.ToString(),
                    ["source"] = definition.Source.ToString(),
                });

            var displayDefinition = definition with
            {
                NextRunAt = AutomationCronComputer.ComputeNextRunAt(
                    definition.CronExpression,
                    definition.LastRunAt ?? DateTimeOffset.UtcNow),
            };

            return Results.Ok(displayDefinition);
        });

        automations.MapGet("/{id}/runs", async (
            HttpContext context,
            string id,
            IConfiguration configuration,
            IAutomationDefinitionRepository definitionRepository,
            IAutomationRunRepository runRepository,
            IDiagnosticsService diagnosticsService,
            [FromQuery] int? limit,
            [FromQuery] string? status,
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
                    message: "Unauthorized access to automation runs endpoint.");
                return Results.Unauthorized();
            }

            if (!TryParseEnum(status, out AutomationRunStatus? parsedStatus))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.automations",
                    eventType: "gateway.automations.invalid_request",
                    level: "warning",
                    message: "Automation runs query received an invalid status filter.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["automationId"] = id,
                    });
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.automation_run_status_invalid",
                    Message: "Automation run status filter is invalid."));
            }

            var definition = await definitionRepository.GetByIdAsync(id, cancellationToken);
            if (definition is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.automations",
                    eventType: "gateway.automations.not_found",
                    level: "warning",
                    message: "Automation runs query targeted a missing automation.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["automationId"] = id,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "automation.not_found",
                    Message: "Automation was not found."));
            }

            var items = await runRepository.ListAsync(
                new AutomationRunQuery(
                    AutomationId: id,
                    Status: parsedStatus,
                    Limit: NormalizeAutomationRunsLimit(limit)),
                cancellationToken);

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.automations",
                eventType: "gateway.automations.runs_listed",
                level: "info",
                message: $"Automation runs query returned {items.Count} items.",
                attributes: new Dictionary<string, string?>
                {
                    ["automationId"] = id,
                    ["status"] = parsedStatus?.ToString(),
                    ["count"] = items.Count.ToString(),
                });

            return Results.Ok(new AutomationRunsQueryResponse(items));
        });

        automations.MapPatch("/{id}", async (
            HttpContext context,
            string id,
            UpdateAutomationDefinitionRequest request,
            IConfiguration configuration,
            IAutomationDefinitionRepository definitionRepository,
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
                    message: "Unauthorized access to automation patch endpoint.");
                return Results.Unauthorized();
            }

            var existing = await definitionRepository.GetByIdAsync(id, cancellationToken);
            if (existing is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.automations",
                    eventType: "gateway.automations.not_found",
                    level: "warning",
                    message: "Automation patch targeted a missing automation.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["automationId"] = id,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "automation.not_found",
                    Message: "Automation was not found."));
            }

            var updated = existing with
            {
                Enabled = request.Enabled,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await definitionRepository.UpsertAsync(updated, cancellationToken);

            var reloaded = await definitionRepository.GetByIdAsync(id, cancellationToken) ?? updated;
            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.automations",
                eventType: "gateway.automations.updated",
                level: "info",
                message: "Updated automation enabled state.",
                attributes: new Dictionary<string, string?>
                {
                    ["automationId"] = reloaded.Id,
                    ["enabled"] = reloaded.Enabled.ToString(),
                });

            return Results.Ok(reloaded);
        });

        automations.MapPost("/{id}/trigger", async (
            HttpContext context,
            string id,
            IConfiguration configuration,
            IAutomationDefinitionRepository definitionRepository,
            IAutomationScheduler automationScheduler,
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
                    message: "Unauthorized access to automation trigger endpoint.");
                return Results.Unauthorized();
            }

            var definition = await definitionRepository.GetByIdAsync(id, cancellationToken);
            if (definition is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.automations",
                    eventType: "gateway.automations.not_found",
                    level: "warning",
                    message: "Automation trigger targeted a missing automation.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["automationId"] = id,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "automation.not_found",
                    Message: "Automation was not found."));
            }

            if (!definition.Enabled)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.automations",
                    eventType: "gateway.automations.trigger_disabled",
                    level: "warning",
                    message: "Cannot trigger a disabled automation.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["automationId"] = id,
                    });
                return Results.BadRequest(new ErrorResponse(
                    Code: "automation.disabled",
                    Message: "Automation is disabled and cannot be triggered manually."));
            }

            // TriggerDefinitionAsync synchronously creates the run record and returns its ID,
            // then starts agent execution in the background. This avoids the race condition where
            // RecoverStaleRunsAsync would immediately mark a freshly-created Queued run as Failed.
            var runId = await automationScheduler.TriggerDefinitionAsync(id, cancellationToken);
            if (runId is null)
            {
                return Results.NotFound(new ErrorResponse(
                    Code: "automation.not_found",
                    Message: "Automation was not found."));
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.automations",
                eventType: "gateway.automations.triggered",
                level: "info",
                message: "Automation triggered manually.",
                attributes: new Dictionary<string, string?>
                {
                    ["automationId"] = id,
                    ["runId"] = runId,
                });

            return Results.Ok(new TriggerAutomationResponse(Ok: true, RunId: runId));
        });

    }
}
