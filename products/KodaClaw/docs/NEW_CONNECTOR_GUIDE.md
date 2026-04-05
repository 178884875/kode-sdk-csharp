# 新渠道接入指南

> KodaClaw Channel Connector 开发者文档
> 最后更新：2026-04-05（Phase 1-3 重构后）

## 概述

KodaClaw 的 channel 层采用 **Connector + Resolver** 架构。每个外部渠道（IM 平台、Webhook 等）实现 `IChannelConnector` 接口，通过 DI 注册后自动被 `ChannelConnectorKindResolver` 索引，无需在分发层手动添加 switch case。

新增一个 connector 只需要 3 步：
1. 写 connector 实现类
2. 加枚举值
3. 注册 DI

**不再需要**修改 `ChannelDeliveryDispatchService`、`ChannelInboundGatewayService`、`ChannelConnectorHostedService`。

---

## 1. 接口定义

```csharp
public interface IChannelConnector
{
    ChannelConnectorKind Kind { get; }

    Task StartAsync(
        ChannelAccount account,
        Func<ChannelEventEnvelope, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken = default);

    Task StopAsync(string accountId, CancellationToken cancellationToken = default);

    Task SendAsync(ChannelOutboundDraft draft, CancellationToken cancellationToken = default);
}
```

四个成员：

| 成员 | 语义 |
|------|------|
| `Kind` | 返回对应的 `ChannelConnectorKind` 枚举值，Resolver 用它做索引 |
| `StartAsync` | 启动入站监听（polling/websocket/等）。`onEvent` 回调用于推送 `ChannelEventEnvelope` 到下游管道 |
| `StopAsync` | 停止入站监听，释放资源 |
| `SendAsync` | 出站发送。接收 `ChannelOutboundDraft`，调用平台 API 发送消息 |

---

## 2. 入站：ChannelEventEnvelope

所有 connector 的入站事件必须归一化为 `ChannelEventEnvelope`：

```csharp
public sealed record ChannelEventEnvelope(
    string EventId,                        // 事件唯一 ID，格式建议 "{platform}-{nativeId}"
    ChannelEventType EventType,             // MessageReceived / MessageEdited / MessageDeleted / ...
    ChannelConnectorKind ConnectorKind,     // 你的 connector 的 Kind
    string AccountId,                      // ChannelAccount.Id
    string ExternalThreadId,               // 平台侧的会话/群组 ID
    ChannelThreadType ThreadType,          // DirectMessage / Group
    DateTimeOffset OccurredAt,             // 事件发生时间（用平台提供的时间，不要用本地时间）
    ChannelIdentity? Sender = null,        // 发送者身份
    ChannelIdentity? Recipient = null,     // 接收者身份
    string? ExternalMessageId = null,      // 平台侧的消息 ID
    string? Text = null,                   // 消息文本
    string? CorrelationId = null,
    string? BindingId = null,              // 已绑定的 ThreadBinding ID（首次由系统分配）
    string? SessionId = null,
    string? MetadataJson = null,           // 平台特有的元数据
    DeliveryMode? DefaultDeliveryMode = null,
    IReadOnlyList<MediaReference>? MediaAttachments = null);
```

**身份模型**：

```csharp
public sealed record ChannelIdentity(
    string Id,                 // 平台侧的用户 ID
    string? Username = null,   // @username
    string? DisplayName = null,// 昵称
    bool IsBot = false,
    string? MetadataJson = null);
```

**媒体引用**：

```csharp
public record MediaReference(
    string MediaId,        // 本地存储的 media ID（通过 IMediaStore 存储后获得）
    string ContentType,    // MIME type
    string? FileName = null,
    long? SizeBytes = null,
    int? DurationMs = null);
```

### 入站媒体处理策略

有三种选择：

| 策略 | 适用场景 | 参考 |
|------|---------|------|
| **下载存储** | 平台有文件下载 API，且 agent 需要读取内容 | 钉钉（通过 API 下载 → IMediaStore） |
| **引用占位** | 平台下载流程复杂（如 Telegram 需要 getFile + download 两步），或 agent 只需知道"用户发了媒体" | Telegram（存 fileId 为 MediaId，Text 设为 `[图片]` 占位） |
| **不支持** | 平台限制或暂不需要 | 微信（纯文本） |

**建议**：优先实现"引用占位"（最小工作量），后续按需升级为"下载存储"。

