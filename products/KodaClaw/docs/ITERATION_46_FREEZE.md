# Iteration 46 FREEZE — 微信个人号渠道接入（WeChat iLink Channel）

**冻结日期**：2026-03-23
**范围摘要**：通过 iLink Bot 协议接入微信个人号。用户在 Settings → 渠道中扫码登录微信，KodaClaw 即可收发微信私信消息。本迭代仅支持私信（DM）文本消息，单账号，媒体/群聊为非目标。

---

## Iter 45 审查结论

Iter 45（KC-4501~4507）正在进行中，本迭代不对其作形式审查，直接开启 Iter 46。

---

## 背景与动机

Telegram 和飞书已完成接入，微信是国内用户最高频的 IM 工具。iLink 是目前最稳定的微信个人号 Bot 中间层，提供 HTTP 长轮询 + 扫码登录能力，与现有 Telegram 架构最接近（均为长轮询），实现复杂度可控。

**用户 Outcome**：
- 用户扫码登录后，微信私信自动触发 KodaClaw Agent，回复直接发回微信
- 已有的 Telegram/飞书渠道行为完全不变

---

## 设计决策记录

| 决策 | 结论 |
|------|------|
| 连接方式 | HTTP 长轮询（`getupdates`，35s hold），与 Telegram 模式一致 |
| 认证 | 扫码登录，获取 `bot_token` 后存 `ConfigurationJson`（明文，生产建议迁移至 CredentialReference + Keychain） |
| 群聊 | 本迭代不支持，仅 DM（`from_user_id` 作为 `ExternalThreadId`） |
| 媒体 | 本迭代不支持（图片/语音/文件），收到后忽略 item，仅处理 `type=1` 文本 |
| 多账号 | 本迭代单账号（架构已支持多账号，但 UI 只允许登记一个 WeChat 账号） |
| 游标持久化 | `get_updates_buf` 存 `~/.kodaclaw/state/wechat/{accountId}/sync_buf.json`，按 accountId 隔离 |
| Markdown 转纯文本 | `SendAsync` 层做，去除 `**`、`##`、`>`、`` ` `` 等标记 |
| context_token | 从收到的消息提取，存入 `ChannelEventEnvelope.MetadataJson`，`SendAsync` 时读取回传 |
| 消息去重 | 内存 LRU 500 条（message_id），防止长轮询重放 |
| session 过期（errcode -14） | 标记账号 State → Degraded，停止轮询，等待用户重新扫码 |

---

## API 端点设计（新增）

```
POST /api/channels/wechat/get-qrcode
  → 调 iLink GET /ilink/bot/get_bot_qrcode?bot_type=3
  → 返回 { qrcode: string, qrcodeImgUrl: string }

GET  /api/channels/wechat/qrcode-status?qrcode={token}
  → 调 iLink GET /ilink/bot/get_qrcode_status?qrcode={token}
  → 返回 { status: "wait"|"scaned"|"confirmed"|"expired", botToken?: string }
  → status=confirmed 时：将 botToken 存 Keychain，创建/更新 ChannelAccount，启动 Connector

POST /api/channels/test-wechat-credentials
  → 调 iLink POST /getLoginStatus，验证 bot_token 有效性
  → 返回 { ok: boolean, errorMessage?: string }
