using KodaClaw.Contracts;
using KodaClaw.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

public static partial class GatewayApp
{
    private static void MapModelEndpoints(WebApplication app)
    {
        var presets = app.MapGroup("/api/models/presets");

        presets.MapGet(string.Empty, (
            HttpContext context,
            IConfiguration configuration,
            ModelPresetService modelPresetService,
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
                    message: "Unauthorized access to model presets endpoint.");
                return Results.Unauthorized();
            }

            var allPresets = modelPresetService.GetAll();
            return Results.Ok(allPresets);
        });

        presets.MapGet("/{presetId}", (
            HttpContext context,
            string presetId,
            IConfiguration configuration,
            ModelPresetService modelPresetService,
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
                    message: "Unauthorized access to model preset detail endpoint.");
                return Results.Unauthorized();
            }

            var preset = modelPresetService.GetById(presetId);
            if (preset is null)
                return Results.NotFound(new ErrorResponse(
                    Code: "model_preset.not_found",
                    Message: "Model preset was not found."));

            return Results.Ok(preset);
        });

        var models = app.MapGroup("/api/models");

        models.MapPost("/test-connection", async (
            HttpContext context,
            ModelConnectionTestRequest request,
            IConfiguration configuration,
            ModelConnectionTestService modelConnectionTestService,
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
                    message: "Unauthorized access to model test-connection endpoint.");
                return Results.Unauthorized();
            }

            var result = await modelConnectionTestService.TestAsync(request, cancellationToken);
            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.models",
                eventType: "gateway.models.connection_tested",
                level: result.Ok ? "info" : "warning",
                message: result.Ok ? "Model connection test succeeded." : $"Model connection test failed: {result.Error}",
                attributes: new Dictionary<string, string?>
                {
                    ["ok"] = result.Ok.ToString(),
                    ["latencyMs"] = result.LatencyMs.ToString(),
                    ["modelId"] = result.ModelId,
                    ["error"] = result.Error,
                });

            return Results.Ok(result);
        });


        var modelsGroup = app.MapGroup("/api/models");

        modelsGroup.MapGet(string.Empty, async (
            HttpContext context,
            IConfiguration configuration,
            IModelRegistryRepository modelRegistryRepository,
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
                    message: "Unauthorized access to models list endpoint.");
                return Results.Unauthorized();
            }

            var endpoints = await modelRegistryRepository.ListAsync(cancellationToken);
            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.models",
                eventType: "gateway.models.listed",
                level: "info",
                message: $"Models query returned {endpoints.Count} items.",
                attributes: new Dictionary<string, string?>
                {
                    ["count"] = endpoints.Count.ToString(),
                });

            return Results.Ok(new ModelsQueryResponse(endpoints));
        });

        modelsGroup.MapGet("/{id}", async (
            HttpContext context,
            string id,
            IConfiguration configuration,
            IModelRegistryRepository modelRegistryRepository,
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
                    message: "Unauthorized access to model detail endpoint.");
                return Results.Unauthorized();
            }

            var endpoint = await modelRegistryRepository.GetByIdAsync(id, cancellationToken);
            if (endpoint is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.models",
                    eventType: "gateway.models.not_found",
                    level: "warning",
                    message: "Requested model endpoint was not found.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["modelEndpointId"] = id,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "model_endpoint.not_found",
                    Message: "Model endpoint was not found."));
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.models",
                eventType: "gateway.models.fetched",
                level: "info",
                message: "Fetched model endpoint detail.",
                attributes: new Dictionary<string, string?>
                {
                    ["modelEndpointId"] = endpoint.Id,
                    ["provider"] = endpoint.Provider.ToString(),
                    ["isDefault"] = endpoint.IsDefault.ToString(),
                });

            return Results.Ok(endpoint);
        });

        modelsGroup.MapPost(string.Empty, async (
            HttpContext context,
            CreateModelEndpointRequest request,
            IConfiguration configuration,
            IModelRegistryRepository modelRegistryRepository,
            ISecretStore secretStore,
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
                    message: "Unauthorized access to model create endpoint.");
                return Results.Unauthorized();
            }

            if (!TryValidateModelEndpointRequest(request, out var validatedRequest, out var error))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.models",
                    eventType: "gateway.models.invalid_request",
                    level: "warning",
                    message: error!.Message);
                return Results.BadRequest(error);
            }

            var now = DateTimeOffset.UtcNow;
            var endpointId = $"model-{Guid.NewGuid():N}";

            var resolvedSecretRef = validatedRequest.ApiKeySecretRef;
            if (!string.IsNullOrWhiteSpace(request.ApiKeyValue))
            {
                var secretRef = new SecretRef("keychain", "models", endpointId);
                await secretStore.UpsertAsync(secretRef, request.ApiKeyValue, cancellationToken);
                resolvedSecretRef = secretRef.ToReferenceString();
            }

            var existingEndpoints = await modelRegistryRepository.ListAsync(cancellationToken);
            var shouldBeDefault = existingEndpoints.Count == 0;

            var endpoint = new ModelEndpoint(
                Id: endpointId,
                DisplayName: validatedRequest.DisplayName,
                Provider: validatedRequest.Provider,
                ModelId: validatedRequest.ModelId,
                BaseUrl: validatedRequest.BaseUrl,
                ApiKeyEnvironmentVariable: validatedRequest.ApiKeyEnvironmentVariable,
                ApiKeySecretRef: resolvedSecretRef,
                Enabled: validatedRequest.Enabled,
                Capabilities: validatedRequest.Capabilities,
                IsDefault: shouldBeDefault,
                CreatedAt: now,
                UpdatedAt: now,
                ContextWindowSize: validatedRequest.ContextWindowSize,
                MaxOutputTokens: validatedRequest.MaxOutputTokens,
                IsReasoning: validatedRequest.IsReasoning);

            await modelRegistryRepository.AddAsync(endpoint, cancellationToken);
            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.models",
                eventType: "gateway.models.created",
                level: "info",
                message: "Created model endpoint.",
                attributes: new Dictionary<string, string?>
                {
                    ["modelEndpointId"] = endpoint.Id,
                    ["provider"] = endpoint.Provider.ToString(),
                });

            return Results.Created($"/api/models/{endpoint.Id}", endpoint);
        });

        modelsGroup.MapPut("/{id}", async (
            HttpContext context,
            string id,
            UpdateModelEndpointRequest request,
            IConfiguration configuration,
            IModelRegistryRepository modelRegistryRepository,
            ISecretStore secretStore,
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
                    message: "Unauthorized access to model update endpoint.");
                return Results.Unauthorized();
            }

            var existing = await modelRegistryRepository.GetByIdAsync(id, cancellationToken);
            if (existing is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.models",
                    eventType: "gateway.models.not_found",
                    level: "warning",
                    message: "Model update targeted a missing endpoint.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["modelEndpointId"] = id,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "model_endpoint.not_found",
                    Message: "Model endpoint was not found."));
            }

            if (!TryValidateModelEndpointRequest(request, out var validatedRequest, out var error))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.models",
                    eventType: "gateway.models.invalid_request",
                    level: "warning",
                    message: error!.Message,
                    attributes: new Dictionary<string, string?>
                    {
                        ["modelEndpointId"] = id,
                    });
                return Results.BadRequest(error);
            }

            if (existing.IsDefault && !validatedRequest.Enabled)
            {
                return Results.Conflict(new ErrorResponse(
                    Code: "model_endpoint.default_must_be_enabled",
                    Message: "The default model endpoint must remain enabled."));
            }

            var resolvedSecretRef = validatedRequest.ApiKeySecretRef;
            if (!string.IsNullOrWhiteSpace(request.ApiKeyValue))
            {
                var secretRef = new SecretRef("keychain", "models", id);
                await secretStore.UpsertAsync(secretRef, request.ApiKeyValue, cancellationToken);
                resolvedSecretRef = secretRef.ToReferenceString();
            }
            else if (string.IsNullOrWhiteSpace(validatedRequest.ApiKeySecretRef))
            {
                // preserve existing secret ref if client didn't send a new key or explicit ref
                resolvedSecretRef = existing.ApiKeySecretRef;
            }

            var updated = existing with
            {
                DisplayName = validatedRequest.DisplayName,
                Provider = validatedRequest.Provider,
                ModelId = validatedRequest.ModelId,
                BaseUrl = validatedRequest.BaseUrl,
                ApiKeyEnvironmentVariable = validatedRequest.ApiKeyEnvironmentVariable,
                ApiKeySecretRef = resolvedSecretRef,
                Enabled = validatedRequest.Enabled,
                Capabilities = validatedRequest.Capabilities,
                ContextWindowSize = validatedRequest.ContextWindowSize,
                MaxOutputTokens = validatedRequest.MaxOutputTokens,
                IsReasoning = validatedRequest.IsReasoning,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            var persisted = await modelRegistryRepository.UpdateAsync(updated, cancellationToken);
            if (!persisted)
            {
                return Results.NotFound(new ErrorResponse(
                    Code: "model_endpoint.not_found",
                    Message: "Model endpoint was not found."));
            }

            var reloaded = await modelRegistryRepository.GetByIdAsync(id, cancellationToken) ?? updated;
            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.models",
                eventType: "gateway.models.updated",
                level: "info",
                message: "Updated model endpoint.",
                attributes: new Dictionary<string, string?>
                {
                    ["modelEndpointId"] = reloaded.Id,
                    ["provider"] = reloaded.Provider.ToString(),
                    ["isDefault"] = reloaded.IsDefault.ToString(),
                });

            return Results.Ok(reloaded);
        });

        modelsGroup.MapDelete("/{id}", async (
            HttpContext context,
            string id,
            IConfiguration configuration,
            IModelRegistryRepository modelRegistryRepository,
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
                    message: "Unauthorized access to model delete endpoint.");
                return Results.Unauthorized();
            }

            var toDelete = await modelRegistryRepository.GetByIdAsync(id, cancellationToken);
            if (toDelete is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.models",
                    eventType: "gateway.models.not_found",
                    level: "warning",
                    message: "Model delete targeted a missing endpoint.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["modelEndpointId"] = id,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "model_endpoint.not_found",
                    Message: "Model endpoint was not found."));
            }

            if (toDelete.IsDefault)
            {
                var allEndpoints = await modelRegistryRepository.ListAsync(cancellationToken);
                var nextDefault = allEndpoints.FirstOrDefault(e => e.Id != id && e.Enabled);
                if (nextDefault is not null)
                {
                    await modelRegistryRepository.SetDefaultAsync(nextDefault.Id, DateTimeOffset.UtcNow, cancellationToken);
                }
                // If no other endpoint exists, allow deleting the last model without reassigning default.
            }

            var deleted = await modelRegistryRepository.DeleteAsync(id, cancellationToken);
            if (!deleted)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.models",
                    eventType: "gateway.models.not_found",
                    level: "warning",
                    message: "Model delete targeted a missing endpoint.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["modelEndpointId"] = id,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "model_endpoint.not_found",
                    Message: "Model endpoint was not found."));
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.models",
                eventType: "gateway.models.deleted",
                level: "info",
                message: "Deleted model endpoint.",
                attributes: new Dictionary<string, string?>
                {
                    ["modelEndpointId"] = id,
                });

            return Results.NoContent();
        });

        modelsGroup.MapPost("/{id}/default", async (
            HttpContext context,
            string id,
            IConfiguration configuration,
            IModelRegistryRepository modelRegistryRepository,
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
                    message: "Unauthorized access to model default endpoint.");
                return Results.Unauthorized();
            }

            var endpoint = await modelRegistryRepository.GetByIdAsync(id, cancellationToken);
            if (endpoint is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.models",
                    eventType: "gateway.models.not_found",
                    level: "warning",
                    message: "Model default update targeted a missing endpoint.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["modelEndpointId"] = id,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "model_endpoint.not_found",
                    Message: "Model endpoint was not found."));
            }

            if (!endpoint.Enabled)
            {
                return Results.Conflict(new ErrorResponse(
                    Code: "model_endpoint.default_must_be_enabled",
                    Message: "Only enabled model endpoints can become default."));
            }

            var updated = await modelRegistryRepository.SetDefaultAsync(id, DateTimeOffset.UtcNow, cancellationToken);
            if (!updated)
            {
                return Results.NotFound(new ErrorResponse(
                    Code: "model_endpoint.not_found",
                    Message: "Model endpoint was not found."));
            }

            var reloaded = await modelRegistryRepository.GetByIdAsync(id, cancellationToken);
            if (reloaded is null)
            {
                return Results.NotFound(new ErrorResponse(
                    Code: "model_endpoint.not_found",
                    Message: "Model endpoint was not found."));
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.models",
                eventType: "gateway.models.default_set",
                level: "info",
                message: "Updated default model endpoint.",
                attributes: new Dictionary<string, string?>
                {
                    ["modelEndpointId"] = reloaded.Id,
                    ["provider"] = reloaded.Provider.ToString(),
                });

            return Results.Ok(reloaded);
        });
    }
}
