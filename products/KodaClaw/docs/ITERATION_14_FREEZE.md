# Iteration 14 FREEZE：Automation Live + Workspace Context Depth

**冻结日期**：2026-03-20
**主题**：纯后端。激活自动化调度器、新增按需 workspace 读取工具、为 channel 会话留下上下文痕迹。

---

## 一句话目标

让 HEARTBEAT.md 定义的自动化真正运行起来，同时给 Agent 补上按需读取 workspace 和更新 channel 线程摘要的能力。

---

## In-scope

| KC | 描述 | 规模 |
|----|------|------|
| KC-1401 | `workspace_read` 工具——Agent 按需读取 workspace 协议文件 | XS |
| KC-1402 | 自动化调度器激活开关——`KodaClawSettings.AutomationsEnabled` + Gateway 默认启用 | S |
| KC-1403 | Channel thread SUMMARY.md 写回——turn 完成后更新 `workspace/channels/<id>/SUMMARY.md` | XS |

---

## Out-of-scope（本迭代明确不做）

- 前端改动（页面将整体重构，本迭代冻结所有前端变更）
- 会话结束后的自动记忆摘要（需要额外 model call，留后续迭代）
- 自动化日志查看 UI
- 新的 channel connector 类型
- `workspace_read` 以外的 workspace 写入扩展

---

## 架构决策

### KC-1401：workspace_read
- 放在 `KodaClaw.Runtime`，与 `WorkspaceProtocolUpdateTool` 同模块
- targets 与 `WorkspaceProtocolUpdateTool.TargetFileMap` 保持一致，额外加 `daily_memory`（动态路径 `workspace/memory/YYYY-MM-DD.md`）
- 返回 `{ target, path, content }` 或 `{ target, path, exists: false }`
- 注册到 `MainSessionOptions.DefaultTools`（automation sessions 自动复用）

### KC-1402：调度器激活
- 历史原因：`AutomationSchedulerOptions.Enabled = false` 是设计上的保守默认，避免无 API Key 时频繁失败
- 解决方案：Gateway 注册时启用 HostedService（`Enabled = true`），同时新增 `KodaClawSettings.AutomationsEnabled: bool = false`（用户级开关）
- `AutomationScheduler.TickAsync()` 在 `_options.Enabled` 检查之后，再通过 `ISettingsRepository` 检查 `AutomationsEnabled`；任一为 false → no-op tick
- 这样：个人工作流的第一条自动化规则需要用户在设置页（或 API）手动启用 `AutomationsEnabled`，而不是静默开始消耗 API token
- `KodaClawSettings` 是 JSON record，新增字段向后兼容（已有 DB 行缺失时反序列化取 C# 默认值 `false`）

### KC-1403：Channel Thread Summary
- 新服务 `IChannelThreadSummaryWriter`，放在 `KodaClaw.ChannelHub`
- `ChannelTurnOrchestrator` 注入为可选依赖（null-safe），在 `ProcessInboundAsync` 返回前调用
- 触发条件：`ExecutedTurn == true && outcome.Kind != Failed && outcome.Kind != NoAction`
- 文件路径：`{workspaceRoot}/workspace/channels/{bindingId}/SUMMARY.md`
- 内容格式：追加条目，不替换全文（channel 历史积累）
- 不做 model call，只写结构化纯文本

---

## 验证命令

```bash
# L0
dotnet build KodaClaw.sln

# L1
dotnet test tests/KodaClaw.UnitTests --filter "WorkspaceRead|AutomationScheduler|ChannelThread"

# L2
dotnet test tests/KodaClaw.IntegrationTests --filter "WorkspaceRead|AutomationScheduler|ChannelThread"

# L3
dotnet test tests/KodaClaw.ContractTests --filter "WorkspaceTemplate|Settings"

# 全量
dotnet test KodaClaw.sln -m:1
```

---

## Wave 计划

| Wave | 内容 |
|------|------|
| Wave 1 | KC-1401 workspace_read 工具 + L1/L2 测试 |
| Wave 2 | KC-1402 AutomationsEnabled + 调度器动态检查 + L1/L2/L3 测试 |
| Wave 3 | KC-1403 Channel thread summary writer + L1/L2 测试 + 全量回归 + BACKLOG 更新 + commit |
