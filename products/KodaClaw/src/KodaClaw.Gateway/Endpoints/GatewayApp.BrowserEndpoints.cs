using KodaClaw.BrowserHub;
using KodaClaw.BrowserHub.Models;
using KodaClaw.BrowserHub.Connection;
using KodaClaw.BrowserHub.Device;
using KodaClaw.BrowserHub.Screenshot;
using KodaClaw.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text.Json;

public static partial class GatewayApp
{
    private static void MapBrowserEndpoints(WebApplication app)
    {
        var browser = app.MapGroup("/api/browser");

        browser.MapGet("/status", async (
            HttpContext context,
            IConfiguration configuration,
            IBrowserHubService browserHubService,
            IDiagnosticsService diagnosticsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryAuthorize(context, configuration, diagnosticsService))
                return Results.Unauthorized();

            var status = await browserHubService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(status);
        });

        browser.MapGet("/devices", async (
            HttpContext context,
            IConfiguration configuration,
            IBrowserHubService browserHubService,
            IDiagnosticsService diagnosticsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryAuthorize(context, configuration, diagnosticsService))
                return Results.Unauthorized();

            var devices = await browserHubService.GetConnectedDevicesAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(devices);
        });

        browser.MapPost("/screenshot/upload", async (
            HttpContext context,
            ScreenshotUploadService screenshotUploadService,
            CancellationToken cancellationToken) =>
        {
            var authHeader = context.Request.Headers.Authorization.ToString();
            const string bearerPrefix = "Bearer ";
            if (!authHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                return Results.Unauthorized();

            var token = authHeader[bearerPrefix.Length..].Trim();
            var filePath = screenshotUploadService.ValidateAndConsume(token);
            if (filePath is null)
                return Results.Unauthorized();

            await using var fileStream = File.Create(filePath);
            await context.Request.Body.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

            return Results.Ok(new { fileId = Path.GetFileNameWithoutExtension(filePath) });
        });

        browser.MapGet("/pairing/token", (
            DevicePairingService pairingService) =>
        {
            var (tokenValue, _) = pairingService.GeneratePairingToken();
            return Results.Ok(new { token = tokenValue, expiresIn = 300 });
        });

        browser.MapPost("/pairing/challenge", async (
            HttpContext context,
            DevicePairingService pairingService) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var bodyJson = await reader.ReadToEndAsync();
            string? token = null;
            if (!string.IsNullOrWhiteSpace(bodyJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(bodyJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("token", out var t) || root.TryGetProperty("Token", out t))
                        token = t.GetString();
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { error = "Invalid JSON body" });
                }
            }

            if (string.IsNullOrWhiteSpace(token))
                return Results.BadRequest(new { error = "Token is required" });

            try
            {
                var data = pairingService.GetPairingChallenge(token);
                return Results.Ok(new
                {
                    challenge = data.Challenge,
                    gwEcdsaPubKey = data.GwEcdsaPubKey,
                    gwEcdhPubKey = data.GwEcdhPubKey,
                    gwSignature = data.GwSignature,
                });
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound(new { error = "Invalid or expired pairing token" });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.ToString(), statusCode: 500);
            }
        });

        browser.MapPost("/pairing/complete", async (
            HttpContext context,
            DevicePairingService pairingService,
            CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var bodyJson = await reader.ReadToEndAsync();
            string? token = null;
            string? extEcdsaPubKey = null;
            string? extSignature = null;
            string? extEcdhPubKey = null;
            string? deviceId = null;
            string? label = null;
            if (!string.IsNullOrWhiteSpace(bodyJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(bodyJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("token", out var t) || root.TryGetProperty("Token", out t))
                        token = t.GetString();
                    if (root.TryGetProperty("extEcdsaPubKey", out var v) || root.TryGetProperty("ExtEcdsaPubKey", out v))
                        extEcdsaPubKey = v.GetString();
                    if (root.TryGetProperty("extSignature", out v) || root.TryGetProperty("ExtSignature", out v))
                        extSignature = v.GetString();
                    if (root.TryGetProperty("extEcdhPubKey", out v) || root.TryGetProperty("ExtEcdhPubKey", out v))
                        extEcdhPubKey = v.GetString();
                    if (root.TryGetProperty("deviceId", out v) || root.TryGetProperty("DeviceId", out v))
                        deviceId = v.GetString();
                    if (root.TryGetProperty("label", out v) || root.TryGetProperty("Label", out v))
                        label = v.GetString();
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { error = "Invalid JSON body" });
                }
            }

            if (string.IsNullOrWhiteSpace(token))
                return Results.BadRequest(new { error = "Token is required" });

            try
            {
                var result = await pairingService.CompletePairingAsync(
                    pairingToken: token,
                    extensionPublicKey: extEcdsaPubKey!,
                    challengeSignature: extSignature!,
                    extensionEcdhPublicKey: extEcdhPubKey!,
                    deviceId: deviceId!,
                    label: label,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                return Results.Ok(new { sessionToken = result.SessionToken });
            }
            catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });


        // TEMPORARY: Test endpoint for browser actions (will be removed after verification)
        browser.MapPost("/test/action", async (
            HttpContext context,
            IBrowserHubService browserHubService,
            BridgeConnectionManager connectionManager,
            CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { error = "Empty body" });

            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            var action = root.GetProperty("action").GetString()!;
            var tabId = root.TryGetProperty("tabId", out var tid) ? tid.GetString() : null;
            var deviceIds = connectionManager.ConnectedDeviceIds;
            if (deviceIds.Count == 0)
                return Results.Ok(new { ok = false, error = "No connected devices" });
            var deviceId = deviceIds.First();

            try
            {
                object? result = action switch
                {
                    "list_tabs" => await browserHubService.ListTabsAsync(cancellationToken),
                    "navigate" => await browserHubService.NavigateAsync(root.GetProperty("url").GetString()!, tabId, cancellationToken),
                    "get_url" => await browserHubService.GetUrlAsync(tabId!, cancellationToken),
                    "snapshot" => await browserHubService.SnapshotAsync(tabId!, null, cancellationToken),
                    "screenshot" => await browserHubService.ScreenshotAsync(tabId!, ScreenshotFormat.Jpeg, 80, cancellationToken),
                    "click" => await browserHubService.ClickAsync(deviceId, tabId, root.GetProperty("elementIndex").GetInt32(), null, null, cancellationToken),
                    "type" => await browserHubService.TypeAsync(deviceId, tabId, root.GetProperty("elementIndex").GetInt32(), root.GetProperty("text").GetString()!, false, 0, cancellationToken),
                    "scroll" => await browserHubService.ScrollAsync(deviceId, tabId, Enum.Parse<ScrollDirection>(root.GetProperty("direction").GetString()!, ignoreCase: true), null, null, cancellationToken),
                    "key_press" => await browserHubService.KeyPressAsync(deviceId, tabId, root.GetProperty("key").GetString()!, cancellationToken),
                    "go_back" => await browserHubService.GoBackAsync(deviceId, tabId, cancellationToken),
                    "close_tab" => await browserHubService.CloseTabAsync(deviceId, tabId!, cancellationToken),
                    "switch_tab" => await browserHubService.SwitchTabAsync(deviceId, tabId!, cancellationToken),
                    "evaluate" => await browserHubService.EvaluateAsync(deviceId, tabId, root.GetProperty("script").GetString()!, true, cancellationToken),
                    "evaluate_write" => await browserHubService.EvaluateWriteAsync(deviceId, tabId, root.GetProperty("script").GetString()!, cancellationToken),
                    "cookies" => await browserHubService.GetCookiesAsync(deviceId, tabId, null, cancellationToken),
                    "form_state" => await browserHubService.GetFormStateAsync(deviceId, tabId, cancellationToken),
                    "console" => await browserHubService.GetConsoleMessagesAsync(deviceId, tabId, null, cancellationToken),
                    "upload_file" => await browserHubService.UploadFileAsync(deviceId, tabId, root.GetProperty("elementIndex").GetInt32(), root.GetProperty("filePath").GetString()!, cancellationToken),
                    "wait" => await browserHubService.WaitAsync(deviceId, tabId, null, null, null, cancellationToken),
                    "intercept" => await browserHubService.StartInterceptAsync(deviceId, tabId, null, null, false, false, cancellationToken),
                    "intercept_clear" => await browserHubService.StopInterceptAsync(deviceId, tabId, cancellationToken),
                    "intercept_result" => await browserHubService.GetInterceptedRequestsAsync(deviceId, tabId, null, null, null, 50, cancellationToken),
                    _ => null
                };

                if (result is null)
                    return Results.BadRequest(new { error = $"Unknown action: {action}" });

                var okProp = result.GetType().GetProperty("Ok");
                var dataProp = result.GetType().GetProperty("Data");
                var errorProp = result.GetType().GetProperty("Error");
                return Results.Ok(new
                {
                    ok = okProp?.GetValue(result),
                    data = dataProp?.GetValue(result),
                    error = errorProp?.GetValue(result), errorCode = result.GetType().GetProperty("ErrorCode")?.GetValue(result)
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { ok = false, error = ex.Message, errorCode = "TEST_ERROR" });
            }
        });


        app.Map("/ws/bridge", async (
            HttpContext context,
            BridgeConnectionManager connectionManager,
            BridgeConnectionHandler _) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var deviceId = context.Request.Query["deviceId"].FirstOrDefault()
                ?? context.Request.Headers["X-Device-Id"].FirstOrDefault()
                ?? Guid.NewGuid().ToString("N");

            var subprotocol = context.WebSockets.WebSocketRequestedProtocols.FirstOrDefault();
            var webSocket = await context.WebSockets.AcceptWebSocketAsync(subprotocol).ConfigureAwait(false);
            await connectionManager.RegisterConnectionAsync(deviceId, webSocket, context.RequestAborted).ConfigureAwait(false);
            await connectionManager.RunReceiveLoopAsync(deviceId, webSocket, context.RequestAborted).ConfigureAwait(false);
        });


    }
}

internal sealed record PairingChallengeRequest(string Token);

internal sealed record PairingCompleteRequest(
    string Token,
    string ExtEcdsaPubKey,
    string ExtSignature,
    string ExtEcdhPubKey,
    string DeviceId,
    string? Label = null);
