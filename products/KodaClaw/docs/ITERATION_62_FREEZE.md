# Iteration 62 FREEZE — 全系统观测埋点覆盖

> 冻结日期：2026-03-26
> 迭代类型：新功能（完整 Capability Slice）
> 前置：Iter 61 完成（DiagnosticsQueryTool、CorrelationId 全链路、Session 健康 badge）

## 目标

在 Iter 60/61 建立的诊断基础设施之上，补全系统各模块的观测埋点，使 DiagnosticsDesk 和 `diagnostics_query` 能真正反映系统全貌：

1. **会话生命周期可见** — 主会话/渠道会话/自动化会话的创建、轮转、失败全程可追踪
2. **Connector 状态可见** — Telegram/飞书/微信的启动、停止、连接异常有事件
3. **数据写操作可追溯** — Workspace 文件修改、Canvas 更新、Inbox 创建均有 info 事件
4. **自动化执行可观测** — 运行成功/失败/崩溃/stale 恢复均有事件，调度黑盒消除
5. **错误降级路径可见** — 各模块 catch 块从"只写日志"升级为"同时写诊断事件"

## 非目标

- 不新增任何 API 端点或前端 UI（埋点全在后端）
- 不修改 `DiagnosticEvent` 数据结构
- 不引入新的依赖或 NuGet 包

## EventType 命名规范

```
{module}.{entity}_{action}
```

示例：
- `main_session.created` / `main_session.rotated` / `main_session.load_failed`
- `channel_session.created` / `channel_session.turn_completed`
- `automation.run_succeeded` / `automation.run_crashed`
- `telegram.connection_established` / `feishu.connection_failed`
- `workspace.protocol_updated` / `canvas.artifact_upserted`

Level 规范：
- `info` — 正常操作完成
- `warning` — 降级、超时、重试、跳过
- `error` — 异常捕获、操作失败

---

## 范围

### KC-6201 会话生命周期埋点（Runtime）

**MainSessionService**：
- session 创建：`main_session.created`（info）`"Main session created: sessionId={id}"`
- session 从 store 加载：`main_session.loaded`（info）`"Main session loaded: sessionId={id}"`
- session 轮转：`main_session.rotated`（info）`"Main session rotated: old={oldId} new={newId}"`
- session 加载失败降级新建：`main_session.load_failed`（warning）`"Main session load failed, creating new: {error}"`
- agent 创建失败：`main_session.agent_create_failed`（error）`"Main session agent creation failed: {error}"`
- 审批等待超时：`approval.wait_timeout`（warning）`"Approval wait timed out after {attempts} attempts"`
- 审批分发失败：`approval.dispatch_failed`（error）`"Approval dispatch failed: {error}"`

**ChannelSessionService**：
- session 创建：`channel_session.created`（info）`"Channel session created: bindingId={id}"`
- session 从 store 恢复：`channel_session.resumed`（info）`"Channel session resumed: bindingId={id} sessionId={sid}"`
- session 超时：`channel_session.timeout`（warning）`"Channel session timed out: bindingId={id} inactiveDays={days}"`
- 入站轮次完成：`channel_session.turn_completed`（info）`"Channel turn completed: bindingId={id} stopReason={reason}"`
- 入站轮次失败：`channel_session.turn_failed`（error）`"Channel turn failed: bindingId={id} error={error}"`

**验证命令**：`dotnet test --filter SessionLifecycle`

---

### KC-6202 Connector 生命周期埋点（ChannelHub）

**TelegramConnector**：
- 账户启动成功：`telegram.account_started`（info）
- 连接超时（后台继续重试）：`telegram.connection_timeout`（warning）
- 连接失败（含 error）：`telegram.connection_failed`（error）
- 账户停止：`telegram.account_stopped`（info）
- 消息发送失败：`telegram.send_failed`（error）

**FeishuConnector**：
- 同上，前缀 `feishu.*`

**WeChatConnector**：
- 同上，前缀 `wechat.*`
- 长轮询循环中断：`wechat.polling_interrupted`（warning）

所有 Connector 均通过 `IDiagnosticsService` 注入，已有 DI 基础。

**验证命令**：`dotnet test --filter ConnectorLifecycle`

---

### KC-6203 数据写操作埋点（Runtime）

**WorkspaceProtocolUpdateTool**：
- 更新成功：`workspace.protocol_updated`（info）`"Workspace protocol updated: target={target} section={section}"`
- 更新失败：`workspace.protocol_update_failed`（error）

**WorkspaceMemoryAppendTool**：
- 追加成功：`workspace.memory_appended`（info）`"Memory appended: priority={p}"`
- 追加失败：`workspace.memory_append_failed`（error）

**CanvasUpsertTool**：
- upsert 成功：`canvas.artifact_upserted`（info）`"Canvas artifact upserted: id={id} kind={kind} isNew={isNew}"`
- upsert 失败：`canvas.upsert_failed`（error）

**InboxCreateTool**：
- 创建成功：`inbox.item_created`（info）`"Inbox item created: id={id} requiresAction={ra}"`
- 创建失败：`inbox.create_failed`（error）

所有工具已有 `IDiagnosticsService` 注入模式可参考（`DiagnosticsQueryTool`）。

**验证命令**：`dotnet test --filter WriteOperationDiagnostics`

---

### KC-6204 自动化执行埋点（Automation）

**AutomationScheduler**：
- 运行成功：`automation.run_succeeded`（info）`"Automation run succeeded: id={id} sessionId={sid}"`
- 运行失败（stop reason ≠ success）：`automation.run_failed`（warning）`"Automation run failed: id={id} reason={reason}"`
- 运行崩溃（未捕获异常）：`automation.run_crashed`（error）`"Automation run crashed: id={id} error={error}"`
- stale run 恢复标记失败：`automation.stale_run_recovered`（warning）`"Stale run marked failed: runId={rid}"`
- 调度 tick 概况：`automation.scheduler_tick`（info）`"Scheduler tick: executed={n} total={total}"`
- settings 读取失败：`automation.settings_read_failed`（warning）

**验证命令**：`dotnet test --filter AutomationDiagnostics`

---

### KC-6205 错误降级路径补全（跨模块）

各模块 catch 块目前只写 `ILogger`，补写 `IDiagnosticsService.Record`：

- `SessionRetentionService`：session folder 删除失败 → `session_retention.folder_delete_failed`（warning）
- `McpHubService`：注入完成统计 → `mcp_hub.injection_completed`（info）；已禁用服务器跳过 → `mcp_hub.servers_disabled`（info）
- `MemoryConsolidationService`：post-consolidation 触发 → `memory.post_consolidation_triggered`（info）
- `GenerateImageTool`：下载/存储失败 → `image_generation.store_failed`（error）

**验证命令**：`dotnet build`（纯逻辑，L0 通过即可；关键路径补 L1 测试）

---

### KC-6206 测试与文档

- L1 单元测试：各模块新埋点的关键路径（mock `IDiagnosticsService`，verify `Record` 被调用，检查 EventType/Level）
- 全量回归：`dotnet test KodaClaw.sln -m:1`
- BACKLOG 状态同步

---

## 完成标准

1. DiagnosticsDesk 中，一次完整的自动化执行（触发 → 运行 → 结束）能看到对应事件链
2. 渠道连接器启动时 DiagnosticsDesk 能看到 `telegram.account_started` / `feishu.account_started`
3. Agent 调用 `workspace_protocol_update` 后 DiagnosticsDesk 能看到 `workspace.protocol_updated`
4. 全量测试无回归