---

## 3. 出站：ChannelOutboundDraft

Agent 的出站消息封装为 `ChannelOutboundDraft`，由 `ChannelDeliveryDispatchService` 通过 Resolver 分发到对应 connector：

```csharp
public sealed record ChannelOutboundDraft(
    string DraftId,
    string BindingId,
    ChannelConnectorKind ConnectorKind,
    string AccountId,
    string ExternalThreadId,
    string MessageText,                      // 消息文本（Markdown 或纯文本）
    ChannelThreadType ThreadType = ChannelThreadType.DirectMessage,
    DeliveryMode DeliveryMode = DeliveryMode.AutoSend,
    DateTimeOffset CreatedAt = default,
    string? SessionId = null,
    string? CorrelationId = null,
    string? ApprovalId = null,
    string? MetadataJson = null,             // 平台特有的出站参数（如钉钉 ActionCard）
    IReadOnlyList<MediaReference>? MediaAttachments = null,
    OutboundMessageFormat Format = OutboundMessageFormat.Auto);
```

### 格式处理

`OutboundMessageFormat` 枚举指导 connector 如何渲染 `MessageText`：

```csharp
public enum OutboundMessageFormat
{
    Auto = 0,        // connector 自行决定（保持默认行为）
    PlainText = 1,   // 明确要求纯文本
    Markdown = 2,    // 要求尽量保留 Markdown 格式
}
```

各 connector 的处理方式：

| 平台 | Auto | Markdown | PlainText |
|------|------|----------|-----------|
| Telegram | 纯文本（无 parse_mode） | 加 `parse_mode="Markdown"` | 纯文本 |
| 钉钉 DM | 启发式检测 `ContainsMarkdown()` | 走 Markdown 格式 | 走纯文本 |
| 钉钉群 | 纯文本 | 纯文本（群消息限制） | 纯文本 |
| 飞书 | 纯文本 | Markdown → Post 富文本，失败 fallback 纯文本 | 纯文本 |
| 微信 | 剥 Markdown 为纯文本 | 剥 Markdown（平台限制） | 剥 Markdown |

**你的 connector 应该**：
- `Auto`：选择平台最合适的默认格式
- `Markdown`：如果平台支持富文本/Markdown，尽量渲染；不支持就 fallback 到纯文本
- `PlainText`：强制纯文本，不做任何格式转换

### 出站媒体

出站媒体通过 `MediaAttachments` 传递 `MediaReference` 列表。Connector 从 `IMediaStore` 读取文件内容，上传到平台 API。

如果平台不支持某种媒体类型，应该 fallback 到纯文本发送（不要抛异常）。

如果 connector 完全不支持出站（如 GenericWebhook），`SendAsync` 应抛 `NotSupportedException`。

---

## 4. 目录结构与文件模板

以 Telegram 为参考，新建 connector 的目录结构：

```
src/KodaClaw.ChannelHub/Connectors/{PlatformName}/
├── {PlatformName}Connector.cs              # 主 connector，实现 IChannelConnector
├── {PlatformName}ConnectorConfiguration.cs  # 配置解析（从 ChannelAccount.ConfigurationJson 提取）
├── {PlatformName}ConnectorOptions.cs        # 行为选项（可选，用于测试和调优）
├── I{PlatformName}ApiClient.cs             # 平台 API 客户端接口（可选）
├── Http{PlatformName}ApiClient.cs          # HTTP 实现（可选）
└── {PlatformName}ApiContracts.cs            # DTO 定义（可选）
```

最小实现只需要一个 `{PlatformName}Connector.cs`。

---

## 5. 配置模型

### ChannelAccount

connector 运行时接收 `ChannelAccount`，关键字段：

```csharp
public sealed record ChannelAccount(
    string Id,                           // 系统分配的账户 ID
    ChannelConnectorKind ConnectorKind,   // 必须匹配你的 Kind
    string DisplayName,
    ChannelAccountState State,           // Connected 才会被启动
    string? ConfigurationJson = null,    // 平台特有的配置（JSON 字符串）
    string? CredentialReference = null,   // 指向 SecretStore 的凭证引用
    bool InboundEnabled = true);
```

### Configuration 解析

每个 connector 定义自己的 `internal sealed record`，提供 `static FromAccount(ChannelAccount, ChannelSecretResolver?)` 工厂方法：

