# Iteration 36 FREEZE — 飞书 / Lark Channel 连接器

冻结日期：2026-03-22

---

## 背景与动机

KodaClaw 已集成 Telegram 和 GenericWebhook 两个渠道连接器。企业用户普遍使用飞书（Lark）作为内部协作工具，接入飞书渠道可让 KodaClaw 在飞书群聊 / 私聊中接收消息并自动回复，覆盖国内最主流的企业 IM 场景。

关键技术选择：KodaClaw 是 local-first 架构（Gateway 运行在 loopback，无公网 IP），飞书官方推荐的 **WebSocket 长连接模式**（`wss://open.feishu.cn/event_bus`）正好完全契合——客户端主动出站，无需公网 URL、无需内网穿透。

---

## 范围（IN）

### Phase 1（KC-3601）：Contracts & 枚举扩展

- `ChannelConnectorKind.Feishu = 2`
- Feishu 专用 DTO：WS 事件信封（`FeishuWsEventEnvelope`）、消息体（`FeishuImMessage`、`FeishuSender`、`FeishuMention`）、Token 响应（`FeishuTenantAccessTokenResponse`、`FeishuAppAccessTokenResponse`）、发消息响应（`FeishuSendMessageResponse`）

### Phase 2（KC-3602）：HTTP API Client

- `IFeishuApiClient` 接口：`GetTenantAccessTokenAsync`、`GetAppAccessTokenAsync`、`SendTextMessageAsync`、`SendImageMessageAsync`（先 upload 图片 → 获取 image_key → 发 image 消息）
- `HttpFeishuApiClient` 实现，调用 `open.feishu.cn` REST API
- Token 有效期 2 小时，`HttpFeishuApiClient` 内部缓存并在过期前 5 分钟自动刷新
- 两种 token 用途区分：WS 建连用 `app_access_token`，发消息用 `tenant_access_token`

### Phase 3（KC-3603）：WebSocket 长连接客户端

- `FeishuWebSocketClient`：管理 `wss://open.feishu.cn/event_bus` 连接生命周期
  - 建连：`GET /open-apis/auth/v3/app_access_token/internal` 获取 token → 连接 WSS → 发送 `registerApp` 认证消息
  - 心跳：30 秒 ping/pong，超时后自动断线重连（指数退避，最大 60 秒）
  - 事件接收：解析 `im.message.receive_v1` 事件，通过回调通知上层
  - **3 秒 ACK 约束**：收到事件后立即回复 ACK（WSS 同步），实际 `onEvent` 回调 fire-and-forget 到后台 Task
  - 事件去重：维护近 500 条已处理 `event_id` 的 LRU 集合（飞书平台在超时时会重推）
  - 断线重连时重新获取 `app_access_token`

### Phase 4（KC-3604）：FeishuConnector 核心实现

- `FeishuConnectorConfiguration`：从 `ChannelAccount.ConfigurationJson` 解析 `appId`、`appSecret`（支持 `credentialReference` 通过 `ChannelSecretResolver` 解析）、`defaultDeliveryMode`
- `FeishuConnectorOptions`：WS 重连配置、心跳间隔、事件去重窗口大小
- `FeishuConnector : IChannelConnector`：
  - `StartAsync`：初始化 `FeishuWebSocketClient`，触发 ACK-then-background 模式的 `onEvent`
  - `StopAsync`：清理 WS 连接和已启动账号
  - `SendAsync`：有 MediaAttachments → `SendImageMessageAsync`；无 → `SendTextMessageAsync`；`ExternalThreadId` 即飞书的 `chat_id` 或 `open_id`（根据线程类型区分）
  - @ 提及解析：群组消息解析 `mentions` 字段，判断 bot `app_id` 是否被 @；文本中剥离 `@_user_x` 标签；将提及信号通过 `ChannelEventEnvelope` 的现有机制传递

### Phase 5（KC-3605）：Gateway 入站路由扩展

