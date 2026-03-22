using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace KodaClaw.ChannelHub.Connectors.Feishu;

/// <summary>
/// 飞书 WebSocket 长连接客户端（pbbp2 protobuf 二进制帧协议）。
///
/// 连接流程：
///   1. POST /callback/ws/endpoint → 获取动态 wss:// URL（含 ticket 认证）
///   2. ws.ConnectAsync(url)，无需额外 Authorization 头
///   3. 收到 method=1 事件帧 → 立即 ACK（3s 内）→ fire-and-forget 业务回调
///   4. 按服务端配置的 PingInterval 发心跳 ping 帧
///   5. 断线后指数退避重连
///
/// 帧格式（protobuf field numbers）：
///   service(3,varint), method(4,varint), headers(5,repeated msg),
///   payload(8,bytes)；Header: key(1,string), value(2,string)
/// method=0 → 控制帧（ping/pong），method=1 → 数据帧（event）
/// </summary>
internal sealed class FeishuWebSocketClient : IAsyncDisposable
{
    // ── protobuf field numbers ────────────────────────────────────────────
    private const int FnSeqId   = 1;
    private const int FnLogId   = 2;
    private const int FnService = 3;
    private const int FnMethod  = 4;
    private const int FnHeaders = 5;
    private const int FnPayload = 8;
    private const int FnHdrKey  = 1;
    private const int FnHdrVal  = 2;

    // protobuf wire types
    private const int WtVarint          = 0;
    private const int WtLengthDelimited = 2;

    // frame method constants
    private const int MethodControl = 0; // ping / pong
    private const int MethodData    = 1; // event

    private const int EventDedupeWindowSize = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IFeishuApiClient _apiClient;
    private readonly string _appId;
    private readonly string _appSecret;
    private readonly Func<FeishuWsEventEnvelope, string, CancellationToken, Task> _onEvent;
    private readonly FeishuConnectorOptions _options;
    private readonly ConcurrentQueue<string> _dedupeQueue = new();
    private readonly ConcurrentDictionary<string, byte> _dedupeSet = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ChunkBuffer> _chunkBuffers = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _cts;
    private Task _loopTask = Task.CompletedTask;

    public FeishuWebSocketClient(
        IFeishuApiClient apiClient,
        string appId,
        string appSecret,
        Func<FeishuWsEventEnvelope, string, CancellationToken, Task> onEvent,
        FeishuConnectorOptions options)
    {
        _apiClient  = apiClient  ?? throw new ArgumentNullException(nameof(apiClient));
        _appId      = appId      ?? throw new ArgumentNullException(nameof(appId));
        _appSecret  = appSecret  ?? throw new ArgumentNullException(nameof(appSecret));
        _onEvent    = onEvent    ?? throw new ArgumentNullException(nameof(onEvent));
        _options    = options    ?? throw new ArgumentNullException(nameof(options));
    }

