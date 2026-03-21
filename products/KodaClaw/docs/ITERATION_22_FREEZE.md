# Iteration 22 FREEZE：Channel-First 体验补全

## 冻结日期
2026-03-21

## 动机

经过 21 个迭代，KodaClaw 的 Channel 能力后端已完整（Telegram / Webhook 连接器、turn orchestration、delivery governance、approval 闭环），但面向 **Channel-first 用户**（主要通过 Telegram 等外部渠道使用 KodaClaw）的产品体验有七处系统性缺口：

1. **Channel session 无时效策略**：`ChannelSessionService` 总是 resume 旧 session，六个月前的 Telegram thread 会重新加载陈旧的 SUMMARY.md，行为混乱
2. **ChannelsDesk 无实时感知**：新 thread 进来必须手动刷新才能看到，没有 pending approval 提示
3. **渠道账号无法在 Web UI 里管理**：绑定 Telegram bot、配置 delivery rule 必须直接改配置文件，无 onboarding 向导
4. **Approval 处理路径过长**：收到桌面通知后必须打开 Web → 定位 InboxApprovalDesk → 找到对应审批才能操作，无快速决策路径
5. **Automation 无法手动触发**：用户需要等待调度器自动执行才能测试 automation，调试体验差
6. **SUMMARY.md 只做截断不做压缩**：长期 channel thread 的早期上下文被截断丢弃，而非语义压缩保留，Koda 会"失忆"
7. **Canvas artifact 无渲染面板**：`canvas_upsert` 工具已通，但 CanvasDesk 只有 iframe preview，用户无法直接查看 Agent 发布的 Markdown 报告/任务看板

---

## 用户可感知的变化

- Telegram thread 超过 7 天未活动后，下一条消息会开始全新会话，不再"记忆混乱"
- ChannelsDesk 每 30 秒自动刷新，有 pending approval 时页签显示角标
- 在 Web UI 里可以添加 Telegram bot、设置 delivery rule，无需改配置文件
- 桌面通知里直接点击 Approve / Reject，无需打开 Web
- AutomationsDesk 每个 automation 条目旁有"立即执行"按钮
- Koda 对 Telegram 长对话的记忆更完整，不再在消息过多后忘记早期内容
- CanvasDesk 能直接渲染 Markdown 报告，Tasklist、Board 等内容无需依赖 iframe

---

## 范围

### 在范围内

| KC | 描述 | 规模 | 层 |
|----|------|------|----|
| KC-2201 | Channel session 时效策略（超 N 天重置） | M | 后端 |
| KC-2202 | ChannelsDesk 实时刷新 + pending 角标 | S | 前端 |
| KC-2203 | Channel 账号绑定 Web UI（创建/编辑渠道账号） | M | 后端 + 前端 |
| KC-2204 | Approval 桌面快捷操作（通知直接 Approve/Reject） | S | Desktop |
| KC-2205 | Automation 手动触发按钮 | S | 后端 + 前端 |
| KC-2206 | SUMMARY.md 语义压缩（超阈值后 LLM 摘要替代截断） | M | 后端 |
| KC-2207 | Canvas artifact 渲染面板（Markdown 原生渲染） | S | 前端 |

### 不在范围内（本迭代明确不做）

- Channel session 完整上下文重建（SUMMARY.md → 会话历史映射）→ 后续专项
- Plugin channel transport 扩展（http / streamableHttp）→ 后续专项
- ModelHub fallback routing → 后续专项
- Canvas artifact 交互（编辑、导出 PDF 等）→ 后续专项
- Automation `LAST_RUN.md` 跨次状态文件 → 后续专项
- Inbox / Canvas 预加载到 system prompt → 后续专项
- 多 Telegram bot 账号的群聊权限细粒度控制 → 后续专项

---

## 架构决策

### KC-2201：Channel session 时效策略

**问题**：`ChannelSessionService.EnsureChannelSessionAsync` 总是 resume，无过期判断。

**决定**：
- `ChannelSessionOptions` 新增 `SessionTimeoutDays: int = 7`（0 = 永不过期）
- `EnsureChannelSessionAsync` 在 resume 前检查：
  ```
  if binding.LastMessageAt < now - SessionTimeoutDays:
      清除 binding.ActiveSessionId
      → 走新建 session 路径（而非 resume）
  ```
- 过期判断只在 **新消息进入时** 触发（lazy check），不做后台扫描
- `ThreadBinding` 的 `LastMessageAt` 已有，`ThreadBinding.LastMessageAt` 在每次 inbound turn 后由 `ChannelTurnOrchestrator` 更新
- 旧 session store 文件不删除（保留审计），只断开 binding 指针

**配置**：`appsettings.json` `Channels:SessionTimeoutDays`，默认 7

### KC-2202：ChannelsDesk 实时刷新