- `ChannelInboundGatewayService`：注入 `FeishuConnector`，新增 `StartFeishuAccountAsync` / `StopFeishuAccountAsync`
- `ChannelConnectorHostedService`（实现 `IChannelConnectorRegistry`）：
  - `StartAsync`：同时查询并启动 Feishu 账号
  - `StopAsync`：同时停止 Feishu 账号
  - `ReloadAccountAsync`：按 `account.ConnectorKind` 路由到对应 connector 的 Stop/Start
  - `StopAccountAsync`：同上路由

### Phase 6（KC-3606）：Gateway 配套端点与诊断修复

- `GatewayApp.ChannelEndpoints.cs`：连接器描述符列表新增 Feishu 条目；新增 `POST /api/channels/test/feishu`（接受 `{ appId, appSecret }` → 调 `GetTenantAccessTokenAsync` → 返回 `{ appName }` 或错误）
- `GatewayApp.ChannelValidation.cs`：`ReconcileChannelAccountRuntimeAsync` 扩展 Feishu 分支（与 Telegram 同等处理：Stop → Start → 写 Connected/Degraded）；`ResolveChannelAccountState` switch 显式加 Feishu case
- `SandboxRiskOverviewService.cs`：`SupportsOutbound` 加 Feishu
- `SecretMigrationReportService.cs`：switch 加 `ChannelConnectorKind.Feishu → BuildFeishuChannelItemAsync`（解析 `appSecret`）

### Phase 7（KC-3607）：测试补全

- `ChannelContractsTests.cs`：新增 Feishu `ChannelConnectorKind` JSON 序列化断言（`"Feishu"` 字符串路径）
- `ChannelApiIntegrationTests.cs`：新增 Feishu 账号 upsert → state 验证 → stop 生命周期集成测试（mock `IFeishuApiClient`）
- `FeishuConnectorConfigurationTests.cs`（新增 UnitTest）：配置解析、凭证引用解析
- `FeishuConnectorTests.cs`（新增 UnitTest）：事件去重逻辑、@ 提及解析

### Phase 8（KC-3608~3609）：前端集成

- `types/contracts.ts`：`ChannelConnectorKind` 新增 `"Feishu"`
- `lib/api.ts`：新增 `testFeishuCredentials(appId, appSecret)`
- `ChannelsDesk.tsx`：添加账号表单中，选择飞书时展示 `appId` + `appSecret`（密码型）输入框 + 连通性测试按钮
- `ChannelSetupWizard.tsx`：新增飞书引导步骤（说明：飞书开发者后台创建企业自建应用 → 开启机器人 → 订阅 `im.message.receive_v1` 事件 → 填入 `appId`/`appSecret` → 测试连通性）

---

## 非目标（OUT）

- **飞书自定义机器人**（Webhook 单向推出）：与 local-first 无公网 IP 场景不符
- **飞书消息卡片（Card Message）格式**：首期只做纯文本 + 图片
- **飞书 Lark 国际版**（`open.larksuite.com`）domain 切换：首期写死国内域名
- **飞书 @ 特定用户**：首期不支持
- **飞书消息已读回执**：首期不支持
- **飞书表情反应（Reaction）事件**：首期不处理
- **per-session-type model binding**：Feishu channel 与其他 channel 用同一路由逻辑

---

## 关键架构约束

| 约束 | 说明 |
|------|------|
| WS 3 秒 ACK | 收到 `im.message.receive_v1` 后必须在 3 秒内回复 WSS ACK；onEvent 回调必须 fire-and-forget |
| Token 双轨 | WS 建连用 `app_access_token`；HTTP 发消息用 `tenant_access_token`；均需 < 2h 刷新 |
| 事件去重 | 飞书平台在未收到 ACK 时会重推；连接器维护近 500 条 event_id LRU |
| 群组 @ 检测 | 解析 `mentions` 字段，text 中剥离飞书特有的 `@_user_x` 标记 |
| local-first | 使用 WS 出站连接，不需要公网 IP；Config 仅 appId + appSecret |

---

## 关键契约变更

