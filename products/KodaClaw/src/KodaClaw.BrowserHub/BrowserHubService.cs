using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using KodaClaw.BrowserHub.Connection;
using KodaClaw.BrowserHub.Device;
using KodaClaw.BrowserHub.Models;
using KodaClaw.Contracts;

namespace KodaClaw.BrowserHub;

/// <summary>
/// Core implementation of <see cref="IBrowserHubService"/> for Phase 1
/// read-only browser operations and Phase 2 write operations.
///
/// <para>
/// Orchestrates <see cref="BrowserDeviceStore"/>, <see cref="DevicePairingService"/>,
/// <see cref="DeviceAuthService"/>, and <see cref="BridgeConnectionManager"/> to
/// forward Agent tool requests to the Chrome extension over the CDP bridge and
/// correlate the responses.
/// </para>
///
/// <para>
/// Request-response correlation: each outbound request is assigned a UUID v4
/// correlation ID. A <see cref="TaskCompletionSource{T}"/> is registered in
/// <see cref="_pendingRequests"/> before the message is sent. When the handler
/// fires <see cref="BridgeConnectionHandler.OnResponse"/>, the matching TCS is
/// completed and the awaiting method returns.
/// </para>
/// </summary>
public sealed class BrowserHubService : IBrowserHubService
{
    private const string DefaultSessionId = "hub-internal";
    private const int DefaultRequestTimeoutMs = 30_000;
    private const int UploadFileTimeoutMs = 60_000;
    private const int InterceptTimeoutMs = 60_000;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly BrowserDeviceStore _deviceStore;
    private readonly BridgeConnectionManager _connectionManager;
    private readonly BridgeConnectionHandler _connectionHandler;

    // ── Pending request map ───────────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, TaskCompletionSource<BridgeResponse>>
        _pendingRequests = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the service and wires up the response callback.
    /// </summary>
    /// <param name="deviceStore">Persistent device store.</param>
    /// <param name="connectionManager">WebSocket connection manager.</param>
    /// <param name="connectionHandler">Protocol message handler.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is <c>null</c>.</exception>
    public BrowserHubService(
        BrowserDeviceStore deviceStore,
        BridgeConnectionManager connectionManager,
        BridgeConnectionHandler connectionHandler)
    {
        ArgumentNullException.ThrowIfNull(deviceStore);
        ArgumentNullException.ThrowIfNull(connectionManager);
        ArgumentNullException.ThrowIfNull(connectionHandler);

        _deviceStore = deviceStore;
        _connectionManager = connectionManager;
        _connectionHandler = connectionHandler;

        _connectionHandler.OnResponse += CompleteRequest;
    }

    // ── IBrowserHubService ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<bool> IsConnectedAsync(CancellationToken ct = default)
    {
        var connected = _connectionManager.ConnectedDeviceIds.Count > 0;
        return Task.FromResult(connected);
    }

    /// <inheritdoc/>
    public async Task<BrowserStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var devices = await _deviceStore.GetAllDevicesAsync(ct).ConfigureAwait(false);
        var connectedDevices = devices.Where(d => d.IsOnline).ToList();

        var lastHeartbeat = devices
            .Where(d => d.LastSeenAt.HasValue)
            .Select(d => d.LastSeenAt!.Value)
            .OrderByDescending(t => t)
            .FirstOrDefault();