**决定**：
- `ChannelsDesk` 加 `useInterval(30_000)` 轮询 `/api/channels/accounts` + `/api/channels/threads`（全量刷新，不做 diff）
- 若 `pending_approvals_count > 0` 或 `pending_deliveries_count > 0`，GlobalRail 的 Channels 导航项显示角标（复用现有 `.v2-global-rail__badge` 样式）
- 刷新中显示 `data-testid="channels-refresh-indicator"` 微转圈（与 sessions-desk 模式对齐）
- 不做 SSE channel event stream（后端无此接口，过重）

### KC-2203：Channel 账号绑定 Web UI

**后端新增**：
- `POST /api/channels/accounts`：创建账号，body: `{ connectorKind, displayName, configuration: {...}, deliveryMode }`
- `PATCH /api/channels/accounts/{id}`：更新账号（displayName、deliveryMode、enabled）
- `DELETE /api/channels/accounts/{id}`：删除账号（同时 stop connector）
- 创建/更新后，Gateway 通知 `ChannelConnectorHostedService` 热重载该账号的 connector（start/stop lifecycle）

**前端**：
- ChannelsDesk 右侧 detail panel 加 "Add Channel" 按钮
- 向导步骤：① 选择渠道类型（Telegram / Webhook）→ ② 填写配置（bot token / webhook path）→ ③ 设置 Delivery Rule → ④ 保存
- 已有账号：可 Edit（delivery rule、displayName）和 Enable/Disable
- `data-testid="channel-add-btn"`, `data-testid="channel-config-form"`, `data-testid="channel-delivery-rule-select"`

**Telegram 配置字段**：`botToken`（SecretRef，写入 Keychain）
**Webhook 配置字段**：`webhookPath`（唯一路径后缀），`sharedSecret`（可选）

### KC-2204：Approval 桌面快捷操作

**决定**：macOS 系统通知支持 action buttons（`Notification` API with `actions`），用于 delivery approval。

**桌面通知策略**（仅 `ApprovalKind.ChannelDelivery`）：
- 通知附带两个 action button：`✓ 发送` / `✗ 不发送`
- 点击 action → desktop main process 调用 `POST /api/approvals/{id}/approve` 或 `reject`（使用已有 Keychain token）
- 点击通知主体（非 action button）→ 现有行为不变（deep-link 打开 Web）
- 其他 `ApprovalKind`（PluginAuthorization 等）保持原有跳 Web 行为，不加 action buttons（操作过于重要，不应在通知里轻点）

**Electron 实现**：`notify-approval.ts` helper，`app.on('notification-action')` 监听 macOS notification action reply。

### KC-2205：Automation 手动触发

**后端新增**：
- `POST /api/automations/{id}/trigger`：立即触发一次执行
- 实现：创建 `AutomationRunRecord`（`status=Queued`），`AutomationScheduler` 在下一次 `TickAsync`（或即时调用）时执行
- 返回：`{ ok, runId }`

**前端**：
- AutomationsDesk 每个 automation 条目加 "▶ 立即执行" 按钮（`enabled` 状态时可点击）
- 点击后 optimistic UI：按钮短暂变为 "执行中..."，并刷新 runs 列表
- `data-testid="automation-trigger-{id}"`

### KC-2206：SUMMARY.md 语义压缩

**问题**：当前截断（保留最后 60 行）在消息量大时丢失早期关键上下文。

**决定**：
- 在 `ChannelThreadSummaryWriter.WriteAsync` 中，当截断前行数超过 **80 行** 时，触发语义压缩而非截断：
  1. 取前 N 行（被截断部分）
  2. 调用 `IModelProvider.CompleteAsync` 生成段落摘要（prompt: "以下是一段对话记录，请用 3-5 条 bullet points 总结关键信息和结论，保持简洁"）
  3. 替换为 `## Compressed History (${timestamp})\n${summary}\n\n---\n`
  4. 保留最后 40 行完整记录，合并后写入 SUMMARY.md
- 压缩调用失败时 **回退到截断**（不阻塞 turn）
- 阈值可配置：`ChannelSessionOptions.SummaryCompressionThreshold = 80`，`SummaryCompressionTargetLines = 40`
- 注入 `IModelProvider`（已在 Runtime 注册，`ChannelSessionOptions` 已有 model 字段）

### KC-2207：Canvas artifact 渲染面板

**问题**：CanvasDesk 目前对 `ContentType=markdown` 的 artifact 只用 iframe preview，iframe 需要 Gateway 服务文件，且无法自定义样式。

**决定**：
- `CanvasDesk` detail panel：`contentType === "markdown"` 时，调用 `/api/canvas/{id}/entry` 获取内容，前端用 `marked` 或 `react-markdown` 直接渲染
- `contentType === "html"` 保持 iframe（html artifact 可能有 JS，必须 sandbox）
- 渲染区域加 `data-testid="canvas-artifact-content"` + `data-kind={kind}` 属性
- `kind === "tasklist"` 时，在 rendered markdown 上叠加一层 checkbox 可交互 wrapper（只视觉，不写回文件）
- 依赖：前端加 `react-markdown` + `remark-gfm`（支持 GFM tables、task lists）

