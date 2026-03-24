# Iteration 45 FREEZE — 自动化渠道推送（Automation Channel Push）

**冻结日期**：2026-03-23
**范围摘要**：为自动化任务增加声明式渠道推送能力。用户在 HEARTBEAT.md 中通过 `- channels:` 和 `- delivery-mode:` 指定输出渠道与推送策略；调度器执行完成后按策略自动推送结果（Auto）或等待用户在 Inbox 确认后推送（Approval）。同步补全 ChannelsDesk 的 BindingId 复制入口，以及 InboxApprovalDesk 的推送操作按钮。

---

## Iter 44 审查结论

Iter 44（KC-4401~4404）尚未登记为完成，本次 FREEZE 不对 44 作形式审查，直接开启 Iter 45。

---

## 新功能范围

### 背景与动机

自动化任务执行结果目前只进 Inbox，用户必须主动打开 UI 查看。高频/重要自动化（如每日摘要、巡检告警）需要主动推送到 Telegram/飞书，才能真正闭环。本迭代打通"自动化 → 渠道"的声明式路径，同时补全"Inbox → 渠道"的手动推送路径。

**用户 Outcome**：
- 配置一次 HEARTBEAT.md，每次自动化执行完，结果自动出现在 Telegram/飞书群
- 审批类自动化（delivery-mode: approval）：Inbox 中看到结果后点一下"推送"，内容发出

---

## 设计决策记录

| 决策 | 结论 |
|------|------|
| 渠道标识 | BindingId 直接写入 HEARTBEAT.md；ChannelsDesk 补充"复制 BindingId"入口 |
| 依赖隔离 | Contracts 层定义 `IAutomationNotificationService` 接口；ChannelHub 实现；Automation 只依赖接口 |
| 推送失败策略 | 单个渠道失败不阻断其他渠道，失败原因记录到 Inbox PayloadJson |
| 推送内容 | 直接使用 `run.Summary`，不做模板渲染 |
| delivery-mode 缺省 | `none`（不写则不推送，向后兼容所有现有自动化） |

---

## HEARTBEAT.md 语法扩展

```markdown
## Daily Digest
- schedule: daily 09:00
- prompt: 生成每日摘要并汇总今日重点
- model: claude-sonnet-4-20250514        # 可选，已有字段
- channels:
  - tg-main-abc123      # BindingId，从 ChannelsDesk 渠道线程列表复制
  - feishu-ops-xyz456
- delivery-mode: auto   # auto | approval | none（缺省 none）
```

**解析规则**：
- `- channels:` 后接缩进列表，每行一个 BindingId（`-` 前缀 + 空格 + ID）
- `- delivery-mode:` 值大小写不敏感，未识别值视为 `none`
- `- channels:` 出现但列表为空，等同于不配置渠道（不推送）
- `delivery-mode: auto/approval` 但 `channels` 为空，忽略 delivery-mode（不推送）

---

## KC 条目

### KC-4501：Contracts 层扩展

**改动层**：`KodaClaw.Contracts`

- `AutomationDefinition` record 新增两个字段（位于 `ModelId` 之后）：
  ```csharp
  IReadOnlyList<string>? NotificationChannels,  // BindingId 列表，null = 不推送
  AutomationNotifyMode NotifyMode,               // None | Auto | Approval
  ```
- 新增枚举：
  ```csharp
  public enum AutomationNotifyMode { None = 0, Auto = 1, Approval = 2 }
  ```
- 新增 `IAutomationNotificationService` 接口（供 Automation 模块消费，ChannelHub 实现）：
  ```csharp
  public interface IAutomationNotificationService
  {
      Task<IReadOnlyList<ChannelPushResult>> PushAsync(
          IReadOnlyList<string> bindingIds,
          string text,
          CancellationToken cancellationToken = default);
  }

  public sealed record ChannelPushResult(
      string BindingId,
      bool Ok,
      string? ErrorMessage,
      DateTimeOffset? SentAt);
  ```
- 更新所有已有测试中的 `new AutomationDefinition(...)` 调用，补 `NotificationChannels: null, NotifyMode: AutomationNotifyMode.None`

**验证**：`dotnet build` 0 错 0 警告（L0）

---

### KC-4502：HeartbeatAutomationCompiler 扩展

**改动层**：`KodaClaw.Workspace`

- `SectionDraft` 加 `List<string>? NotificationChannels` + `AutomationNotifyMode NotifyMode`
- `ParseSections` 新增两个字段解析块：
  - `- channels:` → 进入"子列表收集模式"，读取后续每个 `  - <id>` 行直到非列表行（同 `inputs:` 解析模式）
  - `- delivery-mode:` → `TryReadFieldValue` 解析，`auto`→`Auto`，`approval`→`Approval`，其他→`None`