```csharp
internal sealed record SlackConnectorConfiguration
{
    public string BotToken { get; init; }
    public string AppId { get; init; }
    public DeliveryMode? DefaultDeliveryMode { get; init; }

    public static SlackConnectorConfiguration FromAccount(
        ChannelAccount account,
        ChannelSecretResolver? secretResolver = null)
    {
        var json = JsonDocument.Parse(account.ConfigurationJson ?? "{}");
        var token = secretResolver?.Resolve(
            json.RootElement.GetProperty("botToken").GetString(),
            credentialReference: account.CredentialReference)
            ?? json.RootElement.GetProperty("botToken").GetString()
            ?? throw new InvalidOperationException("Slack bot token is required.");

        return new SlackConnectorConfiguration
        {
            BotToken = token,
            AppId = json.RootElement.GetProperty("appId").GetString() ?? "",
            DefaultDeliveryMode = ...,
        };
    }
}
```

### Options（可选）

用于测试和运行时调优，通过 DI 注入：

```csharp
public sealed record SlackConnectorOptions(
    TimeSpan ReconnectDelay = default,
    int MaxRetries = 3);
```

---

## 6. 接入清单

### Step 1：加枚举值

```csharp
// src/KodaClaw.Contracts/Channels/ChannelConnectorKind.cs
public enum ChannelConnectorKind
{
    Telegram = 0,
    GenericWebhook = 1,
    Feishu = 2,
    WeChat = 3,
    DingTalk = 4,
    Relay = 5,
    Slack = 6,  // ← 新增
}
```

### Step 2：写 Connector 实现

```csharp
// src/KodaClaw.ChannelHub/Connectors/Slack/SlackConnector.cs
public sealed class SlackConnector : IChannelConnector
{
    public ChannelConnectorKind Kind => ChannelConnectorKind.Slack;

    private readonly ConcurrentDictionary<string, StartedAccount> _startedAccounts = new();

    public Task StartAsync(
        ChannelAccount account,
        Func<ChannelEventEnvelope, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken = default)
    {
        // 1. 验证 account.ConnectorKind == Kind
        // 2. 解析 Configuration
        // 3. 启动入站监听（WebSocket / Webhook / Polling）
        // 4. 收到消息时调用 onEvent(envelope, ct)
    }

    public Task StopAsync(string accountId, CancellationToken cancellationToken = default)
    {
        // 1. 从 _startedAccounts 移除
        // 2. 取消 CancellationTokenSource
        // 3. await 监听任务退出
    }

    public Task SendAsync(ChannelOutboundDraft draft, CancellationToken cancellationToken = default)
    {
        // 1. 根据 draft.Format 决定渲染方式
        // 2. 调用平台 API 发送
        // 3. 处理 MediaAttachments（如有）
    }
}
```

### Step 3：注册 DI

```csharp
// src/KodaClaw.ChannelHub/ServiceCollectionExtensions.cs
// 在 AddKodaClawChannelHub() 方法中添加：

services.TryAddSingleton<ISlackApiClient, HttpSlackApiClient>();
services.TryAddSingleton<SlackConnector>();
services.AddSingleton<IChannelConnector>(sp => sp.GetRequiredService<SlackConnector>());
```

**就这么多了。** 不需要改任何其他文件。`ChannelConnectorKindResolver` 会自动索引新的 connector，`ChannelDeliveryDispatchService` 会自动找到它，`ChannelConnectorHostedService` 会自动启动它。

---

## 7. 测试

参考现有 connector 的测试模式：

- **单元测试**：`tests/KodaClaw.UnitTests/ChannelHub/` — Mock API client，验证 Start/Stop/Send 逻辑
- **集成测试**：`tests/KodaClaw.IntegrationTests/ChannelHub/` — 使用真实或 Fake API client，验证端到端流程

测试命名约定：`{PlatformName}Connector{Feature}Tests`

关键测试场景：
- `StartAsync` 重复调用同一个 account → 应抛 `InvalidOperationException`
- `StopAsync` 不存在的 account → 应静默成功
- `SendAsync` 未启动的 account → 应抛 `InvalidOperationException`
- `TryMapToEnvelope` 各消息类型 → 验证 Envelope 字段正确
- `Format` 处理 → 验证 Auto/Markdown/PlainText 三种模式的行为

---

## 8. 已知坑点（来自微信/钉钉/Telegram 接入经验）

