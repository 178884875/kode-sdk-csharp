using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace KodaClaw.BrowserHub.Connection;

/// <summary>
/// Represents a single active WebSocket session with a browser extension device.
/// </summary>
/// <param name="DeviceId">The device identifier associated with this connection.</param>
/// <param name="WebSocket">The live WebSocket instance.</param>
/// <param name="ConnectedAt">UTC time when the connection was established.</param>
/// <param name="LastHeartbeatAt">UTC time of the most recently received message.</param>
internal sealed record DeviceConnection(
    string DeviceId,
    WebSocket WebSocket,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastHeartbeatAt);

/// <summary>
/// Manages all active WebSocket connections from browser extension devices.
/// Tracks live sessions in memory, routes outbound messages, monitors
/// heartbeat timeouts, and exposes an event for inbound message processing.
/// </summary>
public sealed class BridgeConnectionManager : IAsyncDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int HeartbeatIntervalSeconds = 30;
    private const int HeartbeatTimeoutSeconds = 90;
    private const int ReceiveBufferSize = 64 * 1024;

    // ── Private state ─────────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, DeviceConnection> _connections = new();
    private readonly CancellationTokenSource _cts = new();

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised whenever a text message is received from a connected device.
    /// Arguments are the device identifier and the raw JSON message text.
    /// </summary>
    public event Func<string, string, Task>? OnMessageReceived;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the manager and starts the background heartbeat monitor.
    /// </summary>
    public BridgeConnectionManager()
    {
        _ = RunHeartbeatMonitorAsync(_cts.Token);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers (or replaces) the WebSocket connection for the given device.
    /// </summary>
    /// <param name="deviceId">Unique device identifier.</param>
    /// <param name="webSocket">The active WebSocket connection.</param>
    /// <param name="ct">Cancellation token.</param>
    public ValueTask RegisterConnectionAsync(
        string deviceId,
        WebSocket webSocket,
        CancellationToken ct = default)
    {
        var conn = new DeviceConnection(
            DeviceId: deviceId,
            WebSocket: webSocket,
            ConnectedAt: DateTimeOffset.UtcNow,
            LastHeartbeatAt: DateTimeOffset.UtcNow);

        _connections[deviceId] = conn;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Removes the connection for the specified device and closes the WebSocket.
    /// </summary>
    /// <param name="deviceId">The device identifier to disconnect.</param>
    /// <param name="ct">Cancellation token.</param>
    public async ValueTask RemoveConnectionAsync(
        string deviceId,
        CancellationToken ct = default)
    {
        if (!_connections.TryRemove(deviceId, out var conn))
        {
            return;
        }

        try
        {
            if (conn.WebSocket.State == WebSocketState.Open)
            {
                await conn.WebSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection removed",
                    ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Best-effort close; ignore transport errors
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the device has an active open WebSocket connection.
    /// </summary>
    /// <param name="deviceId">The device identifier to test.</param>
    public bool IsConnected(string deviceId) =>
        _connections.TryGetValue(deviceId, out var conn)
        && conn.WebSocket.State == WebSocketState.Open;

    /// <summary>
    /// Returns a snapshot of all currently connected device identifiers.
    /// </summary>
    public IReadOnlyCollection<string> ConnectedDeviceIds =>
        _connections.Keys.ToList();

    /// <summary>
    /// Sends a JSON text message to the specified device.
    /// </summary>
    /// <param name="deviceId">Target device identifier.</param>
    /// <param name="message">The JSON text to transmit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the message was sent successfully; <c>false</c> when the
    /// device is not connected or the underlying send fails.
    /// </returns>
    public async Task<bool> SendToDeviceAsync(
        string deviceId,
        string message,
        CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(deviceId, out var conn))
        {
            return false;
        }

        if (conn.WebSocket.State != WebSocketState.Open)
        {
            return false;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await conn.WebSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _connections.TryRemove(deviceId, out _);
            return false;
        }
    }

    /// <summary>
    /// Runs the receive loop for the given device WebSocket until the connection
    /// closes or <paramref name="ct"/> is cancelled. Each complete text message
    /// is surfaced via <see cref="OnMessageReceived"/>.
    /// </summary>
    /// <param name="deviceId">The device owning the WebSocket.</param>
    /// <param name="webSocket">The WebSocket to read from.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RunReceiveLoopAsync(
        string deviceId,
        WebSocket webSocket,
        CancellationToken ct = default)
    {
        var buffer = new byte[ReceiveBufferSize];
        var builder = new StringBuilder();

        try
        {
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                builder.Clear();

                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket
                        .ReceiveAsync(new ArraySegment<byte>(buffer), ct)
                        .ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _connections.TryRemove(deviceId, out _);
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                } while (!result.EndOfMessage);

                // Refresh heartbeat timestamp on every received message
                if (_connections.TryGetValue(deviceId, out var conn))
                {
                    _connections[deviceId] = conn with { LastHeartbeatAt = DateTimeOffset.UtcNow };
                }

                var text = builder.ToString();
                if (!string.IsNullOrEmpty(text) && OnMessageReceived is { } handler)
                {
                    try
                    {
                        await handler(deviceId, text).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Handler errors must not crash the receive loop
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — exit quietly
        }
        catch
        {
            // Unexpected transport error — clean up the connection entry
        }
        finally
        {
            _connections.TryRemove(deviceId, out _);
        }
    }

    // ── Heartbeat monitor ─────────────────────────────────────────────────────

    /// <summary>
    /// Background loop that runs at <see cref="HeartbeatIntervalSeconds"/> intervals.
    /// Removes devices whose last inbound message exceeds the
    /// <see cref="HeartbeatTimeoutSeconds"/> threshold.
    /// </summary>
    private async Task RunHeartbeatMonitorAsync(CancellationToken ct)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(HeartbeatIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var timeout = TimeSpan.FromSeconds(HeartbeatTimeoutSeconds);
                var now = DateTimeOffset.UtcNow;

                foreach (var (deviceId, conn) in _connections)
                {
                    if (conn.WebSocket.State != WebSocketState.Open)
                    {
                        _connections.TryRemove(deviceId, out _);
                        continue;
                    }

                    if (now - conn.LastHeartbeatAt > timeout)
                    {
                        await RemoveConnectionAsync(deviceId, ct).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();

        foreach (var deviceId in _connections.Keys.ToList())
        {
            try
            {
                await RemoveConnectionAsync(deviceId).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during disposal
            }
            catch
            {
                // Best-effort
            }
        }
    }
}