    public void Start(CancellationToken externalCancellation)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        _loopTask = Task.Run(() => RunConnectionLoopAsync(_cts.Token), CancellationToken.None);
    }

    /// <summary>等待首次 WebSocket 连接建立（注册完成），超时后抛 OperationCanceledException。</summary>
    public Task WaitForFirstConnectionAsync(CancellationToken cancellationToken = default)
        => _connectedTcs.Task.WaitAsync(cancellationToken);

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { await _loopTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    // ── 连接主循环（断线自动重连）────────────────────────────────────────

    private async Task RunConnectionLoopAsync(CancellationToken cancellationToken)
    {
        var retryDelay = _options.ReconnectBaseDelay;
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            try
            {
                await RunSingleConnectionAsync(cancellationToken).ConfigureAwait(false);
                Console.Error.WriteLine($"[FeishuWS] app={_appId} connection closed, reconnecting (attempt #{attempt + 1})...");
                retryDelay = _options.ReconnectBaseDelay;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _connectedTcs.TrySetCanceled(cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FeishuWS] app={_appId} connection failed: {ex.Message}");
                _connectedTcs.TrySetException(ex);
            }

            try
            {
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            retryDelay = retryDelay * 2 < _options.ReconnectMaxDelay
                ? retryDelay * 2
                : _options.ReconnectMaxDelay;
        }
    }

    private async Task RunSingleConnectionAsync(CancellationToken cancellationToken)
    {
        var endpoint = await _apiClient
            .GetWsEndpointAsync(_appId, _appSecret, cancellationToken)
            .ConfigureAwait(false);

        var serviceId = ParseServiceId(endpoint.Url);

        using var ws = new ClientWebSocket();
        // 认证通过 URL 中的 ticket 完成，无需额外 Authorization 头
        await ws.ConnectAsync(new Uri(endpoint.Url), cancellationToken).ConfigureAwait(false);
        Console.Error.WriteLine($"[FeishuWS] app={_appId} connected (service_id={serviceId})");

        _connectedTcs.TrySetResult();

        var pingInterval = TimeSpan.FromSeconds(
            endpoint.PingIntervalSeconds > 0 ? endpoint.PingIntervalSeconds : 90);

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = RunHeartbeatAsync(ws, serviceId, pingInterval, heartbeatCts.Token);

        try
        {
            await ReceiveLoopAsync(ws, serviceId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeatTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    // ── 心跳 ─────────────────────────────────────────────────────────────

    private async Task RunHeartbeatAsync(
        ClientWebSocket ws,
        int serviceId,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                var ping = BuildFrame(serviceId, MethodControl,
                    [("type", "ping")],
                    payload: null);
                await ws.SendAsync(ping, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FeishuWS] app={_appId} heartbeat error: {ex.Message}");
                return;
            }
        }
    }

    // ── 接收循环 ──────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(
        ClientWebSocket ws,
        int serviceId,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];

        while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var (rawBytes, closed) = await ReceiveBinaryAsync(ws, buffer, cancellationToken)
                .ConfigureAwait(false);

            if (closed || rawBytes is null) break;

            Console.Error.WriteLine($"[FeishuWS] RawFrame: {rawBytes.Length} bytes | hex={BitConverter.ToString(rawBytes[..Math.Min(32, rawBytes.Length)])}");

            var frame = DecodeFrame(rawBytes);
            if (frame is null)
            {
                Console.Error.WriteLine($"[FeishuWS] DecodeFrame returned null");
                continue;
            }

            var frameType = GetHeader(frame.Headers, "type");
            Console.Error.WriteLine($"[FeishuWS] Decoded: service={frame.Service} method={frame.Method} type={frameType} payloadLen={frame.Payload?.Length ?? 0}");
            if (frameType == "pong") continue;

            if (frame.Method == MethodData)
            {
                await HandleEventFrameAsync(ws, frame, serviceId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task<(byte[]? Data, bool Closed)> ReceiveBinaryAsync(
        ClientWebSocket ws,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        using var ms = new System.IO.MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
                return (null, true);

            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return (ms.ToArray(), false);
    }

    // ── 事件帧处理（3 秒 ACK + 分块重组 + fire-and-forget 业务逻辑）─────

    private async Task HandleEventFrameAsync(
        ClientWebSocket ws,
        FeishuProtoFrame frame,
        int serviceId,
        CancellationToken cancellationToken)
    {
        var frameReceivedAt = Stopwatch.GetTimestamp();

        // 仅处理 type=event 的帧（控制帧 type=ping/pong 已在调用处过滤）
        var frameType = GetHeader(frame.Headers, "type");
        if (!string.IsNullOrEmpty(frameType) &&
            !string.Equals(frameType, "event", StringComparison.Ordinal))
        {
            return;
        }

        // 立即 ACK，满足飞书 3s 要求
        await AckFrameAsync(ws, frame, frameReceivedAt, cancellationToken).ConfigureAwait(false);

        if (frame.Payload is null || frame.Payload.Length == 0) return;

        // ── 分块重组（官方 SDK DataCache.mergeData 逻辑）──────────────────
        // 每帧 headers 含 message_id、sum（总分块数）、seq（0-based 序号）
        var msgId = GetHeader(frame.Headers, "message_id");
        var sumStr = GetHeader(frame.Headers, "sum");
        var seqStr = GetHeader(frame.Headers, "seq");

        byte[] fullPayload;

        if (!string.IsNullOrEmpty(sumStr) &&
            int.TryParse(sumStr, out var sum) && sum > 1 &&
            !string.IsNullOrEmpty(msgId))
        {
            // 多块消息：累积直到全部到齐
            int.TryParse(seqStr, out var seq);
            var buf = _chunkBuffers.GetOrAdd(msgId, _ => new ChunkBuffer(sum));
            buf.Set(seq, frame.Payload);

            if (!buf.IsComplete)
                return; // 等待剩余分块

            fullPayload = buf.Assemble();
            _chunkBuffers.TryRemove(msgId, out _);
        }
        else
        {
            // 单块消息（sum==1 或无分块头）
            fullPayload = frame.Payload;
        }

        Console.Error.WriteLine($"[FeishuWS] Event payload ({fullPayload.Length}b): {Encoding.UTF8.GetString(fullPayload, 0, Math.Min(300, fullPayload.Length))}");

        FeishuWsEventEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<FeishuWsEventEnvelope>(fullPayload, JsonOptions);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[FeishuWS] Deserialize failed: {ex.Message}");
            return;
        }

        if (envelope is null) return;

        var eventId = envelope.Header?.EventId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(eventId) && !TryTrackEvent(eventId)) return;

        // fire-and-forget：业务处理不阻塞 WS 接收循环
        _ = Task.Run(async () =>
        {
            try
            {
                await _onEvent(envelope, eventId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FeishuWS] app={_appId} event handler error: {ex.Message}");
            }
        }, CancellationToken.None);
    }

    private static async Task AckFrameAsync(
        ClientWebSocket ws,
        FeishuProtoFrame frame,
        long frameReceivedTimestamp,
        CancellationToken cancellationToken)
    {
        var msgId = GetHeader(frame.Headers, "message_id");
        if (string.IsNullOrWhiteSpace(msgId)) return;

        // biz_rt = 收到帧到发出 ACK 的耗时（毫秒），对齐官方 SDK 行为
        var elapsedMs = (long)((Stopwatch.GetTimestamp() - frameReceivedTimestamp)
            * 1000.0 / Stopwatch.Frequency);

        // ACK = 复用原帧 headers + 追加 biz_rt，payload = {"code":200}
        var ackHeaders = frame.Headers
            .Append(("biz_rt", elapsedMs.ToString()))
            .ToArray();
        var ackPayload = Encoding.UTF8.GetBytes("{\"code\":200}");
        var ackBytes = BuildFrame(frame.Service, frame.Method, ackHeaders, ackPayload);

        await ws.SendAsync(ackBytes, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken)
            .ConfigureAwait(false);
    }

    // ── Protobuf 编码 / 解码 ──────────────────────────────────────────────

    private static ArraySegment<byte> BuildFrame(
        int service,
        int method,
        (string Key, string Value)[] headers,
        byte[]? payload)
    {
        var buf = new List<byte>(256);

        WriteTag(buf, FnService, WtVarint);  WriteVarint(buf, (ulong)service);
        WriteTag(buf, FnMethod,  WtVarint);  WriteVarint(buf, (ulong)method);

        foreach (var (k, v) in headers)
        {
            var hbuf = new List<byte>();
            WriteTag(hbuf, FnHdrKey, WtLengthDelimited); WriteString(hbuf, k);
            WriteTag(hbuf, FnHdrVal, WtLengthDelimited); WriteString(hbuf, v);

            WriteTag(buf, FnHeaders, WtLengthDelimited);
            WriteVarint(buf, (ulong)hbuf.Count);
            buf.AddRange(hbuf);
        }

        if (payload is { Length: > 0 })
        {
            WriteTag(buf, FnPayload, WtLengthDelimited);
            WriteVarint(buf, (ulong)payload.Length);
            buf.AddRange(payload);
        }

        return new ArraySegment<byte>(buf.ToArray());
    }

    private static FeishuProtoFrame? DecodeFrame(byte[] data)
    {
        try
        {
            var pos = 0;
            long service = 0, method = 0;
            var headers = new List<(string Key, string Value)>();
            byte[]? payload = null;

            while (pos < data.Length)
            {
                var tag = (int)ReadVarint(data, ref pos);
                var fieldNumber = tag >> 3;
                var wireType   = tag & 0x07;

                switch (fieldNumber)
                {
                    case FnSeqId:
                    case FnLogId:
                        ReadVarint(data, ref pos);
                        break;
                    case FnService:
                        service = (long)ReadVarint(data, ref pos);
                        break;
                    case FnMethod:
                        method = (long)ReadVarint(data, ref pos);
                        break;
                    case FnHeaders:
                    {
                        var len = (int)ReadVarint(data, ref pos);
                        headers.Add(DecodeHeader(data, pos, len));
                        pos += len;
                        break;
                    }
                    case FnPayload:
                    {
                        var len = (int)ReadVarint(data, ref pos);
                        payload = new byte[len];
                        Array.Copy(data, pos, payload, 0, len);
                        pos += len;
                        break;
                    }
                    default:
                        SkipField(data, ref pos, wireType);
                        break;
                }
            }

            return new FeishuProtoFrame((int)service, (int)method, headers, payload);
        }
        catch
        {
            return null;
        }
    }

    private static (string Key, string Value) DecodeHeader(byte[] data, int start, int length)
    {
        var pos = start;
        var end = start + length;
        string key = "", value = "";

        while (pos < end)
        {
            var tag = (int)ReadVarint(data, ref pos);
            var fieldNumber = tag >> 3;
            var len = (int)ReadVarint(data, ref pos);
            var s = Encoding.UTF8.GetString(data, pos, len);
            pos += len;

            if      (fieldNumber == FnHdrKey) key   = s;
            else if (fieldNumber == FnHdrVal) value = s;
        }

        return (key, value);
    }

    private static ulong ReadVarint(byte[] data, ref int pos)
    {
        ulong result = 0;
        int   shift  = 0;

        while (true)
        {
            var b = data[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }

        return result;
    }

    private static void SkipField(byte[] data, ref int pos, int wireType)
    {
        switch (wireType)
        {
            case WtVarint:          ReadVarint(data, ref pos); break;
            case WtLengthDelimited: pos += (int)ReadVarint(data, ref pos); break;
            case 1:                 pos += 8; break; // 64-bit
            case 5:                 pos += 4; break; // 32-bit
        }
    }

    private static void WriteTag(List<byte> buf, int fieldNumber, int wireType)
        => WriteVarint(buf, (ulong)((fieldNumber << 3) | wireType));

    private static void WriteVarint(List<byte> buf, ulong value)
    {
        while (value > 0x7F)
        {
            buf.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        buf.Add((byte)value);
    }

    private static void WriteString(List<byte> buf, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        WriteVarint(buf, (ulong)bytes.Length);
        buf.AddRange(bytes);
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────

    private static string GetHeader(IReadOnlyList<(string Key, string Value)> headers, string key)
        => headers.FirstOrDefault(h => string.Equals(h.Key, key, StringComparison.Ordinal)).Value
           ?? string.Empty;

    /// <summary>从 WS URL query string 中提取 service_id 参数。</summary>
    private static int ParseServiceId(string url)
    {
        try
        {
            const string marker = "service_id=";
            var idx = url.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return 0;

            idx += marker.Length;
            var end = url.IndexOf('&', idx);
            var s = end < 0 ? url[idx..] : url[idx..end];
            return int.TryParse(s, out var id) ? id : 0;
        }
        catch
        {
            return 0;
        }
    }


    // ── 事件去重 LRU ──────────────────────────────────────────────────────

    private bool TryTrackEvent(string eventId)
    {
        if (!_dedupeSet.TryAdd(eventId, 0)) return false;

        _dedupeQueue.Enqueue(eventId);

        while (_dedupeQueue.Count > EventDedupeWindowSize)
        {
            if (_dedupeQueue.TryDequeue(out var oldest))
                _dedupeSet.TryRemove(oldest, out _);
        }

        return true;
    }
}

/// <summary>飞书分块消息缓冲区（对应官方 SDK DataCache.mergeData）。</summary>
internal sealed class ChunkBuffer
{
    private readonly byte[]?[] _chunks;
    private int _received;

    public ChunkBuffer(int total) => _chunks = new byte[total][];

    public void Set(int seq, byte[] data)
    {
        if (seq < 0 || seq >= _chunks.Length) return;
        if (_chunks[seq] is not null) return; // 已收到
        _chunks[seq] = data;
        Interlocked.Increment(ref _received);
    }

    public bool IsComplete => _received >= _chunks.Length;

    public byte[] Assemble()
    {
        var total = _chunks.Sum(c => c?.Length ?? 0);
        var result = new byte[total];
        var offset = 0;
        foreach (var chunk in _chunks)
        {
            if (chunk is null) continue;
            chunk.CopyTo(result, offset);
            offset += chunk.Length;
        }
        return result;
    }
}

/// <summary>飞书 protobuf 帧解码结果（内部使用）。</summary>
internal sealed class FeishuProtoFrame
{
    public FeishuProtoFrame(
        int service,
        int method,
        List<(string Key, string Value)> headers,
        byte[]? payload)
    {
        Service = service;
        Method  = method;
        Headers = headers;
        Payload = payload;
    }

    public int Service { get; }
    public int Method  { get; }
    public IReadOnlyList<(string Key, string Value)> Headers { get; }
    public byte[]? Payload { get; }
}