        return new BrowserStatus(
            IsConnected: connectedDevices.Count > 0,
            Devices: connectedDevices,
            LastHeartbeat: lastHeartbeat == default ? null : lastHeartbeat);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BrowserDevice>> GetConnectedDevicesAsync(
        CancellationToken ct = default)
    {
        var all = await _deviceStore.GetAllDevicesAsync(ct).ConfigureAwait(false);
        return all.Where(d => d.IsOnline).ToList();
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<NavigateResult>> NavigateAsync(
        string url,
        string? tabId = null,
        CancellationToken ct = default)
    {
        var payload = new { url };
        return await SendRequestAsync<NavigateResult>(
            action: "navigate",
            payload: payload,
            tabId: tabId,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<string>> SnapshotAsync(
        string tabId,
        string? selector = null,
        CancellationToken ct = default)
    {
        var payload = selector is not null ? new { selector } : (object?)null;
        return await SendRequestAsync<string>(
            action: "snapshot",
            payload: payload,
            tabId: tabId,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<string>> ScreenshotAsync(
        string tabId,
        ScreenshotFormat format = ScreenshotFormat.Jpeg,
        int quality = 80,
        CancellationToken ct = default)
    {
        var payload = new
        {
            format = format.ToString().ToLowerInvariant(),
            quality,
        };
        return await SendRequestAsync<string>(
            action: "screenshot",
            payload: payload,
            tabId: tabId,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<string>> GetUrlAsync(
        string tabId,
        CancellationToken ct = default)
    {
        return await SendRequestAsync<string>(
            action: "get_url",
            payload: null,
            tabId: tabId,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<IReadOnlyList<TabInfo>>> ListTabsAsync(
        CancellationToken ct = default)
    {
        return await SendRequestAsync<IReadOnlyList<TabInfo>>(
            action: "list_tabs",
            payload: null,
            tabId: null,
            ct: ct).ConfigureAwait(false);
    }

    // ── Phase 2: write operations ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<BrowserResult<ClickResult>> ClickAsync(
        string deviceId,
        string? tabId,
        int elementIndex,
        int? offsetX = null,
        int? offsetY = null,
        CancellationToken ct = default)
    {
        var payload = new { elementIndex, offsetX, offsetY };
        return await SendRequestAsync<ClickResult>(
            deviceId: deviceId,
            action: "click",
            payload: payload,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<TypeResult>> TypeAsync(
        string deviceId,
        string? tabId,
        int elementIndex,
        string text,
        bool clearFirst = false,
        int delayMs = 50,
        CancellationToken ct = default)
    {
        var payload = new { elementIndex, text, clearFirst, delayMs };
        return await SendRequestAsync<TypeResult>(
            deviceId: deviceId,
            action: "type",
            payload: payload,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<ScrollResult>> ScrollAsync(
        string deviceId,
        string? tabId,
        ScrollDirection direction,
        int? amount = null,
        int? elementIndex = null,
        CancellationToken ct = default)
    {
        var payload = new
        {
            direction = direction.ToString().ToLowerInvariant(),
            amount,
            elementIndex,
        };
        return await SendRequestAsync<ScrollResult>(
            deviceId: deviceId,
            action: "scroll",
            payload: payload,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<KeyPressResult>> KeyPressAsync(
        string deviceId,
        string? tabId,
        string key,
        CancellationToken ct = default)
    {
        var payload = new { key };
        return await SendRequestAsync<KeyPressResult>(
            deviceId: deviceId,
            action: "key_press",
            payload: payload,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<NavigationResult>> GoBackAsync(
        string deviceId,
        string? tabId,
        CancellationToken ct = default)
    {
        return await SendRequestAsync<NavigationResult>(
            deviceId: deviceId,
            action: "go_back",
            payload: null,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<CloseTabResult>> CloseTabAsync(
        string deviceId,
        string tabId,
        CancellationToken ct = default)
    {
        return await SendRequestAsync<CloseTabResult>(
            deviceId: deviceId,
            action: "close_tab",
            payload: null,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<SwitchTabResult>> SwitchTabAsync(
        string deviceId,
        string tabId,
        CancellationToken ct = default)
    {
        var payload = new { tabId };
        return await SendRequestAsync<SwitchTabResult>(
            deviceId: deviceId,
            action: "switch_tab",
            payload: payload,
            tabId: null,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<object?>> EvaluateAsync(
        string deviceId,
        string? tabId,
        string script,
        bool sandboxed = true,
        CancellationToken ct = default)
    {
        var payload = new { script, sandboxed };
        return await SendRequestAsync<object?>(
            deviceId: deviceId,
            action: "evaluate",
            payload: payload,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<EvaluateResult>> EvaluateWriteAsync(
        string deviceId,
        string? tabId,
        string script,
        CancellationToken ct = default)
    {
        var payload = new { script, sandboxed = false };
        return await SendRequestAsync<EvaluateResult>(
            deviceId: deviceId,
            action: "evaluate_write",
            payload: payload,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<CookiesResult>> GetCookiesAsync(
        string deviceId,
        string? tabId,
        string? url = null,
        CancellationToken ct = default)
    {
        var payload = url is not null ? new { url } : (object?)null;
        return await SendRequestAsync<CookiesResult>(
            deviceId: deviceId,
            action: "cookies",
            payload: payload,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<FormStateResult>> GetFormStateAsync(
        string deviceId,
        string? tabId,
        CancellationToken ct = default)
    {
        return await SendRequestAsync<FormStateResult>(
            deviceId: deviceId,
            action: "form_state",
            payload: null,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<ConsoleMessagesResult>> GetConsoleMessagesAsync(
        string deviceId,
        string? tabId,
        int? sinceTimestamp = null,
        CancellationToken ct = default)
    {
        var payload = sinceTimestamp.HasValue ? new { sinceTimestamp } : (object?)null;
        return await SendRequestAsync<ConsoleMessagesResult>(
            deviceId: deviceId,
            action: "console",
            payload: payload,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<WaitResult>> WaitAsync(
        string deviceId,
        string? tabId,
        int? durationMs = null,
        string? waitForSelector = null,
        string? waitUntil = null,
        CancellationToken ct = default)
    {
        var payload = new { durationMs, waitForSelector, waitUntil };
        var timeoutMs = durationMs.HasValue
            ? Math.Max(DefaultRequestTimeoutMs, durationMs.Value + 5_000)
            : DefaultRequestTimeoutMs;
        return await SendRequestAsync<WaitResult>(
            deviceId: deviceId,
            action: "wait",
            payload: payload,
            tabId: tabId,
            timeoutMs: timeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<UploadFileResult>> UploadFileAsync(
        string deviceId,
        string? tabId,
        int elementIndex,
        string filePath,
        CancellationToken ct = default)
    {
        var payload = new { elementIndex, filePath };
        return await SendRequestAsync<UploadFileResult>(
            deviceId: deviceId,
            action: "upload_file",
            payload: payload,
            tabId: tabId,
            timeoutMs: UploadFileTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    // ── Network intercept ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<BrowserResult<InterceptResult>> StartInterceptAsync(
        string deviceId,
        string? tabId,
        string? urlPattern = null,
        string[]? resourceTypes = null,
        bool requestHeaders = false,
        bool responseBody = false,
        CancellationToken ct = default)
    {
        var payload = new { urlPattern, resourceTypes, requestHeaders, responseBody };
        return await SendRequestAsync<InterceptResult>(
            deviceId: deviceId,
            action: "intercept",
            payload: payload,
            tabId: tabId,
            timeoutMs: InterceptTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<InterceptClearResult>> StopInterceptAsync(
        string deviceId,
        string? tabId,
        CancellationToken ct = default)
    {
        return await SendRequestAsync<InterceptClearResult>(
            deviceId: deviceId,
            action: "intercept_clear",
            payload: null,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BrowserResult<InterceptRequestsResult>> GetInterceptedRequestsAsync(
        string deviceId,
        string? tabId,
        string? urlPattern = null,
        long? sinceTimestamp = null,
        string[]? resourceTypes = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        var payload = new { urlPattern, sinceTimestamp, resourceTypes, limit };
        return await SendRequestAsync<InterceptRequestsResult>(
            deviceId: deviceId,
            action: "intercept_result",
            payload: payload,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs,
            ct: ct).ConfigureAwait(false);
    }

    // ── Request dispatch ──────────────────────────────────────────────────────

    /// <summary>
    /// Selects the first connected device, serialises a <see cref="BridgeRequest"/>,
    /// sends it, and awaits the correlated <see cref="BridgeResponse"/>.
    /// </summary>
    /// <typeparam name="T">Expected data type in the response.</typeparam>
    /// <param name="action">Bridge action name.</param>
    /// <param name="payload">Action payload object.</param>
    /// <param name="tabId">Optional target tab ID.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<BrowserResult<T>> SendRequestAsync<T>(
        string action,
        object? payload,
        string? tabId,
        CancellationToken ct)
    {
        var deviceId = _connectionManager.ConnectedDeviceIds.FirstOrDefault();
        if (deviceId is null)
        {
            return Fail<T>("BRIDGE_001", "No connected browser device.");
        }

        var (requestId, json) = _connectionHandler.BuildRequestJson(
            action: action,
            sessionId: DefaultSessionId,
            payload: payload,
            tabId: tabId,
            timeoutMs: DefaultRequestTimeoutMs);

        var tcs = new TaskCompletionSource<BridgeResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingRequests[requestId] = tcs;

        try
        {
            var sent = await _connectionManager
                .SendToDeviceAsync(deviceId, json, ct)
                .ConfigureAwait(false);

            if (!sent)
            {
                _pendingRequests.TryRemove(requestId, out _);
                return Fail<T>("BRIDGE_007", "Failed to send message to device.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(DefaultRequestTimeoutMs);

            try
            {
                var response = await tcs.Task
                    .WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);

                if (response.Ok)
                {
                    var data = DeserializeData<T>(response.Data);
                    return new BrowserResult<T>(Ok: true, Data: data);
                }

                return Fail<T>("BRIDGE_008", response.Error ?? "Unknown error.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Fail<T>("BRIDGE_004", "Request timed out.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    /// <summary>
    /// Routes a request to a specific device, serialises a <see cref="BridgeRequest"/>,
    /// sends it, and awaits the correlated <see cref="BridgeResponse"/>.
    /// Used by Phase 2 write operations that accept an explicit device identifier.
    /// </summary>
    /// <typeparam name="T">Expected data type in the response.</typeparam>
    /// <param name="deviceId">Target device identifier.</param>
    /// <param name="action">Bridge action name.</param>
    /// <param name="payload">Action payload object.</param>
    /// <param name="tabId">Optional target tab ID.</param>
    /// <param name="timeoutMs">Request timeout in milliseconds.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<BrowserResult<T>> SendRequestAsync<T>(
        string deviceId,
        string action,
        object? payload,
        string? tabId,
        int timeoutMs,
        CancellationToken ct)
    {
        if (!_connectionManager.ConnectedDeviceIds.Contains(deviceId))
        {
            return Fail<T>("BRIDGE_001", $"Device '{deviceId}' is not connected.");
        }

        var (requestId, json) = _connectionHandler.BuildRequestJson(
            action: action,
            sessionId: DefaultSessionId,
            payload: payload,
            tabId: tabId,
            timeoutMs: timeoutMs);

        var tcs = new TaskCompletionSource<BridgeResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingRequests[requestId] = tcs;

        try
        {
            var sent = await _connectionManager
                .SendToDeviceAsync(deviceId, json, ct)
                .ConfigureAwait(false);

            if (!sent)
            {
                _pendingRequests.TryRemove(requestId, out _);
                return Fail<T>("BRIDGE_007", "Failed to send message to device.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                var response = await tcs.Task
                    .WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);

                if (response.Ok)
                {
                    var data = DeserializeData<T>(response.Data);
                    return new BrowserResult<T>(Ok: true, Data: data);
                }

                return Fail<T>("BRIDGE_008", response.Error ?? "Unknown error.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Fail<T>("BRIDGE_004", "Request timed out.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    // ── Response correlation ──────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="BridgeConnectionHandler.OnResponse"/> when a response
    /// arrives. Locates the matching <see cref="TaskCompletionSource{T}"/> and
    /// completes it.
    /// </summary>
    private void CompleteRequest(string deviceId, BridgeResponse response)
    {
        if (_pendingRequests.TryRemove(response.Id, out var tcs))
        {
            tcs.TrySetResult(response);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BrowserResult<T> Fail<T>(string code, string error) =>
        new(Ok: false, Data: default, Error: error, ErrorCode: code);

    /// <summary>
    /// Deserialises the <c>data</c> field of a <see cref="BridgeResponse"/>.
    /// The field arrives as a <see cref="JsonElement"/> when using System.Text.Json;
    /// this helper converts it to <typeparamref name="T"/>.
    /// </summary>
    private static T? DeserializeData<T>(object? data)
    {
        if (data is null)
        {
            return default;
        }

        if (data is T directCast)
        {
            return directCast;
        }

        if (data is JsonElement element)
        {
            return element.Deserialize<T>(JsonOptions);
        }

        try
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    // ── JSON options ──────────────────────────────────────────────────────────

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