```

---

## KC 条目

### KC-4601：Contracts 层扩展

**改动层**：`KodaClaw.Contracts`

- `ChannelConnectorKind.cs` 加 `WeChat = 3`
- 新建 `WeChatQrCodeResult.cs`：
  ```csharp
  public sealed record WeChatQrCodeResult(string Qrcode, string QrcodeImgUrl);
  ```
- 新建 `WeChatQrCodeStatus.cs`：
  ```csharp
  public sealed record WeChatQrCodeStatus(
      string Status,        // "wait" | "scaned" | "confirmed" | "expired"
      string? BotToken);
  ```

**验证**：`dotnet build` 0 错 0 警告（L0）

---

### KC-4602：WeChatApiContracts + IWeChatApiClient + HttpWeChatApiClient

**改动层**：`KodaClaw.ChannelHub/Connectors/WeChat/`（新建目录）

**新建文件**：

1. **`WeChatApiContracts.cs`** — iLink API 的所有 DTO：
   - `ILinkGetUpdatesResponse`（ret, msgs, get_updates_buf, longpolling_timeout_ms）
   - `ILinkMessage`（message_id, from_user_id, context_token, item_list）
   - `ILinkMessageItem`（type, text_item?）
   - `ILinkTextItem`（text）
   - `ILinkSendMessageRequest`（msg, base_info）
   - `ILinkSendMessageBody`（from_user_id, to_user_id, client_id, context_token, message_type, message_state, item_list）
   - `ILinkQrCodeResponse`（qrcode, qrcode_img_content）
   - `ILinkQrCodeStatusResponse`（status, bot_token?, ilink_bot_id?, ilink_user_id?）
   - `ILinkLoginStatusResponse`（ret）
   - JSON 命名策略：`snake_case`（`[JsonPropertyName]` 标注）

2. **`IWeChatApiClient.cs`**：
   ```csharp
   public interface IWeChatApiClient
   {
       Task<ILinkQrCodeResponse> GetQrCodeAsync(CancellationToken ct = default);
       Task<ILinkQrCodeStatusResponse> GetQrCodeStatusAsync(string qrcode, CancellationToken ct = default);
       Task<ILinkLoginStatusResponse> CheckLoginStatusAsync(string botToken, CancellationToken ct = default);
       Task<ILinkGetUpdatesResponse> GetUpdatesAsync(string syncBuf, CancellationToken ct = default);
       Task SendTextAsync(string toUserId, string contextToken, string text, CancellationToken ct = default);
   }
   ```

3. **`HttpWeChatApiClient.cs`** — 实现上述接口：
   - BaseUrl `https://ilinkai.weixin.qq.com`（参考 vibe-remote wechat_auth.py DEFAULT_BASE_URL）
   - 通用 Headers：`Content-Type: application/json`、`AuthorizationType: ilink_bot_token`、`X-WECHAT-UIN`（随机 uint32 转 base64，每实例生成一次）、`Authorization: Bearer {bot_token}`（bot_token 为空时不加）
   - `GetUpdatesAsync`：POST `/ilink/bot/getupdates`，body 含 `get_updates_buf` + `base_info.channel_version: "kodaclaw"`
   - `SendTextAsync`：POST `/ilink/bot/sendmessage`，`message_type=2`，`message_state=2`，`client_id="kodaclaw-{Guid}"`
   - `GetQrCodeAsync`：GET，无 token
   - `GetQrCodeStatusAsync`：GET，带 `iLink-App-ClientVersion: 1` header，无 token
   - `CheckLoginStatusAsync`：POST `/getLoginStatus`，body `{ "token": "{botToken}" }`，无 auth header
   - `BotToken` 属性可在登录后设置（初始 null）

**验证**：`dotnet build` 0 错（L0）

---

### KC-4603：WeChatConnector + 配套组件

**改动层**：`KodaClaw.ChannelHub/Connectors/WeChat/`

**新建文件**：

1. **`WeChatConnectorOptions.cs`**：
   ```csharp
   public sealed record WeChatConnectorOptions
   {
       public int LruDeduplicationSize { get; init; } = 500;
       public int SessionExpiredRetryDelayMs { get; init; } = 30_000;
       public int ErrorRetryDelayMs { get; init; } = 2_000;
   }
   ```

2. **`WeChatConnectorConfiguration.cs`**：
   - `BotToken`（从 CredentialReference → Keychain 解析，或 ConfigurationJson 直接读取）
   - `StateDir`（游标文件目录，默认 `{workspaceRoot}/state/wechat/{accountId}/`）
   - 静态 `FromAccount(ChannelAccount, ISecretStore, IOptions<WorkspaceOptions>)` 工厂

3. **`WeChatAuthManager.cs`**：
   - `SaveSyncBuf(accountId, syncBuf)` → 写 `{StateDir}/sync_buf.json`
   - `LoadSyncBuf(accountId)` → 读上述文件，不存在返回 `""`
   - `ClearSyncBuf(accountId)` → 删除文件（账号重新登录时调用）

