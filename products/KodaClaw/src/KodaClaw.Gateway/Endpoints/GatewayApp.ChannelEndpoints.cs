using System.IO;
using KodaClaw.ChannelHub;
using KodaClaw.ChannelHub.Connectors.Webhook;
using KodaClaw.Contracts;
using KodaClaw.Gateway.Channels;
using KodaClaw.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

public static partial class GatewayApp
{
    private static void MapChannelEndpoints(WebApplication app)
    {
        var channels = app.MapGroup("/api/channels");
        channels.MapGet("/connectors", (
            HttpContext context,
            IConfiguration configuration,
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
                    message: "Unauthorized access to channel connectors endpoint.");
                return Results.Unauthorized();
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.channels",
                eventType: "gateway.channels.connectors_listed",
                level: "info",
                message: "Listed available channel connectors.");

            return Results.Ok(new[]
            {
                new
                {
                    Kind = ChannelConnectorKind.GenericWebhook,
                    DisplayName = "Generic Webhook",
                    Implemented = true,
                    SupportsInbound = true,
                    SupportsOutbound = false,
                    ProductOwned = true,
                },
                new
                {
                    Kind = ChannelConnectorKind.Telegram,
                    DisplayName = "Telegram",
                    Implemented = true,
                    SupportsInbound = true,
                    SupportsOutbound = true,
                    ProductOwned = true,
                },
            });
        });

        channels.MapGet("/accounts", async (
            HttpContext context,
            string? connectorKind,
            string? state,
            int? limit,
            IConfiguration configuration,
            IChannelAccountRepository channelAccountRepository,
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
                    message: "Unauthorized access to channel accounts endpoint.");
                return Results.Unauthorized();
            }

            if (!TryParseEnum(connectorKind, out ChannelConnectorKind? parsedConnectorKind))
            {
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.channel_connector_kind_invalid",
                    Message: "Channel connector kind is invalid."));
            }

            if (!TryParseEnum(state, out ChannelAccountState? parsedState))
            {
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.channel_account_state_invalid",
                    Message: "Channel account state is invalid."));
            }

            var items = await channelAccountRepository.ListAsync(
                new ChannelAccountQuery(
                    ConnectorKind: parsedConnectorKind,
                    State: parsedState,
                    Limit: NormalizeChannelsLimit(limit)),
                cancellationToken);

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.channels",
                eventType: "gateway.channels.accounts_listed",
                level: "info",
                message: $"Channel accounts query returned {items.Count} items.",
                attributes: new Dictionary<string, string?>
                {
                    ["count"] = items.Count.ToString(),
                });

            return Results.Ok(items);
        });

        channels.MapPost("/accounts", async (
            HttpContext context,
            UpsertChannelAccountRequest request,
            IConfiguration configuration,
            IChannelAccountRepository channelAccountRepository,
            ChannelInboundGatewayService channelInboundGatewayService,
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
                    message: "Unauthorized access to upsert channel account endpoint.");
                return Results.Unauthorized();
            }

            if (!TryValidateChannelAccountRequest(request, out var validationError))
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.channels",
                    eventType: "gateway.channels.invalid_request",
                    level: "warning",
                    message: validationError!.Message,
                    attributes: new Dictionary<string, string?>
                    {
                        ["accountId"] = request.Id,
                    });
                return Results.BadRequest(validationError);
            }

            var existing = await channelAccountRepository.GetByIdAsync(request.Id, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var state = ResolveChannelAccountState(request, existing);
            var account = new ChannelAccount(
                Id: request.Id.Trim(),
                ConnectorKind: request.ConnectorKind,
                DisplayName: request.DisplayName.Trim(),
                State: state,
                CreatedAt: existing?.CreatedAt ?? now,
                UpdatedAt: now,
                ExternalAccountId: NormalizeOptionalString(request.ExternalAccountId),
                CredentialReference: NormalizeOptionalString(request.CredentialReference),
                Description: NormalizeOptionalString(request.Description),
                ConfigurationJson: NormalizeOptionalJson(request.ConfigurationJson),
                InboundEnabled: request.InboundEnabled,
                LastConnectedAt: state == ChannelAccountState.Connected
                    ? existing?.LastConnectedAt ?? now
                    : existing?.LastConnectedAt,
                LastDisconnectedAt: state == ChannelAccountState.Disconnected
                    ? now
                    : existing?.LastDisconnectedAt,
                LastError: existing?.LastError);

            account = await ReconcileChannelAccountRuntimeAsync(
                account,
                existing,
                channelAccountRepository,
                channelInboundGatewayService,
                cancellationToken);

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.channels",
                eventType: existing is null
                    ? "gateway.channels.account_created"
                    : "gateway.channels.account_updated",
                level: "info",
                message: existing is null
                    ? "Created channel account."
                    : "Updated channel account.",
                attributes: new Dictionary<string, string?>
                {
                    ["accountId"] = account.Id,
                    ["connectorKind"] = account.ConnectorKind.ToString(),
                    ["state"] = account.State.ToString(),
                });

            return existing is null
                ? Results.Created($"/api/channels/accounts/{account.Id}", account)
                : Results.Ok(account);
        });

        channels.MapGet("/threads", async (
            HttpContext context,
            string? connectorKind,
            string? accountId,
            string? threadType,
            string? sessionKind,
            string? sessionId,
            int? limit,
            IConfiguration configuration,
            IThreadBindingRepository threadBindingRepository,
            IChannelAccountRepository channelAccountRepository,
            ChannelAuditQueryService channelAuditQueryService,
            IApprovalRepository approvalRepository,
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
                    message: "Unauthorized access to channel threads endpoint.");
                return Results.Unauthorized();
            }

            if (!TryParseEnum(connectorKind, out ChannelConnectorKind? parsedConnectorKind))
            {
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.channel_connector_kind_invalid",
                    Message: "Channel connector kind is invalid."));
            }

            if (!TryParseEnum(threadType, out ChannelThreadType? parsedThreadType))
            {
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.channel_thread_type_invalid",
                    Message: "Channel thread type is invalid."));
            }

            if (!TryParseEnum(sessionKind, out SessionKind? parsedSessionKind))
            {
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.channel_session_kind_invalid",
                    Message: "Channel session kind is invalid."));
            }

            var bindings = await threadBindingRepository.ListAsync(
                new ChannelQuery(
                    ConnectorKind: parsedConnectorKind,
                    AccountId: accountId,
                    ThreadType: parsedThreadType,
                    SessionKind: parsedSessionKind,
                    SessionId: sessionId,
                    Limit: NormalizeChannelsLimit(limit)),
                cancellationToken);

            var items = new List<ChannelThreadSummary>(bindings.Count);
            foreach (var binding in bindings)
            {
                var account = await channelAccountRepository.GetByIdAsync(binding.AccountId, cancellationToken);
                var pendingApproval = await LoadPendingChannelApprovalAsync(
                    approvalRepository,
                    binding.SessionId,
                    cancellationToken);
                var recentAudit = await channelAuditQueryService.ListRecentByBindingIdAsync(binding.Id, 6, cancellationToken);

                items.Add(BuildChannelThreadSummary(
                    binding,
                    account,
                    pendingApproval,
                    TryResolveLastTurnOutcome(recentAudit)));
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.channels",
                eventType: "gateway.channels.threads_listed",
                level: "info",
                message: $"Channel threads query returned {items.Count} items.",
                attributes: new Dictionary<string, string?>
                {
                    ["count"] = items.Count.ToString(),
                });

            return Results.Ok(new ChannelsQueryResponse(items));
        });

        channels.MapGet("/threads/{bindingId}", async (
            HttpContext context,
            string bindingId,
            IConfiguration configuration,
            IWorkspaceService workspaceService,
            IThreadBindingRepository threadBindingRepository,
            IChannelAccountRepository channelAccountRepository,
            ChannelPolicyEngine channelPolicyEngine,
            ChannelAuditQueryService channelAuditQueryService,
            IApprovalRepository approvalRepository,
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
                    message: "Unauthorized access to channel thread detail endpoint.");
                return Results.Unauthorized();
            }

            var detail = await LoadChannelThreadDetailAsync(
                workspaceService,
                threadBindingRepository,
                channelAccountRepository,
                channelPolicyEngine,
                channelAuditQueryService,
                approvalRepository,
                bindingId,
                cancellationToken);
            if (detail is null)
            {
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.channels",
                    eventType: "gateway.channels.thread_not_found",
                    level: "warning",
                    message: "Requested channel thread was not found.",
                    attributes: new Dictionary<string, string?>
                    {
                        ["bindingId"] = bindingId,
                    });
                return Results.NotFound(new ErrorResponse(
                    Code: "channel.thread_not_found",
                    Message: "Channel thread was not found."));
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.channels",
                eventType: "gateway.channels.thread_fetched",
                level: "info",
                message: "Fetched channel thread detail.",
                sessionId: detail.Binding.SessionId,
                attributes: new Dictionary<string, string?>
                {
                    ["bindingId"] = detail.Binding.Id,
                    ["accountId"] = detail.Binding.AccountId,
                });

            return Results.Ok(detail);
        });

        channels.MapPatch("/threads/{bindingId}/settings", async (
            HttpContext context,
            string bindingId,
            UpdateThreadSettingsRequest request,
            IConfiguration configuration,
            IThreadBindingRepository threadBindingRepository,
            IDiagnosticsService diagnosticsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryAuthorize(context, configuration))
            {
                return Results.Unauthorized();
            }

            if (request.DeliveryMode is null)
            {
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.delivery_mode_required",
                    Message: "At least one setting field is required."));
            }

            var found = await threadBindingRepository.UpdateDeliveryModeOverrideAsync(
                bindingId,
                request.DeliveryMode,
                cancellationToken);

            if (!found)
            {
                return Results.NotFound(new ErrorResponse(
                    Code: "channel.thread_not_found",
                    Message: "Channel thread was not found."));
            }

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.channels",
                eventType: "gateway.channels.thread_settings_updated",
                level: "info",
                message: "Channel thread settings updated.",
                attributes: new Dictionary<string, string?>
                {
                    ["bindingId"] = bindingId,
                    ["deliveryMode"] = request.DeliveryMode?.ToString(),
                });

            return Results.NoContent();
        });

        channels.MapGet("/threads/{bindingId}/audit", async (
            HttpContext context,
            string bindingId,
            int? limit,
            IConfiguration configuration,
            IThreadBindingRepository threadBindingRepository,
            ChannelAuditQueryService channelAuditQueryService,
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
                    message: "Unauthorized access to channel audit endpoint.");
                return Results.Unauthorized();
            }

            var binding = await threadBindingRepository.GetByIdAsync(bindingId, cancellationToken);
            if (binding is null)
            {
                return Results.NotFound(new ErrorResponse(
                    Code: "channel.thread_not_found",
                    Message: "Channel thread was not found."));
            }

            var items = await channelAuditQueryService.ListRecentByBindingIdAsync(
                bindingId,
                NormalizeChannelsLimit(limit),
                cancellationToken);

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.channels",
                eventType: "gateway.channels.audit_listed",
                level: "info",
                message: $"Fetched channel audit entries for binding '{bindingId}'.",
                sessionId: binding.SessionId,
                attributes: new Dictionary<string, string?>
                {
                    ["bindingId"] = bindingId,
                    ["count"] = items.Count.ToString(),
                });

            return Results.Ok(items);
        });

        channels.MapPost("/webhook/{accountId}/events", async (
            HttpContext context,
            string accountId,
            IConfiguration configuration,
            IWorkspaceService workspaceService,
            IChannelAccountRepository channelAccountRepository,
            IThreadBindingRepository threadBindingRepository,
            GenericWebhookConnector webhookConnector,
            ChannelPolicyEngine channelPolicyEngine,
            ChannelAuditQueryService channelAuditQueryService,
            IApprovalRepository approvalRepository,
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
                    message: "Unauthorized access to channel webhook endpoint.");
                return Results.Unauthorized();
            }

            var account = await channelAccountRepository.GetByIdAsync(accountId, cancellationToken);
            if (account is null)
            {
                return Results.NotFound(new ErrorResponse(
                    Code: "channel.account_not_found",
                    Message: "Channel account was not found."));
            }

            if (account.ConnectorKind != ChannelConnectorKind.GenericWebhook)
            {
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.channel_account_connector_invalid",
                    Message: "Channel account is not a generic webhook account."));
            }

            if (!account.InboundEnabled)
            {
                return Results.Conflict(new ErrorResponse(
                    Code: "channel.account_inbound_disabled",
                    Message: "Channel account inbound delivery is disabled."));
            }

            using var reader = new StreamReader(context.Request.Body);
            var payloadJson = await reader.ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return Results.BadRequest(new ErrorResponse(
                    Code: "validation.channel_webhook_payload_required",
                    Message: "Webhook payload is required."));
            }

            var channelInboundGatewayService = context.RequestServices.GetRequiredService<ChannelInboundGatewayService>();
            ChannelInboundHandlingResult? handlingResult = null;
            var dispatchResult = await webhookConnector.HandleInboundAsync(
                account,
                payloadJson,
                context.Request.Headers[WebhookSecretHeaderName].ToString(),
                async (envelope, token) =>
                {
                    handlingResult = await channelInboundGatewayService.ProcessAsync(envelope, token);
                },
                cancellationToken);

            if (!dispatchResult.Accepted)
            {
                var (statusCode, error) = MapWebhookRejection(dispatchResult);
                RecordDiagnosticEvent(
                    diagnosticsService,
                    context,
                    source: "gateway.channels",
                    eventType: "gateway.channels.webhook_rejected",
                    level: statusCode == StatusCodes.Status401Unauthorized ? "warning" : "error",
                    message: error.Message,
                    attributes: new Dictionary<string, string?>
                    {
                        ["accountId"] = account.Id,
                        ["connectorKind"] = account.ConnectorKind.ToString(),
                        ["rejectionCode"] = dispatchResult.RejectionCode,
                    });
                return Results.Json(error, statusCode: statusCode);
            }

            if (handlingResult?.Processing is null)
            {
                return Results.Json(
                    new ErrorResponse(
                        Code: "channel.webhook_processing_failed",
                        Message: "Webhook event was accepted but channel processing did not complete."),
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var detail = await LoadChannelThreadDetailAsync(
                workspaceService,
                threadBindingRepository,
                channelAccountRepository,
                channelPolicyEngine,
                channelAuditQueryService,
                approvalRepository,
                handlingResult.Processing.Binding.Id,
                cancellationToken);

            RecordDiagnosticEvent(
                diagnosticsService,
                context,
                source: "gateway.channels",
                eventType: "gateway.channels.webhook_accepted",
                level: "info",
                message: "Accepted generic webhook event.",
                sessionId: handlingResult.Processing.Binding.SessionId,
                attributes: new Dictionary<string, string?>
                {
                    ["accountId"] = account.Id,
                    ["bindingId"] = handlingResult.Processing.Binding.Id,
                    ["createdBinding"] = handlingResult.Processing.CreatedBinding.ToString(),
                    ["eventType"] = dispatchResult.Event!.EventType.ToString(),
                    ["turnOutcomeKind"] = handlingResult.Turn?.Outcome.Kind.ToString(),
                });

            return Results.Ok(detail);
        });

    }
}