| 类型 | 变更 |
|------|------|
| 修改枚举 | `ChannelConnectorKind.Feishu = 2` |
| 新增 DTO | `FeishuWsEventEnvelope`、`FeishuImMessage`、`FeishuSender`、`FeishuMention`、`FeishuTenantAccessTokenResponse` 等 |
| 新增类 | `IFeishuApiClient`、`HttpFeishuApiClient`、`FeishuWebSocketClient`、`FeishuConnector`、`FeishuConnectorConfiguration`、`FeishuConnectorOptions` |
| 修改类 | `ChannelInboundGatewayService`（新增 Feishu Start/Stop）、`ChannelConnectorHostedService`（扩展 Feishu 生命周期）、`GatewayApp.ChannelValidation.cs`（Reconcile + ResolveState）、`SandboxRiskOverviewService`（SupportsOutbound）、`SecretMigrationReportService`（switch 分支） |
| 新增端点 | `POST /api/channels/test/feishu` |
| 前端类型 | `ChannelConnectorKind` 新增 `"Feishu"` |
| 前端 API | `testFeishuCredentials(appId, appSecret)` |

---

## 验证命令

```bash
# L0
dotnet build KodaClaw.sln
cd apps/kodaclaw-web && npm run typecheck && npm run build

# L1 - 单元测试
dotnet test tests/KodaClaw.UnitTests --filter "Feishu"

# L2 - 集成测试
dotnet test tests/KodaClaw.IntegrationTests --filter "Feishu"
dotnet test tests/KodaClaw.IntegrationTests --filter "ChannelApi"

# L3 - 契约测试
dotnet test tests/KodaClaw.ContractTests --filter "Channel"

# 全量回归
dotnet test KodaClaw.sln -m:1

# 前端测试
cd apps/kodaclaw-web
npm run test
npm run test:e2e  # 需要 Gateway 运行中
```

---

## 受影响文件清单

| 文件 | 类型 |
|------|------|
| `src/KodaClaw.Contracts/ChannelConnectorKind.cs` | 修改 |
| `src/KodaClaw.ChannelHub/Connectors/Feishu/FeishuApiContracts.cs` | 新增 |
| `src/KodaClaw.ChannelHub/Connectors/Feishu/IFeishuApiClient.cs` | 新增 |
| `src/KodaClaw.ChannelHub/Connectors/Feishu/HttpFeishuApiClient.cs` | 新增 |
| `src/KodaClaw.ChannelHub/Connectors/Feishu/FeishuWebSocketClient.cs` | 新增 |
| `src/KodaClaw.ChannelHub/Connectors/Feishu/FeishuConnector.cs` | 新增 |
| `src/KodaClaw.ChannelHub/Connectors/Feishu/FeishuConnectorConfiguration.cs` | 新增 |
| `src/KodaClaw.ChannelHub/Connectors/Feishu/FeishuConnectorOptions.cs` | 新增 |
| `src/KodaClaw.ChannelHub/ServiceCollectionExtensions.cs` | 修改 |
| `src/KodaClaw.Gateway/Channels/ChannelInboundGatewayService.cs` | 修改 |
| `src/KodaClaw.Gateway/Channels/ChannelConnectorHostedService.cs` | 修改 |
| `src/KodaClaw.Gateway/Validation/GatewayApp.ChannelValidation.cs` | 修改 |
| `src/KodaClaw.Gateway/SandboxRiskOverviewService.cs` | 修改 |
| `src/KodaClaw.Gateway/SecretMigrationReportService.cs` | 修改 |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.ChannelEndpoints.cs` | 修改 |
| `tests/KodaClaw.UnitTests/ChannelHub/FeishuConnectorConfigurationTests.cs` | 新增 |
| `tests/KodaClaw.UnitTests/ChannelHub/FeishuConnectorTests.cs` | 新增 |
| `tests/KodaClaw.ContractTests/Channels/ChannelContractsTests.cs` | 修改 |
| `tests/KodaClaw.IntegrationTests/Gateway/ChannelApiIntegrationTests.cs` | 修改 |
| `apps/kodaclaw-web/src/types/contracts.ts` | 修改 |
| `apps/kodaclaw-web/src/lib/api.ts` | 修改 |
| `apps/kodaclaw-web/src/components/ChannelsDesk.tsx` | 修改 |
| `apps/kodaclaw-web/src/components/settings/ChannelSetupWizard.tsx` | 修改 |