---

## 契约变更

### 后端新增端点

```
POST   /api/channels/accounts           → 创建渠道账号
PATCH  /api/channels/accounts/{id}      → 更新渠道账号
DELETE /api/channels/accounts/{id}      → 删除渠道账号

POST   /api/automations/{id}/trigger    → 立即触发一次执行
       返回：{ ok: bool, runId: string }
```

### 后端新增配置字段（非破坏性）

`ChannelSessionOptions` 新增：
```csharp
public int SessionTimeoutDays { get; init; } = 7;
public int SummaryCompressionThreshold { get; init; } = 80;
public int SummaryCompressionTargetLines { get; init; } = 40;
```

### 前端新增 DTO（`contracts.ts`）

```typescript
export interface CreateChannelAccountRequest {
  connectorKind: ChannelConnectorKind;
  displayName: string;
  configuration: Record<string, string>;
  deliveryMode: DeliveryMode;
}

export interface TriggerAutomationResponse {
  ok: boolean;
  runId: string;
}
```

---

## 验收标准

1. Telegram thread 超过 7 天后，新消息触发全新 channel session（不 resume 旧 session），binding.ActiveSessionId 已重置
2. ChannelsDesk 每 30 秒自动刷新，有 pending channel delivery approval 时 GlobalRail Channels 项显示角标
3. 在 ChannelsDesk 通过 Web UI 添加 Telegram bot token，保存后 connector 自动启动（无需重启 Gateway）
4. macOS 桌面通知（ChannelDelivery approval）包含 "发送" / "不发送" action button，点击后直接调用 approve/reject API
5. AutomationsDesk "立即执行" 按钮点击后，`/api/automations/{id}/trigger` 返回 200，runs 列表出现新记录
6. SUMMARY.md 超过 80 行时，写入后文件包含 `## Compressed History` 段落 + 最后 40 行完整记录
7. CanvasDesk 打开一个 `contentType=markdown` 的 artifact 时，内容以 HTML 富文本渲染（而非 iframe）
8. `dotnet test KodaClaw.sln -m:1` 全量通过
9. `cd apps/kodaclaw-web && npm run typecheck && npm test && npm run build`

---

## 关键文件汇总

| 文件 | 操作 |
|------|------|
| `src/KodaClaw.Runtime/ChannelSessionOptions.cs` | 修改（加 SessionTimeoutDays / SummaryCompressionThreshold / TargetLines） |
| `src/KodaClaw.Runtime/ChannelSessionService.cs` | 修改（EnsureChannelSessionAsync 加时效检查） |
| `src/KodaClaw.ChannelHub/ChannelThreadSummaryWriter.cs` | 修改（语义压缩替代截断） |
| `src/KodaClaw.ChannelHub/IChannelConnectorRegistry.cs` | 新增（热重载接口） |
| `src/KodaClaw.Gateway/ChannelConnectorHostedService.cs` | 修改（响应热重载信号） |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.ChannelEndpoints.cs` | 修改（加 POST/PATCH/DELETE accounts） |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.AutomationEndpoints.cs` | 修改（加 POST trigger） |
| `apps/kodaclaw-web/src/components/ChannelsDesk.tsx` | 修改（轮询刷新、角标、账号绑定向导） |
| `apps/kodaclaw-web/src/components/AutomationsDesk.tsx` | 修改（手动触发按钮） |
| `apps/kodaclaw-web/src/components/CanvasDesk.tsx` | 修改（Markdown 渲染面板） |
| `apps/kodaclaw-web/src/types/contracts.ts` | 修改（新增 DTO） |
| `apps/kodaclaw-web/src/lib/api.ts` | 修改（新增 API 调用函数） |
| `apps/kodaclaw-desktop/src/main.ts` | 修改（notification action buttons） |
| `tests/KodaClaw.UnitTests/Runtime/ChannelSessionTimeoutTests.cs` | 新增 |
| `tests/KodaClaw.UnitTests/ChannelHub/ChannelThreadSummaryWriterTests.cs` | 修改（加压缩测试） |
| `tests/KodaClaw.IntegrationTests/Gateway/ChannelAccountApiIntegrationTests.cs` | 新增 |
| `tests/KodaClaw.IntegrationTests/Gateway/AutomationTriggerIntegrationTests.cs` | 新增 |

---

## Wave 计划

| Wave | 内容 |
|------|------|
| Wave 1 | KC-2201（channel session 时效）+ KC-2206（SUMMARY.md 语义压缩）— 后端核心 |
| Wave 2 | KC-2203（渠道账号 Web UI）— 后端 API + 前端向导 |
| Wave 3 | KC-2202（ChannelsDesk 刷新/角标）+ KC-2205（Automation 手动触发）— 前端 |
| Wave 4 | KC-2204（桌面 approval 快捷操作）+ KC-2207（Canvas 渲染面板）— Desktop + 前端收口 |