| # | 坑 | 说明 |
|---|-----|------|
| 1 | **Content-Type 不要带 charset** | 用 `MediaTypeHeaderValue("application/json")`，不要用 `new StringContent(json, Encoding.UTF8, "application/json")`。某些平台（微信 iLink）会拒绝带 charset 的请求 |
| 2 | **长轮询必须禁用 keep-alive** | 加 `request.Headers.ConnectionClose = true`，否则 HTTP/2 连接复用会导致轮询卡死 |
| 3 | **平台 context token 不要存 ThreadBinding** | 用内存 `ConcurrentDictionary` 缓存，key 格式 `{accountId}::{platformUserId}`，接受重启丢失 |
| 4 | **不要用 `ReadFromJsonAsync<T>()`** | 某些平台响应 Content-Type 是 `application/octet-stream`，用 `ReadAsStringAsync` + `JsonSerializer.Deserialize<T>()` |
| 5 | **不要用 `EnsureSuccessStatusCode()`** | 4xx 时要读 response body 才能知道具体错误。先读 body 再判断 status code |
| 6 | **API 调用超时要合理** | 轮询类 API 用 30-60 秒超时，普通 API 用 10 秒。不要用 HttpClient 默认的 100 秒 |
| 7 | **Agent 可能不调 channel_send** | `ChannelTurnOrchestrator` 有 fallback：当 `sentTexts.Count == 0 && RawResponse` 非空时自动 dispatch。你的 connector 不需要处理这个 |
| 8 | **StartAsync 内部用 Task.Run 启动监听** | 不要 await 监听循环，否则会阻塞 `ChannelConnectorHostedService.StartAsync` 的后续 connector 启动 |
| 9 | **多账户用 ConcurrentDictionary 管理** | 一个 connector 可能同时服务多个 account（比如两个 Telegram bot），用 `ConcurrentDictionary<string, StartedAccount>` 管理 |
| 10 | **诊断事件用 RecordDiagnosticEvent** | 注入 `IDiagnosticsService`，在关键节点（account started/stopped/message received/send failed）记录诊断事件 |

---

## 9. 架构参考

```
┌─────────────────────────────────────────────────────┐
│                   平台 (Telegram/飞书/...)              │
└──────────────────────┬──────────────────────────────┘
                       │ 平台协议 (HTTP/WebSocket/Polling)
                       ▼
┌─────────────────────────────────────────────────────┐
│              XxxConnector : IChannelConnector         │
│  ┌──────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │StartAsync│  │  StopAsync   │  │  SendAsync    │  │
│  │入站监听   │  │  停止释放    │  │  出站发送     │  │
│  └────┬─────┘  └──────────────┘  └───────┬───────┘  │
└───────┼─────────────────────────────────┼───────────┘
        │ ChannelEventEnvelope           │ ChannelOutboundDraft
        ▼                                 ▲
┌─────────────────────────────────────────────────────┐
│           ChannelInboundGatewayService               │
│     统一入口 → 去重 → ThreadBinding → Agent          │
└──────────────────────┬──────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────┐
│           ChannelTurnOrchestrator                    │
│        Session 锁 → 策略检查 → Agent 执行              │
└──────────────────────┬──────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────┐
│       ChannelDeliveryDispatchService                 │
│   Resolver.TryGet(Kind) → Connector.SendAsync        │
└─────────────────────────────────────────────────────┘
```

Resolver 是连接入站和出站的桥梁：
- **入站**：`HostedService` → 遍历 `Resolver.GetAll()` → `InboundGateway.StartAccountAsync` → `Connector.StartAsync`
- **出站**：`DispatchService` → `Resolver.TryGet(draft.ConnectorKind)` → `Connector.SendAsync`

---

## 10. 相关文档

| 文档 | 内容 |
|------|------|
| `docs/CHANNEL_SPEC.md` | Channel 系统设计规范（领域模型、安全策略、会话隔离） |
| `docs/ADR-0009-channels-v1.md` | 架构决策：为什么 connector 是 product-owned 而非 plugin-hosted |
| `docs/WECHAT_INTEGRATION.md` | 微信接入完整记录（含 §11 接入教训） |
| `docs/TELEGRAM_ONBOARDING.md` | Telegram 用户侧配置指南 |
| `docs/ITERATION_5_FREEZE.md` | Channel v1 冻结范围定义 |
