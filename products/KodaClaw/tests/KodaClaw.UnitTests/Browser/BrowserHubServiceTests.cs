using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Linq;
using FluentAssertions;
using KodaClaw.BrowserHub;
using KodaClaw.BrowserHub.Connection;
using KodaClaw.BrowserHub.Device;
using KodaClaw.BrowserHub.Models;
using KodaClaw.Contracts;
using Xunit;

namespace KodaClaw.UnitTests.Browser;

public sealed class BrowserHubServiceTests : IAsyncLifetime
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "kodaclaw-browserhub-tests", Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _receiveLoops = [];
    private BridgeConnectionManager? _manager;

    [Fact]
    public async Task ListTabsAsync_aggregates_tabs_from_all_connected_devices_and_sets_device_id()
    {
        var service = CreateService([
            new DeviceScript("device-a", request => BuildListTabsResponse(request, new[]
            {
                new { id = "11", url = "https://a.example", title = "A", active = true },
            })),
            new DeviceScript("device-b", request => BuildListTabsResponse(request, new[]
            {
                new { id = "22", url = "https://b.example", title = "B", active = false },
            })),
        ]);

        var result = await service.ListTabsAsync(_cts.Token);

        result.Ok.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Should().HaveCount(2);
        result.Data.Should().ContainSingle(t => t.TabId == "11" && t.DeviceId == "device-a");
        result.Data.Should().ContainSingle(t => t.TabId == "22" && t.DeviceId == "device-b");
    }

    [Fact]
    public async Task EvaluateDomAsync_sends_live_dom_action_and_deserializes_json_value()
    {
        string? capturedAction = null;
        JsonElement? capturedPayload = null;

        var service = CreateService([
            new DeviceScript("device-a", request =>
            {
                capturedAction = request.GetProperty("action").GetString();
                capturedPayload = request.GetProperty("payload").Clone();
                return BuildResponse(request, new
                {
                    count = 3,
                    firstHref = "https://example.com/r1",
                });
            }),
        ]);

        var result = await service.EvaluateDomAsync(
            "device-a",
            "11",
            "({ count: document.links.length, firstHref: document.links[0]?.href ?? null })",
            null,
            _cts.Token);

        result.Ok.Should().BeTrue();
        capturedAction.Should().Be("evaluate_dom");
        capturedPayload.Should().NotBeNull();
        capturedPayload!.Value.GetProperty("script").GetString()
            .Should().Contain("document.links.length");

        var json = JsonSerializer.SerializeToElement(result.Data);
        json.GetProperty("count").GetInt32().Should().Be(3);
        json.GetProperty("firstHref").GetString().Should().Be("https://example.com/r1");
    }

    [Fact]
    public async Task ExtractLinksAsync_sends_high_level_action_and_deserializes_links()
    {
        string? capturedAction = null;
        JsonElement? capturedPayload = null;

        var service = CreateService([
            new DeviceScript("device-a", request =>
            {
                capturedAction = request.GetProperty("action").GetString();
                capturedPayload = request.GetProperty("payload").Clone();
                return BuildResponse(request, new
                {
                    kind = "link_extract",
                    url = "https://example.com",
                    title = "Example",
                    selector = "main",
                    linkSelector = "a[href]",
                    totalMatches = 2,
                    returnedCount = 2,
                    sameOriginOnly = false,
                    links = new[]
                    {
                        new { text = "Docs", href = "https://example.com/docs", title = (string?)null, host = "example.com", sameOrigin = true },
                        new { text = "GitHub", href = "https://github.com/example", title = (string?)null, host = "github.com", sameOrigin = false },
                    },
                    note = (string?)null,
                });
            }),
        ]);

        var result = await service.ExtractLinksAsync("device-a", "11", "main", "a[href]", 5, false, null, _cts.Token);

        result.Ok.Should().BeTrue();
        capturedAction.Should().Be("extract_links");
        capturedPayload.Should().NotBeNull();
        capturedPayload!.Value.GetProperty("selector").GetString().Should().Be("main");
        capturedPayload.Value.GetProperty("limit").GetInt32().Should().Be(5);
        result.Data!.Links.Should().HaveCount(2);
        result.Data.Links[0].Text.Should().Be("Docs");
    }

    [Fact]
    public async Task ExtractResultsAsync_sends_high_level_action_and_deserializes_results()
    {
        string? capturedAction = null;
        JsonElement? capturedPayload = null;

        var service = CreateService([
            new DeviceScript("device-a", request =>
            {
                capturedAction = request.GetProperty("action").GetString();
                capturedPayload = request.GetProperty("payload").Clone();
                return BuildResponse(request, new
                {
                    kind = "result_extract",
                    url = "https://google.com/search?q=openclaw",
                    title = "openclaw - Google Search",
                    strategy = "google",
                    selector = "#search",
                    itemSelector = "div.g",
                    totalMatches = 1,
                    returnedCount = 1,
                    sameOriginOnly = false,
                    results = new[]
                    {
                        new { index = 0, title = "OpenClaw", url = "https://github.com/openclaw", snippet = "OpenClaw on GitHub", source = "github.com" },
                    },
                    note = (string?)null,
                });
            }),
        ]);

        var result = await service.ExtractResultsAsync("device-a", "11", strategy: "auto", limit: 10, framePath: null, ct: _cts.Token);

        result.Ok.Should().BeTrue();
        capturedAction.Should().Be("extract_results");
        capturedPayload.Should().NotBeNull();
        capturedPayload!.Value.GetProperty("strategy").GetString().Should().Be("auto");
        capturedPayload.Value.GetProperty("limit").GetInt32().Should().Be(10);
        result.Data!.Results.Should().ContainSingle();
        result.Data.Results[0].Title.Should().Be("OpenClaw");
    }

    [Fact]
    public async Task SnapshotAsync_includes_same_origin_frame_path_in_payload()
    {
        JsonElement? capturedPayload = null;

        var service = CreateService([
            new DeviceScript("device-a", request =>
            {
                capturedPayload = request.GetProperty("payload").Clone();
                return BuildResponse(request, "{\"kind\":\"dom_snapshot\"}");
            }),
        ]);

        var result = await service.SnapshotAsync(
            tabId: "11",
            selector: "main",
            framePath: ["iframe[name='shell']", "iframe#content"],
            deviceId: "device-a",
            ct: _cts.Token);

        result.Ok.Should().BeTrue();
        capturedPayload.Should().NotBeNull();
        capturedPayload!.Value.GetProperty("selector").GetString().Should().Be("main");
        capturedPayload.Value.GetProperty("framePath").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("iframe[name='shell']", "iframe#content");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _cts.Cancel();

        foreach (var loop in _receiveLoops)
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_manager is not null)
        {
            await _manager.DisposeAsync();
        }

        try
        {
            if (Directory.Exists(_workspaceRoot))
            {
                Directory.Delete(_workspaceRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    private BrowserHubService CreateService(
        IEnumerable<DeviceScript> deviceScripts)
    {
        Directory.CreateDirectory(_workspaceRoot);
        var store = new BrowserDeviceStore(_workspaceRoot);
        _manager = new BridgeConnectionManager();
        var auth = new DeviceAuthService(store);
        var handler = new BridgeConnectionHandler(_manager, store, auth);
        var service = new BrowserHubService(store, _manager, handler);

        foreach (var script in deviceScripts)
        {
            var socket = new ScriptedWebSocket(script.Handler);
            _manager.RegisterConnectionAsync(script.DeviceId, socket, _cts.Token).GetAwaiter().GetResult();
            _receiveLoops.Add(_manager.RunReceiveLoopAsync(script.DeviceId, socket, _cts.Token));
        }

        return service;
    }

    private static string BuildListTabsResponse(JsonElement request, object data)
        => BuildResponse(request, data);

    private static string BuildResponse(JsonElement request, object data)
    {
        var response = new
        {
            version = "1.0",
            id = request.GetProperty("id").GetString(),
            ok = true,
            data,
        };

        return JsonSerializer.Serialize(response);
    }

    private sealed record DeviceScript(string DeviceId, Func<JsonElement, string> Handler);

    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Func<JsonElement, string> _onSend;
        private readonly ConcurrentQueue<byte[]> _pendingMessages = new();
        private readonly SemaphoreSlim _messageSignal = new(0);
        private WebSocketState _state = WebSocketState.Open;

        public ScriptedWebSocket(Func<JsonElement, string> onSend)
        {
            _onSend = onSend;
        }

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            _messageSignal.Release();
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _state = WebSocketState.Closed;
            _messageSignal.Dispose();
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            await _messageSignal.WaitAsync(cancellationToken);

            if (_state != WebSocketState.Open || !_pendingMessages.TryDequeue(out var payload))
            {
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, endOfMessage: true);
            }

            payload.AsSpan().CopyTo(buffer.AsSpan());
            return new WebSocketReceiveResult(payload.Length, WebSocketMessageType.Text, endOfMessage: true);
        }

        public override async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await _messageSignal.WaitAsync(cancellationToken);

            if (_state != WebSocketState.Open || !_pendingMessages.TryDequeue(out var payload))
            {
                return new ValueWebSocketReceiveResult(0, WebSocketMessageType.Close, endOfMessage: true);
            }

            payload.CopyTo(buffer);
            return new ValueWebSocketReceiveResult(payload.Length, WebSocketMessageType.Text, endOfMessage: true);
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            EnqueueResponse(buffer.AsSpan());
            return Task.CompletedTask;
        }

        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            EnqueueResponse(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private void EnqueueResponse(ReadOnlySpan<byte> requestBytes)
        {
            var requestJson = Encoding.UTF8.GetString(requestBytes);
            using var doc = JsonDocument.Parse(requestJson);
            var responseJson = _onSend(doc.RootElement.Clone());
            _pendingMessages.Enqueue(Encoding.UTF8.GetBytes(responseJson));
            _messageSignal.Release();
        }
    }
}
