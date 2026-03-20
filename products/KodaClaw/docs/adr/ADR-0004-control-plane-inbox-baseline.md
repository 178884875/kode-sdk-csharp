# ADR-0004：控制面 SQLite 基线（Inbox / Approval / Settings）

## 状态

- `Accepted`

## 日期

- 2026-03-18

## Implementation Status

- `KC-0201` `Completed`
- `KC-0202` `Completed`
- `KC-0203` `Completed`
- `KC-0209` `Completed`
- `KC-0210` `Completed`
- `KC-0212` `Completed`
- `config/control-plane.db` 现承载 `inbox_items`、`approvals`、`app_settings`、`model_endpoints` 四张控制面基础表
- `MainSessionService` 已开始把 runtime approval linkage 写入 `approvals` / `inbox_items`
- Gateway 已提供 `GET /api/settings` 与 `PUT /api/settings`
- `kodaclaw-web` 主工作台已消费同一控制面数据源，挂载 `Inbox / Approval` 与 `Models / Settings` 两个 desk
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter SettingsApiIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`

## 背景

迭代 2 `Control Plane Beta` 需要一个稳定的控制面契约与存储基线，供 Gateway、Runtime、Diagnostics 和未来 QA 共同验证。迭代 1 的体系已聚焦在 workspace runtime 与 web shell，自带的控制面能力尚未定型；而我们需要一个本地、可审计、可追溯的 SQLite 数据面来承载 Inbox、审批和产品设置的初始数据。这个存储必须和 contracts 一起固定，以便并行 worker（control plane、gateway、runtime、qa）都围绕同一个写集和验证流程展开。

## 决策

我们决定：

- 将模型 `InboxItem` / `InboxItemKind` / `InboxItemStatus` / `InboxQuery` / `IInboxRepository` 固定为迭代 2 的 Inbox contracts。
- 将模型 `Approval` / `ApprovalKind` / `ApprovalStatus` / `ApprovalQuery` / `IApprovalRepository` 固定为迭代 2 的 Approval contracts。
- 将模型 `KodaClawSettings` / `ThemeMode` / `ISettingsRepository` 固定为迭代 2 的 Settings contracts。
- 引入 `SqliteInboxRepository`、`SqliteApprovalRepository`、`SqliteSettingsRepository`，并把它们统一绑定到工作区根目录下的 `config/control-plane.db`（本地 SQLite 文件），作为迭代 2 当前控制面基线。
- 后续 `Diagnostics query`、更完整的 control-plane API 和其他持久化能力，优先在同一数据库文件上追加 schema，而不是引入第二套控制面写集。

该方案适用于 iteration 2 的 control plane 快速收口：contract + storage 同步后，subagents —— gateway、runtime、qa —— 可以独立实现 API/handler，其验证点都围绕同一个工作区 SQLite 文件。

## 备选方案

### 方案 A：继续使用内存仓储

- 描述：先用 in-memory/文件快照存储 Inbox 数据，等 API 完善再换到持久化驱动。
- 优点：启动快、改动少。
- 缺点：无法共享数据、校验 SQL schema、Docker 运行不一致，风险把集成问题留到后面。

### 方案 B：依赖外部服务（如 existing Kode.Agent control plane）

- 描述：调用现有 Kode.Agent 中心控制面接口，避免新存储。
- 优点：不需要额外持久层实现。
- 缺点：与 roadmap 的 `local-first` 和 `inspectable autonomy` 目标矛盾；容易制造跨 module contract 依赖；无便捷方式做 offline diagnostics/drill。

## 后果

- 正向影响：控制面 contracts、存储、验证直接冻结，subagents 可并行实现 API/diagnostics/QA，而不再猜测写集。
- 成本：需要维护多个控制面仓储与共享 `config/control-plane.db` schema，并保证测试覆盖。
- 风险：SQLite schema 变更需要同步迁移策略；数据库文件必须在运行时自动初始化，而不是依赖手工预置。
- 对后续迭代限制：新的控制面持久化能力应优先复用该数据库文件，或明确给出迁移路径。

## 验证方式

- L1：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj`
- L2：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj`
- L3：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- L4：手动检查工作区 `config/control-plane.db` 内容与 `SqliteInboxRepository` schema。

## 关联文档

- `docs/ARCHITECTURE.md`
- `docs/ENGINEERING_PLAYBOOK.md`
- `docs/PARALLEL_EXECUTION_PLAN.md`
- `docs/WORKSPACE_SPEC.md`
- `docs/ITERATION_PLAN.md`

## 关联切片 / PR

- Capability Slice: `KC-0201`
- PR: `Control plane Inbox baseline (KC-0201)`
