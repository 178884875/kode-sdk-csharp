# Iteration 50 FREEZE — Channel DM 主会话等价升级 + Session Reset 命令

**日期**：2026-03-24
**类型**：新功能
**范围**：KodaClaw.Runtime / KodaClaw.ChannelHub

---

## 背景与动机

当前 Channel DM session 被设计为受限渠道 session——上下文加载保守（不含长期记忆、每日记忆）、沙箱隔离到 sessionDirectory、有 7 天 timeout。这个设计在渠道能力初期是合理的保守选择，但与产品目标不符：

> "外部渠道不是通知发送器，而是独立会话入口" — PRODUCT.md

Channel DM 的使用场景是：**用户外出时通过手机端（Telegram/WeChat）与 Koda 交互**。此时：
1. 渠道连接由 owner 主动授权，DM 消息来源等同 owner 本人，信任级别等同主会话
2. 用户不在桌面前，approval 机制在 DM 场景下 UX 悖论——等待桌面审批毫无意义
3. DM 里的 Koda 应该"认识"用户，了解用户画像和行为准则，而不是一个失忆的受限 bot

**Group session 维持现状**——群组成员不受 owner 控制，信任边界不同。

本迭代目标：**让 DM session 在上下文、工具、沙箱、生命周期上全面对齐主会话**，同时为所有渠道类型引入 session reset 命令。

---

## 范围（IN SCOPE）

### 轨道一：DM Session 全量上下文加载

`ChannelSessionService.ResolveEffectiveScope()` 当前对所有渠道统一返回保守 scope，`LoadLongTermMemory` 硬编码为 `false`。

修改后按 ThreadType 分叉：

```csharp
// DM：等同主会话，全量加载
LoadLongTermMemory: isDirectMessage,
LoadUserProfile:    isDirectMessage && policy.LoadUserProfile,

// Group：维持保守策略
```

`LoadContextDocumentsAsync()` 补充 DM 路径：
- 加载 IDENTITY / SOUL / USER / AGENTS / ONTOLOGY（已有）
- 新增加载 `memory/YYYY-MM-DD.md`（今日）和 `memory/{yesterday}.md`（昨日），与 Main session 对齐

### 轨道二：DM Session Timeout 移除

`ChannelSessionOptions.SessionTimeoutDays`（默认 7）目前对所有渠道生效。

修改 `ChannelSessionService.EnsureChannelSessionAsync()` 中的 timeout 判断：

```csharp
// 仅 Group session 受 timeout 约束；DM 不超时，对齐主会话连续性语义
var isSessionTimedOut = binding.ThreadType == ChannelThreadType.Group
    && _options.SessionTimeoutDays > 0
    && binding.LastInboundAt.HasValue
    && binding.LastInboundAt.Value < DateTimeOffset.UtcNow.AddDays(-_options.SessionTimeoutDays);
```

### 轨道三：DM 沙箱与工具升级

**沙箱 WorkingDirectory**：

```csharp
// DM → workspace root（等同主会话）
// Group → sessionDirectory（维持隔离）
WorkingDirectory = isDirectMessage
    ? _workspaceService.RootPath
    : sessionDirectory,
```

**工具列表**：DM session 在 `ChannelSessionOptions.Tools` 基础上追加：
- `workspace_protocol_update`
- `workspace_memory_append`

Group session 工具列表不变（不开放 workspace 写工具）。

### 轨道四：Session Reset 命令拦截

在 `ChannelTurnOrchestrator`（或 `ChannelInboundGatewayService`）入口，消息进 agent 之前做文本匹配：

```csharp
private static readonly HashSet<string> SessionResetCommands =
    new(StringComparer.OrdinalIgnoreCase) { "/new", "/clear", "/reset" };

var trimmed = envelope.Text?.Trim() ?? "";
if (SessionResetCommands.Contains(trimmed))
{
    await _channelSessionService.EvictSessionAsync(binding.SessionId);
    await SendDirectReplyAsync(binding, "已开启新会话。", cancellationToken);
    return; // 不进 agent
}
```

`IChannelSessionService` 新增：
```csharp
Task EvictSessionAsync(string sessionId, CancellationToken cancellationToken = default);
```

`ChannelSessionService` 实现：从 `_agents` 字典移除并 dispose agent，下次 `EnsureChannelSessionAsync` 时自然创建新 session。

**平台覆盖**：Telegram（slash command 原生）和 WeChat（纯文本硬匹配）均走同一逻辑，无平台差异分支。

---

## 非目标（OUT OF SCOPE）

- 不做 Group session 信任升级（Group 成员不受 owner 控制，维持现有限制）
- 不引入 git 作为 workspace 变更审批层（留后续讨论）
- 不改变 approval 机制——DM 本来就不需要 approval，Group 的 approval 逻辑不变
- 不在 DM 里注入 Plugin 工具（Plugin 工具通过 MCP 路径扩展，PluginHost 工具仅主会话）
- 不做 session reset 的 undo 机制
- 不改变 `SUMMARY.md` 的 `LoadRecentThreadSummary` 策略

---

## 关键契约

### EffectivePolicyScope（DM vs Group）

| 字段 | DM（新） | Group（不变） |
|------|---------|-------------|
| `LoadAgents` | `policy.LoadAgents` | `policy.LoadAgents` |
| `LoadIdentity` | `policy.LoadIdentity` | `policy.LoadIdentity` |
| `LoadSoul` | `policy.LoadSoul` | `policy.LoadSoul` |
| `LoadUserProfile` | `policy.LoadUserProfile` | `false` |
| `LoadLongTermMemory` | `true` | `false` |
| `LoadRecentThreadSummary` | `policy.LoadRecentThreadSummary` | `policy.LoadRecentThreadSummary` |

### DM 追加工具

```csharp
// ChannelSessionService，仅 DM
if (binding.ThreadType == ChannelThreadType.DirectMessage)
{
    tools.Add("workspace_protocol_update");
    tools.Add("workspace_memory_append");
}
```

### IChannelSessionService 接口扩展

```csharp
public interface IChannelSessionService
{
    Task<ChannelSessionHandle> EnsureChannelSessionAsync(...);
    Task<ChannelTurnExecutionResult> RunInboundTurnAsync(...);
    Task EvictSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
```

---

## 受影响模块

| 模块 | 变更类型 |
|------|---------|
| `KodaClaw.Runtime` | `ChannelSessionService`（主要）、`ChannelSessionOptions`（轻微）|
| `KodaClaw.ChannelHub` | `ChannelTurnOrchestrator` 或 `ChannelInboundGatewayService`（session reset 拦截）|
| `KodaClaw.Contracts` | `IChannelSessionService` 新增 `EvictSessionAsync` |

---

## 验证矩阵

| 层级 | 验证内容 | 工具 |
|------|---------|------|
| L0 | `dotnet build` 0 错 0 警告；`npm run typecheck` 通过 | dotnet / tsc |
| L1 | `ChannelSessionServiceDmUpgradeTests`：DM scope 含 MEMORY/daily memory；DM timeout 不触发；Group timeout 仍触发；DM WorkingDirectory = workspace root | xUnit |
| L1 | `SessionResetCommandTests`：`/new`、`/clear`、`/reset` 命中并 evict；正常消息不触发；大小写不敏感 | xUnit |
| L2 | `ChannelDmSessionIntegrationTests`：DM session 全量 workspace 上下文注入验证 | WebApplicationFactory |
| L5 | Dogfood：Telegram DM 发消息验证 Koda 有完整人格上下文；发 `/new` 后新会话；旧会话消失 | 真实 Gateway |
