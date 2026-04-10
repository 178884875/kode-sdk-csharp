# Iteration 71 FREEZE — Sub-Agent 实时进度可见性

> 冻结日期：2026-04-09
> 状态：FROZEN

---

## 背景与动机

当父 Agent 调用编排工具（`isolate_task` / `ask_specialist` / `pipeline` / `retry_with_reflection` / `validate_and_fix`）时，子 agent 可能运行数十秒甚至更长，期间 Chat UI 只显示一个静态的 `agent_working` 指示器，用户完全不知道子 agent 在做什么。

本迭代通过消费 SDK 已有的 Monitor 频道事件，在 Chat UI 的工具调用行内实时展示子 agent 当前正在使用哪个工具，运行结束后折叠为紧凑摘要。

---

## 目标（用户路径）

1. 用户发消息，父 Agent 决定调用 `isolate_task` → Chat 出现 `agent_working` 行
2. `isolate_task` 子 agent 开始运行 → **`agent_working` 行下方出现子行**："↳ isolate_task · 正在运行..."
3. 子 agent 调用 `fs_read` → 子行更新："↳ isolate_task · 读取文件 (fs_read)"
4. 子 agent 调用 `fs_grep` → 子行更新："↳ isolate_task · 搜索代码 (fs_grep)"
5. `isolate_task` 工具完成 → 子行折叠进 `tool_activity` 卡："↳ isolate_task · 使用了 4 个工具"
6. `pipeline` 场景：子行显示当前阶段名 + 工具："↳ pipeline:gather · 读取文件 (fs_read)"

---

## 范围（IN SCOPE）

### 后端 — `KodaClaw.Runtime`
- `ChatSessionService`：在现有 Progress 流之外，**并行订阅** Agent Monitor 频道，通过 `System.Threading.Channels` fan-in 合并两路流，统一产出 `ChatStreamEvent`
- 消费的 Monitor 事件（共 3 类）：
  - `SubAgentCreatedEvent` → 新事件类型 `subagent_start`
  - `SubAgentToolStartEvent` → 新事件类型 `subagent_working`
  - `SubAgentToolEndEvent` → 新事件类型 `subagent_tool_done`（用于前端精确计数）
  - **不消费** `SubAgentDeltaEvent`（子 agent 研究文本，太噪）
  - **不消费** `SubAgentThinkingEvent`（内部推理，不面向用户）
- `ChatStreamEvent` record 新增字段：`SubAgentId?`、`Label?`、`SubAgentToolName?`

### 契约 — `KodaClaw.Contracts` + `contracts.ts`
- `ChatStreamEvent` 新增可选字段（向后兼容，前端旧字段不变）
- 新 `type` 字符串值：`"subagent_start"`、`"subagent_working"`、`"subagent_tool_done"`

### 前端 — `apps/kodaclaw-web`
- `MessageBubble.tsx`（或其子组件）：
  - 新增两个局部状态：
    - `activeSubAgents: Map<subAgentId, SubAgentStatus>` — 当前活跃子 agent（含 label/toolName/toolCount）
    - `pendingSubAgents: Map<label, SubAgentStatus>` — `subagent_start` 先于 `agent_working` 到达时的暂存队列
  - `subagent_start`：尝试挂载到当前活跃的 `agent_working` 父行（按 sub-label 前缀规则匹配，见决策 2）；无父行时入 pending 队列
  - `subagent_working`：更新对应 `SubAgentId` 的 `toolName`，格式"↳ {label} · {工具友好名} ({toolName})"
  - `subagent_tool_done`：对应 `SubAgentId` 的 `toolCount += 1`，不改变显示文字
  - `tool_activity`：按 sub-label 前缀规则收集对应子行，折叠为"↳ {label} · 使用了 N 个工具"固定显示
- 工具名本地化映射（小表）：`fs_read`→"读取文件"、`fs_grep`→"搜索代码"、`fs_write`→"写入文件"、`bash_run`→"执行命令"等，未知工具名直接展示原名

