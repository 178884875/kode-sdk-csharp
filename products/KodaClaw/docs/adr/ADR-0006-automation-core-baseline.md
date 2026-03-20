# ADR-0006：Automation Core 第一波复用共享 contracts 与控制面数据库

## 状态

- `Accepted`

## 日期

- 2026-03-18

## Implementation Status

- `KC-0301` `Completed`
- `KC-0303` `Completed`
- `KC-0304` `Completed`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj --filter FullyQualifiedName~KodaClaw.UnitTests.Automation`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter HeartbeatAutomationCompilerContractTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter AutomationContractsTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter AutomationSessionServiceIntegrationTests`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`

## 背景

Iteration 3 需要先把 automation 的最小骨架落地，给后续 durable scheduler、Inbox 投递与 Canvas 执行结果提供稳定边界。主线程在冻结 contract 时明确了三个要求：

- automation 领域模型必须是跨模块共享 contract，而不是各模块各自维护一套私有 DTO；
- automation store 不能再开第二个产品级数据库，要复用现有工作区 `config/control-plane.db`；
- `HEARTBEAT.md` parser 与 automation runtime 必须围绕同一套路径语义协作，避免 parser 产物与 runtime 读取规则不一致。

## 决策

我们决定：

- `KodaClaw.Contracts` 作为 automation shared contract 的唯一来源，承载 `AutomationDefinition`、`AutomationSchedule`、`AutomationRunRecord` 及相关 enum / repository 接口。
- `KodaClaw.Automation` 复用工作区根目录下的 `config/control-plane.db`，新增 `automation_definitions` 与 `automation_runs` 两张表，并通过 `AddKodaClawAutomation()` 接入产品 DI 组合层。
- `HeartbeatAutomationCompiler` 直接输出共享 `AutomationDefinition` contract，而不是再定义一套 Workspace 私有 automation type。
- `weekdays HH:mm` 语法不扩展新的 schedule kind，而是编译为 `AutomationScheduleKind.Weekly` + 周一到周五的 `DaysOfWeek` 集合，保持 schedule contract 简洁稳定。
- `inputs` 在 parser 阶段规范化为 workspace-relative 路径（例如 `tasks/daily-note.md`），runtime 阶段统一解析到 `workspace/` 目录下；为了兼容调试/旧测试，runtime 仍接受显式 `workspace/...` 前缀。
- `AutomationSessionService` 使用 `SessionKind.Automation`，最小上下文固定加载 `AGENTS.md`、`IDENTITY.md`、`SOUL.md`、`USER.md`、`HEARTBEAT.md`，并追加 `InputPaths` 引用文件。

## 备选方案

### 方案 A：Workspace / Runtime / Automation 各自维护私有 automation DTO

- 描述：parser 输出 Workspace 私有类型，runtime 再定义自己的 session definition，store 再做一次持久化映射。
- 优点：每个模块可以独立前进，短期实现速度快。
- 缺点：contract 漂移风险高，测试 fixture 容易互相矛盾，后续 scheduler / web 页面会承受更多映射成本。

### 方案 B：单独建立 automation.db

- 描述：给 automation 开一个新的 SQLite 文件，和控制面数据库完全隔离。
- 优点：表结构更独立，迁移思路直观。
- 缺点：会把工作区状态拆到多个产品级数据库里，增加备份、恢复、调试与跨域查询成本，也与现有 control-plane 模块的工作区存储策略不一致。

## 后果

- 正向影响：parser、runtime、store、未来 API/Web 页面共享同一套 automation contract，后续 `KC-0302` / `KC-0305` 可以在稳定边界上继续推进。
- 正向影响：工作区只维护一份产品级 SQLite 数据库，automation / control-plane / model registry 统一落在同一存储根下，便于备份与诊断。
- 成本：`AutomationSchedule` 需要通过 `DaysOfWeek` 表达工作日语义，调用方不能依赖单独的 `Weekdays` enum 值。
- 成本：runtime 必须明确区分“工作区根路径”和“workspace 内容目录”，否则规范化的 `InputPaths` 会被解析错位。
- 对后续迭代限制：`KC-0302` durable scheduler 必须直接消费 shared contract + repositories；`KC-0308` automation web 页面也应遵循同一序列化格式。

## 验证方式

- L2：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj --filter FullyQualifiedName~KodaClaw.UnitTests.Automation`
- L2：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter HeartbeatAutomationCompilerContractTests`
- L2：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter AutomationContractsTests`
- L2：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter AutomationSessionServiceIntegrationTests`
- L3：`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- L4：手动检查工作区 `config/control-plane.db` 是否出现 `automation_definitions` / `automation_runs` 表，并确认 runtime 对 `tasks/...` 这类 workspace-relative 输入的实际加载结果。

## 关联文档

- `docs/ARCHITECTURE.md`
- `docs/WORKSPACE_SPEC.md`
- `docs/IMPLEMENTATION_STATUS.md`
- `docs/PARALLEL_EXECUTION_PLAN.md`
- `docs/adr/ADR-0004-control-plane-inbox-baseline.md`
- `docs/adr/ADR-0005-runtime-approval-flow.md`

## 关联切片 / PR

- Capability Slice: `KC-0301`
- Capability Slice: `KC-0303`
- Capability Slice: `KC-0304`