4. **`WeChatConnector.cs`** — 核心实现，`IChannelConnector`：
   - `Kind` = `ChannelConnectorKind.WeChat`
   - `ConcurrentDictionary<string, StartedAccount>` 管理活跃连接
   - **`StartAsync`**：
     - 从 `WeChatConnectorConfiguration.FromAccount` 解析配置
     - 设置 `HttpWeChatApiClient.BotToken`
     - 加载持久化游标 `syncBuf`
     - 启动后台 `PollLoopAsync` Task
   - **`StopAsync`**：取消 CancellationTokenSource，等待 Task 完成
   - **`SendAsync`**：
     - 从 `draft.MetadataJson` 读 `contextToken`
     - 调 `MarkdownToPlainText(draft.MessageText)`（正则去除标记）
     - 调 `_api.SendTextAsync`
   - **`PollLoopAsync`**：
     ```
     while (!ct.IsCancellationRequested)
       response = await GetUpdatesAsync(syncBuf, ct)
       if errcode == -14 → MarkAccountDegraded(); delay 30s; continue
       syncBuf = response.GetUpdatesBuf
       SaveSyncBuf(syncBuf)
       foreach msg in response.Msgs
         if LRU.Contains(msg.MessageId) → skip
         LRU.Add(msg.MessageId)
         foreach item in msg.ItemList where item.Type == 1
           await DispatchTextEventAsync(msg, item.TextItem.Text, onEvent, ct)
     ```
   - **`DispatchTextEventAsync`**：构造 `ChannelEventEnvelope`
     - `EventType = MessageReceived`
     - `ExternalThreadId = msg.FromUserId`
     - `ThreadType = DirectMessage`
     - `Sender = new ChannelIdentity(msg.FromUserId, ...)`
     - `MetadataJson = {"contextToken": "..."}`
   - **`MarkAccountDegraded`**：通过 `IChannelAccountRepository` 将账号 State 置 Degraded

**验证**：`dotnet build` 0 错（L0）

---

### KC-4604：Gateway 与生命周期集成

**改动层**：`KodaClaw.Gateway`、`KodaClaw.ChannelHub`（ServiceCollectionExtensions）

**修改文件**：

1. **`KodaClaw.ChannelHub/ServiceCollectionExtensions.cs`**：
   - 注册 `IWeChatApiClient` → `HttpWeChatApiClient`（Singleton）
   - 注册 `WeChatAuthManager`（Singleton）
   - 注册 `WeChatConnector`（Singleton）

2. **`KodaClaw.Gateway/Channels/ChannelConnectorHostedService.cs`**：
   - `StartAsync`：加 `await StartAccountsByKindAsync(ChannelConnectorKind.WeChat, ct);`
   - `StopAsync`：加 WeChat stop arm
   - `StartAccountByKindAsync` switch：加 `ChannelConnectorKind.WeChat => _inboundGateway.StartWeChatAccountAsync(...)`

3. **`KodaClaw.Gateway/Channels/ChannelInboundGatewayService.cs`**：
   - 加 `StartWeChatAccountAsync(ChannelAccount, CancellationToken)` 方法（与 StartFeishuAccountAsync 同模式）
   - 加 `StopWeChatAccountAsync(string accountId, CancellationToken)` 方法

4. **`KodaClaw.Gateway/Endpoints/GatewayApp.ChannelEndpoints.cs`**：
   - `/api/channels/connectors` 列表加 WeChat 描述符：
     ```csharp
     new { Kind = "WeChat", DisplayName = "微信", Implemented = true,
           SupportsInbound = true, SupportsOutbound = true, ProductOwned = false }
     ```
   - 加 `ReconcileChannelAccountRuntimeAsync` WeChat arm
   - 加端点（新建 partial `GatewayApp.WeChatAuthEndpoints.cs`）：
     - `POST /api/channels/wechat/get-qrcode`
     - `GET /api/channels/wechat/qrcode-status`
     - `POST /api/channels/test-wechat-credentials`

**验证**：`dotnet build` 0 错（L0）；`dotnet test --filter WeChatApiIntegrationTests`（L2）

---

### KC-4605：前端接入

**改动层**：`apps/kodaclaw-web`

**修改 `contracts.ts`**：
```typescript
// ChannelConnectorKind union 加：
| "WeChat"

// 新增：
interface WeChatQrCodeResult {
  qrcode: string;
  qrcodeImgUrl: string;
}
interface WeChatQrCodeStatus {
  status: "wait" | "scaned" | "confirmed" | "expired";
  botToken?: string;
}
```

**修改 `api.ts`**：
```typescript
export async function getWeChatQrCode(): Promise<WeChatQrCodeResult>
export async function pollWeChatQrStatus(qrcode: string): Promise<WeChatQrCodeStatus>
export async function testWeChatCredentials(botToken: string): Promise<{ ok: boolean; errorMessage?: string }>
```