- `BuildDefinitions` 构造 `AutomationDefinition` 时传入新字段
- 未知顶级字段仍抛 `HeartbeatCompilationException`

**契约测试**（新增，加入 `HeartbeatAutomationCompilerContractTests.cs`）：
1. `channels` 单条 BindingId → `NotificationChannels` 含该 ID
2. `channels` 多条 → `NotificationChannels` 含所有 ID，顺序保留
3. `delivery-mode: auto` → `NotifyMode = Auto`
4. `delivery-mode: approval` → `NotifyMode = Approval`
5. `delivery-mode: none` → `NotifyMode = None`
6. 无 `channels` 字段 → `NotificationChannels = null`，`NotifyMode = None`
7. `channels:` 后列表为空 → `NotificationChannels = []`（空列表）
8. `delivery-mode` 大小写不敏感（`AUTO` → `Auto`）

**验证**：L1 契约测试全绿

---

### KC-4503：SQLite 存储层迁移

**改动层**：`KodaClaw.Automation`（`SqliteAutomationDatabase` + `SqliteAutomationDefinitionRepository`）

- `SqliteAutomationDatabase`：
  - `CREATE TABLE` DDL 加 `notification_channels TEXT NULL`（JSON 数组）和 `notify_mode INTEGER NOT NULL DEFAULT 0`
  - 两个 idempotent `ALTER TABLE ... ADD COLUMN` 迁移（try/catch `"duplicate column name"`）
- `SqliteAutomationDefinitionRepository`：
  - INSERT：加 `$notificationChannels`（`JsonSerializer.Serialize(list) ?? NULL`）、`$notifyMode`（`(int)mode`）
  - SELECT：读 `notification_channels`（JSON 反序列化）、`notify_mode`（`(AutomationNotifyMode)int`）
  - 列索引全部对齐（`ModelId` 已在 Iter 前一次迭代加入，此次继续追加）
- `SqliteAutomationDefinitionRepositoryTests.cs`：补充 channels/notifyMode 的 round-trip 测试

**验证**：L1 round-trip 测试通过；`dotnet build` 干净

---

### KC-4504：AutomationScheduler 自动推送（Auto 模式）

**改动层**：`KodaClaw.Automation`

- `AutomationScheduler` 构造函数新增可选依赖：`IAutomationNotificationService? notificationService = null`
- `RunSessionCoreAsync` 完成后、`UpsertResultInboxItemAsync` 之前，判断：
  ```
  if (definition.NotifyMode == Auto
      && definition.NotificationChannels is { Count: > 0 }
      && notificationService is not null
      && !string.IsNullOrWhiteSpace(run.Summary))
  {
      pushResults = await notificationService.PushAsync(channels, summary, ct)
  }
  ```
- `UpsertResultInboxItemAsync` 扩展 `PayloadJson`：加入 `channelPushResults` 数组：
  ```json
  {
    "automationId": "...",
    "runId": "...",
    "status": "...",
    "summary": "...",
    "channelPushResults": [
      { "bindingId": "tg-xxx", "ok": true, "sentAt": "..." },
      { "bindingId": "feishu-yyy", "ok": false, "errorMessage": "Binding not found" }
    ]
  }
  ```
- 推送失败不影响 run 最终状态（Success/Failed 由 Agent 执行结果决定）
- `IAutomationNotificationService` 为 null 时静默跳过（服务未注册场景的安全降级）

**集成测试**（`AutomationSchedulerIntegrationTests.cs`，新增）：
1. `NotifyMode=Auto` + 注入 `CapturingNotificationService` → 执行完后 `PushAsync` 被调用，bindingIds 与定义一致
2. `NotifyMode=None` → `PushAsync` 不被调用
3. 推送单个渠道失败 → Inbox PayloadJson 含 `ok: false` + errorMessage，run 状态仍为 Succeeded

**验证**：L2 集成测试全绿

---

### KC-4505：Gateway 手动推送端点（Approval 模式）

**改动层**：`KodaClaw.Gateway` + `KodaClaw.ChannelHub`

**ChannelHub**（`AutomationNotificationService.cs`）：
- 实现 `IAutomationNotificationService`：
  ```csharp
  public sealed class AutomationNotificationService : IAutomationNotificationService
  {
      private readonly IChannelSendService _sendService;
      // 遍历 bindingIds，逐一调用 _sendService.SendAsync，catch 异常 → ChannelPushResult(Ok:false)
  }
  ```
- `ServiceCollectionExtensions` 注册为 scoped

