using KodaClaw.Contracts;
using KodaClaw.Gateway;
using KodaClaw.Workspace;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

public static partial class GatewayApp
{
    private static void MapWorkspaceEndpoints(WebApplication app)
    {
        var workspace = app.MapGroup("/api/workspace");

        workspace.MapGet("/persona-presets", (
            HttpContext context,
            IConfiguration configuration,
            PersonaPresetService personaPresetService,
            IDiagnosticsService diagnosticsService) =>
        {
            if (!TryAuthorize(context, configuration))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.auth",
                    eventType: "gateway.auth.failed",
                    level: "warning",
                    message: "Unauthorized access to persona presets endpoint.");
                return Results.Unauthorized();
            }

            var allPresets = personaPresetService.GetAll();
            return Results.Ok(allPresets);
        });

        workspace.MapGet("/persona-presets/{presetId}", (
            HttpContext context,
            string presetId,
            IConfiguration configuration,
            PersonaPresetService personaPresetService,
            IDiagnosticsService diagnosticsService) =>
        {
            if (!TryAuthorize(context, configuration))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.auth",
                    eventType: "gateway.auth.failed",
                    level: "warning",
                    message: "Unauthorized access to persona preset detail endpoint.");
                return Results.Unauthorized();
            }

            var preset = personaPresetService.GetById(presetId);
            if (preset is null)
                return Results.NotFound(new ErrorResponse(
                    Code: "persona_preset.not_found",
                    Message: "Persona preset was not found."));

            return Results.Ok(preset);
        });

        var onboarding = app.MapGroup("/api/onboarding");

        onboarding.MapGet("/state", async (
            HttpContext context,
            IConfiguration configuration,
            OnboardingStateService onboardingStateService,
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
                    message: "Unauthorized access to onboarding state endpoint.");
                return Results.Unauthorized();
            }

            var state = await onboardingStateService.GetStateAsync(cancellationToken);
            return Results.Ok(state);
        });

        onboarding.MapPut("/state", async (
            HttpContext context,
            OnboardingState body,
            IConfiguration configuration,
            OnboardingStateService onboardingStateService,
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
                    message: "Unauthorized access to onboarding state update endpoint.");
                return Results.Unauthorized();
            }

            await onboardingStateService.SaveStateAsync(body, cancellationToken);
            return Results.Ok(body);
        });

        onboarding.MapPost("/complete", async (
            HttpContext context,
            IConfiguration configuration,
            OnboardingStateService onboardingStateService,
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
                    message: "Unauthorized access to onboarding complete endpoint.");
                return Results.Unauthorized();
            }

            var state = await onboardingStateService.CompleteAsync(cancellationToken);
            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.onboarding",
                eventType: "gateway.onboarding.completed",
                level: "info",
                message: "Onboarding marked as completed.");
            return Results.Ok(state);
        });

        onboarding.MapPost("/reset", async (
            HttpContext context,
            IConfiguration configuration,
            OnboardingStateService onboardingStateService,
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
                    message: "Unauthorized access to onboarding reset endpoint.");
                return Results.Unauthorized();
            }

            var state = await onboardingStateService.ResetAsync(cancellationToken);
            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.onboarding",
                eventType: "gateway.onboarding.reset",
                level: "info",
                message: "Onboarding state reset.");
            return Results.Ok(state);
        });

        onboarding.MapPost("/apply-persona", async (
            HttpContext context,
            ApplyPersonaRequest body,
            IConfiguration configuration,
            PersonaPresetService personaPresetService,
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
                    message: "Unauthorized access to onboarding apply-persona endpoint.");
                return Results.Unauthorized();
            }

            var preset = personaPresetService.GetById(body.PresetId);
            if (preset is null)
                return Results.NotFound(new ErrorResponse(
                    Code: "persona_preset.not_found",
                    Message: "Persona preset was not found."));

            // Write SOUL.md and IDENTITY.md into workspace
            var soulPath = Path.Combine(workspaceService.RootPath,
                KodaClawWorkspaceLayout.WorkspaceDirectory,
                KodaClawWorkspaceLayout.SoulFile);
            var identityPath = Path.Combine(workspaceService.RootPath,
                KodaClawWorkspaceLayout.WorkspaceDirectory,
                KodaClawWorkspaceLayout.IdentityFile);

            await File.WriteAllTextAsync(soulPath, preset.SoulMarkdown, cancellationToken);
            await File.WriteAllTextAsync(identityPath, preset.IdentityMarkdown, cancellationToken);

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.onboarding",
                eventType: "gateway.onboarding.persona_applied",
                level: "info",
                message: "Persona preset applied to workspace.",
                attributes: new Dictionary<string, string?>
                {
                    ["presetId"] = body.PresetId,
                    ["displayName"] = preset.DisplayName,
                });

            return Results.Ok(new { ok = true });
        });
    }
}