**新建 `WeChatQrLoginPanel.tsx`**：
- Step 1：点击"获取二维码"按钮 → 调 `getWeChatQrCode()`
- Step 2：展示 QR 图片（`<img src={qrcodeImgUrl} />`） + "等待扫码"状态
- 每 2s 轮询 `pollWeChatQrStatus(qrcode)`：
  - `wait` → 继续轮询
  - `scaned` → 提示"已扫码，请在手机上确认"
  - `confirmed` → 停止轮询，回调 `onLoginSuccess(botToken)`，提示"登录成功"
  - `expired` → 提示过期，显示"重新获取"按钮
- 最长 3 分钟自动停止（防止无限轮询）

**修改 `ChannelSetupWizard.tsx`**：
- 连接器选择加 "微信" 选项
- 微信分支：展示 `WeChatQrLoginPanel`，登录成功后进入"交付模式"步骤
- 无需凭证输入步骤（扫码替代）

**修改 `ChannelsDesk.tsx`**：
- WeChat 账号卡片展示 State（Connected / Degraded）
- Degraded 状态显示"重新扫码"按钮（触发 `WeChatQrLoginPanel`）

**验证**：`npm run typecheck` 通过（L0）

---

### KC-4606：测试

**改动层**：测试项目

**新建 `WeChatConnectorConfigurationTests.cs`**（L1，约 8 个）：
- BotToken 从 ConfigurationJson 直读
- BotToken 从 CredentialReference → Keychain 解析
- ConfigurationJson 缺失时安全 fallback
- StateDir 路径拼接规则

**新建 `WeChatApiContractTests.cs`**（L3，约 4 个）：
- `ILinkMessage` JSON 反序列化（snake_case → PascalCase）
- `ILinkGetUpdatesResponse` 反序列化（含 msgs 数组）
- `ILinkQrCodeResponse` 反序列化
- `ILinkSendMessageRequest` 序列化验证

**新建 `WeChatAuthApiIntegrationTests.cs`**（L2，3 个）：
- `POST /api/channels/wechat/get-qrcode` 未认证 → 401
- `POST /api/channels/wechat/get-qrcode` 已认证 → 200，返回 qrcode + qrcodeImgUrl
- `GET /api/channels/wechat/qrcode-status?qrcode=…` → 200，返回 `status: "wait"`

**实际结果**：L1 8 个 + L3 4 个 + L2 3 个 = 15 个新测试全绿；全量回归失败均为预存在问题（与微信无关）

---

## 依赖关系

```
KC-4601（Contracts）
    └─ KC-4602（API Client）← 依赖 DTO
    └─ KC-4603（Connector）← 依赖 API Client + Contracts
    └─ KC-4604（Gateway 集成）← 依赖 KC-4602 + KC-4603
    └─ KC-4605（前端）← 依赖 KC-4604 API shape
KC-4606（测试）← 最后跑
```

实施顺序：KC-4601 → KC-4602 → KC-4603 → KC-4604 → KC-4605 → KC-4606

---

## 非目标（本迭代不做）

- 群聊消息接收与回复
- 图片 / 语音 / 文件 / 视频消息处理（CDN AES 加解密）
- 多账号（UI 层限制单账号，后端架构已支持）
- 主动发消息（Bot 无法主动发起，context_token 必须来自用户消息）
- 正在输入状态（`sendtyping` / `getconfig`）
- `typing_ticket` 支持
- 消息编辑/撤回处理

---

## 验收标准

1. **扫码登录**：Settings → 渠道 → 添加微信 → 二维码展示 → 扫码 → 账号状态 Connected
2. **接收文本**：微信向 Bot 发文字 → KodaClaw Agent 收到并回复 → 微信收到回复
3. **游标持久化**：重启 Gateway 后不重放已处理消息
4. **会话过期**：iLink 返回 errcode -14 → 账号标记 Degraded → UI 提示"重新扫码"
5. **Markdown 剥离**：Agent 回复含 `**bold**` / `## header` → 发出纯文本
6. **向后兼容**：Telegram/飞书账号行为完全不变
7. **L0**：`dotnet build` 0 错 0 警告；`npm run typecheck` 通过
8. **L1/L2/L3**：所有新增测试全绿；全量回归 `dotnet test KodaClaw.sln -m:1` 失败数 0