**Gateway**（新文件 `GatewayApp.AutomationNotificationEndpoints.cs`）：
```
POST /api/inbox/{id}/push-to-channel
Body: { bindingIds?: string[] }  // 为空则读取 definition.NotificationChannels
Response: { results: ChannelPushResult[] }
```
- Handler 逻辑：
  1. 读取 InboxItem（404 if not found）
  2. 解析 PayloadJson 取 `automationId`
  3. 读 `AutomationDefinition`（取 `NotificationChannels`）
  4. 合并 request body 中的 `bindingIds`（以 body 优先，body 空则用 definition）
  5. 调 `IAutomationNotificationService.PushAsync`
  6. 更新 InboxItem PayloadJson 中的 `channelPushResults`
  7. 返回结果

**验证**：1 个 L2 集成测试：POST → 200 + results 数组含正确 bindingId

---

### KC-4506：前端扩展

**改动层**：`apps/kodaclaw-web`

**`contracts.ts`**：
```typescript
// AutomationDefinition 加：
notificationChannels?: string[] | null;
notifyMode?: "None" | "Auto" | "Approval";

// 新增：
interface ChannelPushResult {
  bindingId: string;
  ok: boolean;
  errorMessage?: string | null;
  sentAt?: string | null;
}
```

**`api.ts`**：
```typescript
// 新增：
async function pushAutomationResultToChannel(
  inboxId: string,
  bindingIds?: string[]
): Promise<{ results: ChannelPushResult[] }>
```

**AutomationsDesk.tsx**：
- 卡片 meta 行：有 `notificationChannels` 时显示渠道数量 tag（`N 个渠道`）
- 详情面板：`渠道推送` 行，列出 BindingId 列表 + 推送模式标签（Auto/Approval/未配置）

**InboxApprovalDesk.tsx**（AutomationResult 详情）：
- 解析 `selectedItem.payloadJson` 中的 `channelPushResults`
- 有推送结果时：在 summary 下方渲染推送状态列表（每行：BindingId + 成功✓/失败✗ + 时间/错误）
- `NotifyMode = Approval` 且无推送记录时：显示"推送到渠道"按钮，点击调 `pushAutomationResultToChannel`，成功后刷新 Inbox 列表

**ChannelsDesk.tsx**：
- 渠道线程列表每行加"复制 BindingId"图标按钮（`Copy` 图标，点击写 clipboard）

**验证**：`npm run typecheck` 通过；L0 干净

---

### KC-4507：测试补全与 BACKLOG 登记

**改动层**：测试项目

- `HeartbeatAutomationCompilerContractTests.cs`：8 个新测试（KC-4502 列出）
- `AutomationSchedulerIntegrationTests.cs`：3 个新测试（KC-4504 列出）
- `AutomationSchedulerTests.cs`：补 `NotificationChannels: null, NotifyMode: AutomationNotifyMode.None` 到所有已有 `CreateDefinition` 调用
- 全量回归：`dotnet test KodaClaw.sln -m:1` 失败数 0

---

## 依赖关系

```
KC-4501（Contracts 扩展）
    └─ KC-4502（编译器解析）
    └─ KC-4503（存储层）
    └─ KC-4504（调度器推送）← 依赖 KC-4503
    └─ KC-4505（Gateway 端点）← 依赖 KC-4503
    └─ KC-4506（前端）← 依赖 KC-4504 / KC-4505 API shape
KC-4507（测试补全）← 最后跑
```

实施顺序：KC-4501 → KC-4502 + KC-4503 并行 → KC-4504 + KC-4505 并行 → KC-4506 → KC-4507

---

## 非目标（本迭代不做）

- 推送重试机制（失败即记录，不重试）
- 推送历史持久化到独立表（写 PayloadJson 即可）
- 自动化结果模板渲染（直接用 summary 原文）
- HEARTBEAT.md 中渠道的友好名称支持（只用 BindingId）
- Inbox Approval 项的推送功能（仅 AutomationResult 支持）

---

## 验收标准

1. **语法解析**：HEARTBEAT.md 中写 `channels:` + `delivery-mode: auto` 编译成功，`AutomationDefinition.NotificationChannels` 非空
2. **Auto 推送**：调度器执行完毕后，`IAutomationNotificationService.PushAsync` 被调用；Inbox PayloadJson 含 `channelPushResults`
3. **推送失败隔离**：单个 BindingId 无效时，其他渠道正常推送；InboxItem 中对应条目 `ok: false`
4. **Approval 推送**：InboxApprovalDesk 中 AutomationResult 显示"推送到渠道"按钮，点击后渠道收到消息
5. **BindingId 复制**：ChannelsDesk 线程列表可一键复制 BindingId
6. **向后兼容**：无 `channels` / `delivery-mode` 的现有自动化行为完全不变
7. **L0**：`dotnet build` 0 错 0 警告；`npm run typecheck` 通过
8. **L1/L2**：编译器测试 8 个 + 调度器集成测试 3 个全绿