---

## 非目标（OUT OF SCOPE）

- **不展示子 agent 文本流**（SubAgentDeltaEvent）：研究型工具的中间文本对用户无意义，会产生噪声
- **并行工具**（`parallel_research` / `fan_out_fan_in` / `map_reduce`）：SDK 层已决定不透传事件，本迭代不涉及
- **Monitor 频道其他事件**（ContextCompression、SkillActivated 等）：不在本迭代处理
- **独立的 Sub-Agent 时间线面板**：三号方案，不在本迭代
- **Channel/Automation session**：仅处理 Chat session（`ChatSessionService`），其余 session 不改

---

## 关键设计决策

### 1. Fan-in 用 `System.Threading.Channels` 而非 Rx / merge 库

`System.Threading.Channels` 是 .NET 内置，无需新依赖。两个后台 Task 各自读取一路 IAsyncEnumerable 并写入同一个 `Channel<FanInItem>`，主循环读 `Channel.Reader` 产出 `ChatStreamEvent`。`FanInItem` 是 discriminated union record（`ProgressItem(EventEnvelope)` / `MonitorItem(IAgentEvent)`）。

**后台 Task 退出与取消的责任**：用 `using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` 创建内部 CTS，两个后台 Task 均使用 `linkedCts.Token`。主循环在 `DoneEvent`、`ErrorEvent`、任意异常触发 `yield break` 的 **`finally` 块**中调 `await linkedCts.CancelAsync()`，再 `await Task.WhenAll(progressTask, monitorTask)` 确保两个 Task 干净退出。`yield break` 后迭代器的 dispose 不会自动取消 token，必须由 `finally` 显式处理，否则 Monitor 后台 Task 会悬挂到外层 `cancellationToken` 超时。

### 2. 事件顺序竞态：前端用"待挂载"队列处理

由于 fan-in 合并后无法保证 `subagent_start`（Monitor）与 `agent_working`（Progress）的到达顺序，**前端不依赖顺序**：

- 收到 `subagent_start` 时，若当前没有活跃的 `agent_working` 父行，将其暂存进 `pendingSubAgents: Map<label, SubAgentStatus>`
- 收到 `agent_working(toolName)` 时，检查 `pendingSubAgents` 里是否有匹配的待挂载子行，有则立即渲染
- 正常顺序（`agent_working` 先到）：直接在父行下方插入子行

**sub-label 前缀匹配规则**（贯穿 pending 队列匹配和折叠逻辑）：

```
label 匹配 toolName  ⟺  label === toolName  ||  label.startsWith(toolName + ":")
```

例：`toolName = "retry_with_reflection"` 能匹配 `label = "retry_with_reflection:1/3"`；`toolName = "pipeline"` 能匹配 `label = "pipeline:gather"`。此规则统一用于 pending 队列查找和 `tool_activity` 折叠收集，不允许精确匹配。

### 3. 子行状态存在前端，不存后端

`subagent_start` 携带 `SubAgentId + Label`，`subagent_working` 携带 `SubAgentId + ToolName`。前端用 `Map<subAgentId, SubAgentStatus>` 维护活跃子 agent 状态，`SubAgentStatus` 包含 `{ label, toolName, toolCount }`。后端无状态，只做事件映射。

### 4. pipeline 多 stage：累计折叠，不覆盖

`pipeline` 运行期间会产出多个 `SubAgentId`（每个 stage 一个），label 格式为 `"pipeline:gather"` / `"pipeline:analyze"` 等。前端**不覆盖**已完成的 stage 子行，而是按顺序累加：

```
↳ pipeline:gather  · 读取文件 ✓ (3 tools)
↳ pipeline:analyze · 搜索代码...           ← 当前活跃
```

