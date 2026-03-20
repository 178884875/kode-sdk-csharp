# ADR-0005：Runtime 审批流采用 live-session continuation

## 状态

- `Accepted`

## 日期

- 2026-03-18

## Implementation Status

- `KC-0203` `Completed`
- `KC-0205` `Completed`
- `KC-0206` `Completed`
- `KC-0207` `Completed`
- `KC-0210` `Completed`
- `KC-0211` `Completed`
- `kodaclaw-web` 主工作台已提供 `Inbox / Approval` 与 `Sessions / Diagnostics` desk，用来消费 live-session approval、session summary 与 diagnostics timeline 合约
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter MainSessionServiceIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter ApprovalContractsTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter ApprovalApiIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter GatewaySessionsIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter DiagnosticsContractsTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`

## 背景

迭代 2 需要把 SDK 原生 tool approval 能力接入 KodaClaw 的控制面，让高风险动作能够在产品层形成 `Approval` / `InboxItem`、被 Gateway 查询，并为后续 `KC-0205` Approval API 提供稳定边界。与此同时，我们必须先确认 SDK 当前在 crash recovery 上的真实能力，避免 KodaClaw 在产品语义层承诺一个实际上无法正确恢复的“跨进程审批续跑”。

经代码级分析与运行时验证，当前 `Kode.Agent.Sdk` 能可靠支持的语义是：

- 同一进程内、同一个 live agent 实例上，`ApproveToolCallAsync(callId)` / `DenyToolCallAsync(callId, note)` 可以唤醒等待中的审批；
- crash-style resume 不会恢复一个正在 `AwaitingApproval` 的 waiter continuation；
- KodaClaw 当前主会话恢复策略使用 `RecoveryStrategy.Crash`，因此恢复后应该显式处理 stale approvals，而不是尝试续跑。

## 决策

我们决定：

- `MainSessionService` 直接订阅 SDK 的 `PermissionRequiredEvent` / `PermissionDecidedEvent`，而不是发明产品层的伪审批协议。
- 当 live session 进入审批时，Runtime 立即把该审批写入 `approvals` 与 `inbox_items`，并在 payload 中持久化 `callId`、tool 名、input preview、permission metadata。
- KodaClaw 采用 deterministic id 规则把 runtime `callId` 映射到控制面记录，便于后续 Gateway Approval API 做 live-agent 路由。
- Approval continuation 只保证在 live session 内成立：批准必须回到同一个 in-memory agent 实例，由 SDK 自身继续执行，不额外调用 `ResumeAsync()`。
- Gateway Approval API 在 live session 仍在时会把拒绝路由回原 agent；如果 live session 已不存在，则 reject 可以直接把 `Pending` approval 终态化为 `Rejected` 并 resolve 对应 inbox，因为该路径不需要恢复 tool continuation。
- 如果主会话以 crash-style resume 恢复，所有仍为 `Pending` 的审批都视为 stale；Runtime 在恢复阶段把它们标记为 `Canceled`，并同步 resolve 对应 inbox item。
- `KC-0206` Sessions API 直接基于 `JsonAgentStore` 的 `meta.json`、messages、tool-call records 提供 `breakpointState`、`pendingApprovalCount`、`pendingApprovalCallIds` 等最小可验证摘要。
- `KC-0207` Diagnostics query API 提供 `/api/diagnostics/recent` 与 `/api/diagnostics/timeline` 两个入口，但共用统一 query contract，以便后续 Web timeline 页面复用。

## 备选方案

### 方案 A：产品层自行维护一套审批 continuation

- 描述：收到 `PermissionRequiredEvent` 后，只把审批写入数据库；批准时由 KodaClaw 重新构造 tool execution continuation。
- 优点：理论上可以做跨进程续跑。
- 缺点：需要复制 SDK 内部状态机与 tool runner 语义，风险高、正确性差，而且与现有 SDK 保存的 breakpoint/tool-call 语义冲突。

### 方案 B：在恢复后调用 `ApproveToolCallAsync()` + `ResumeAsync()` 试图续跑

- 描述：把待审批 callId 从 store 读回，恢复后手工调用 SDK approval/resume API。
- 优点：实现看似简单。
- 缺点：当前 SDK 不会恢复等待中的 approval waiter；`ResumeAsync()` 重新进入 step 时会走 dangling tool-use sealing，不是对原审批点的正确 continuation。

## 后果

- 正向影响：KodaClaw 的审批语义和 SDK 真实能力保持一致；Gateway、Sessions、Diagnostics、后续 Approval API 都围绕同一个 live-session contract 工作。
- 成本：当前产品不承诺“进程重启后继续同一个审批中的 tool call”；Approval API 必须先判断 live session 是否仍在。
- 风险：后续如果要支持跨进程审批续跑，必须先扩展 SDK，而不是在 KodaClaw 侧做状态机补丁。
- 对后续迭代限制：`KC-0205` 必须以 live-agent lookup 为中心；approve 若找不到 live approval，应返回明确错误而不是假装可以继续执行；reject 则只允许把仍为 `Pending` 的审批直接终态化。

## 验证方式

- L2：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter MainSessionServiceIntegrationTests`
- L2：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter ApprovalApiIntegrationTests`
- L2：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter GatewaySessionsIntegrationTests`
- L2：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter GatewayDiagnosticsIntegrationTests`
- L3：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj`
- L4：手动检查 runtime approval 在 `config/control-plane.db` 的 `approvals` / `inbox_items` 写入，以及 session resume 后 stale approval 的取消行为。

## 关联文档

- `docs/ARCHITECTURE.md`
- `docs/CHANNEL_SPEC.md`
- `docs/IMPLEMENTATION_STATUS.md`
- `docs/PARALLEL_EXECUTION_PLAN.md`
- `docs/adr/ADR-0004-control-plane-inbox-baseline.md`

## 关联切片 / PR

- Capability Slice: `KC-0203`
- Capability Slice: `KC-0205`
- Capability Slice: `KC-0206`
- Capability Slice: `KC-0207`