**stage 完成信号**：SDK 没有显式的"stage 完成"事件，前端用以下规则推断：
- 收到 `subagent_start(label="pipeline:X")` 时，将当前活跃的 `pipeline:*` 子行标记为 ✓（说明前一个 stage 已结束）
- `tool_activity(toolName="pipeline")` 到达时，将最后一个仍活跃的 `pipeline:*` 子行也标记为 ✓，然后折叠所有 `label.startsWith("pipeline:")` 的子行为一条摘要："↳ pipeline · 共 N 个阶段，使用了 M 个工具"（M 为各 stage `toolCount` 累计）

### 5. 工具计数用 ToolEnd 而非 ToolStart

只消费 `SubAgentToolStartEvent` 会导致"已发起但未完成"的调用也被计入（如工具取消）。因此后端**额外消费 `SubAgentToolEndEvent`**，前端收到时对应子行的 `toolCount += 1`。

- 后端：新增第三个事件映射：`SubAgentToolEndEvent` → 新事件类型 `subagent_tool_done`，携带 `SubAgentId`
- 前端：收到 `subagent_tool_done` 时 `toolCount++`，不改变子行显示文字（工具名保持最近一次 `subagent_working` 的值）
- `tool_activity` 折叠时取 `toolCount`（已完成数），而不是 `subagent_working` 的到达次数

`contracts.ts` 同步新增 `"subagent_tool_done"` type。

### 6. Monitor 订阅范围：整个消息轮次，不是整个 session

Progress 订阅是**按消息轮次**开启（每次 `StreamAsync` 调用），Monitor 订阅与之保持一致，在同一次 `StreamAsync` 内开启并在 `DoneEvent`/`ErrorEvent` 后关闭。不在 session 层持久订阅，避免多轮消息间事件交叉。

### 7. 工具名本地化在前端小表维护

不走 i18n 体系，组件内定义 `TOOL_DISPLAY_NAMES` 常量，未知工具名 fallback 原名。

---

## 变更文件清单

| 文件 | 变更 |
|------|------|
| `src/KodaClaw.Runtime/Sessions/ChatSessionService.cs` | 新增 Monitor fan-in；新增 `subagent_start`/`subagent_working`/`subagent_tool_done` 映射 |
| `src/KodaClaw.Contracts/Chat/ChatStreamEvent.cs` | 新增 `SubAgentId?`、`Label?`、`SubAgentToolName?` 字段 |
| `apps/kodaclaw-web/src/types/contracts.ts` | 同步新增字段 + 新 type 值（含 `subagent_tool_done`） |
| `apps/kodaclaw-web/src/components/chat/MessageBubble.tsx` | 子行状态管理（pendingSubAgents + toolCount）+ 渲染逻辑 |
| `apps/kodaclaw-web/src/components/chat/MessageBubble.css` | 子行样式（缩进、`--text-secondary`、`0.85rem`，全量 CSS token） |
| `tests/KodaClaw.IntegrationTests/Sessions/SubAgentProgressTests.cs` | L2 集成测试 |

---

## 验证命令

```bash
# L0
dotnet build KodaClaw.sln
npm run typecheck

# L1/L2
dotnet test KodaClaw.sln -m:1 --filter "SubAgentProgress"

# L5 Dogfood
# 1. 启动 Gateway + Web
# 2. isolate_task 场景：发送"分析整个 src 目录的 API 结构"
#    - 确认：agent_working 行下出现子行，初始显示"↳ isolate_task · 正在运行..."
#    - 确认：子 agent 调工具时子行实时更新工具名（如"读取文件 (fs_read)"）
#    - 确认：isolate_task 完成后子行折叠为"↳ isolate_task · 使用了 N 个工具"（N 为实际完成数）
# 3. pipeline 场景：触发一个多阶段 pipeline 任务
#    - 确认：第 1 阶段子行实时更新工具名
#    - 确认：第 2 阶段开始时第 1 阶段子行出现 ✓ 标记
#    - 确认：pipeline 完成时所有 stage 子行折叠为一条摘要（含总工具数）
# 4. retry_with_reflection 场景（如需）：确认各次重试 label 不同（:1/N、:2/N），父行折叠正确
```
