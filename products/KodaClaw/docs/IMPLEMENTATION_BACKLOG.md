# KodaClaw 实施 Backlog

这份 backlog 按模块拆解，为后续逐步实现提供任务地图。这里不追求一次性列完所有技术细节，而是给出足够清晰的开发切入口。

## Epic A：产品工程初始化

- 建立 `products/KodaClaw/` 独立目录与命名空间
- 建立独立 `KodaClaw.sln`
- 配置公共 `Directory.Build.props`
- 定义共享 contracts 与基础包引用
- 建立文档与 ADR 目录

## Epic B：Workspace 引导与协议

- 实现 `~/.kodaclaw` 初始化器
- 生成默认 `AGENTS.md` / `IDENTITY.md` / `SOUL.md`
- 生成 `BOOTSTRAP.md` 与首次启动标记
- 定义主会话 / 渠道会话 / 自动化会话的加载规则
- 定义 `MEMORY.md` 与日记式 memory 写入策略
- 定义 `HEARTBEAT.md` 到 automation 的映射规则

## Epic C：Gateway 基础设施

- 建立本地 loopback-only ASP.NET Core 服务
- 增加 token 认证
- 增加健康检查与版本接口
- 增加 chat / sessions / approvals API 框架
- 增加 SSE 输出通道
- 增加统一错误模型
- `2026-03-20` 维护性重构进度：Gateway `Program.cs` Wave 0 ~ Wave 4 已全部收口，`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/Program.cs` 现仅 45 行；models/settings/automations/plugins/canvas/approvals/sessions/inbox/channels/system/chat/diagnostics/root endpoints 均已迁入独立 partial，validation / mapping / infrastructure helpers 也已分层落位，并通过 Gateway 定向集成回归与最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 验证。

## Epic D：Runtime 组合层

- 实现 `SessionKind` 与 Agent 工厂
- 复用 SDK 的 `JsonAgentStore`
- 按 session type 注入不同 Workspace 上下文
- 把 SDK event stream 映射为产品层 timeline event
- 把审批状态映射到 Control Plane
- 加入 session pool 与恢复策略

## Epic E：Control Plane

- 定义 InboxItem、Approval、DiagnosticEvent 数据模型
- 实现审批仓储与 API
- 实现 Inbox API
- 实现 Diagnostics timeline 查询
- 实现 settings API
- 实现 session 详情 API

## Epic F：Web 控制台

- 搭建基础路由与壳布局
- 聊天页
- Inbox 页
- Sessions 页
- Workspace 浏览页
- Models / Plugins / Channels / Automations 页面骨架
- Diagnostics 页面
- `2026-03-20` 前端两轮收口已完成：`kodaclaw-web` 已切到新的 shell / workbench 视觉体系，默认语言切到 `zh-CN`，支持运行时切换 `en-US`，并已通过 `npm run build && npm run test && npm run test:e2e` 验证。

## Web V2：2026-03-20 Live Dogfood 收口

- `KC-W2-001`：`Completed`。Canvas 默认预览与选中 artifact 预览已切到 Gateway 签发的 path-based preview URL：`/api/canvas/default` 与新增的 `/api/canvas/{id}/entry` 现在都会返回可直接供 iframe 使用的 `entryUrl`，并通过 `CanvasApiIntegrationTests`、`src/__tests__/canvas-desk.spec.tsx`、`npm run typecheck` 与 `npm run build` 验证；`tests/kc0309-canvas.spec.ts` 已改为在当前 app shell 尚未把 `CanvasDesk` 接到 live canvas surface 时条件跳过，避免把壳层接线缺口误判为 preview 回归。
- `KC-W2-002`：`Completed`。SDK `Agent.Send()` 后台处理路径在 chat completed 后现会把最终 `BreakpointState.Ready` 持久化回 store，`/api/sessions` 不再残留 `StreamingModel`；已通过新增 runtime/gateway 回归测试与定向 integration suite 验证。
- `KC-W2-003`：`Completed`。已在现有 `kodaclaw-web` 内落地 `shell-shared/`、`shell-v1/` 与 `shell-v2/` 结构，并通过默认 `shell-v2` + `?shell=v1` / `localStorage["kodaclaw.shellVariant"]` override 接入新的三段式壳层，不新建第二套 web app；`App.tsx` 现会复用同一份 `workbench` / `contextPanel` view-model，在 `LegacyShell` 与 `V2Shell` 间切换，同时保持 `desk-tab-*` test ids 稳定。已通过 `npm run typecheck`、`npm run test -- src/__tests__/chat-context-rail.spec.tsx src/__tests__/sessions-diagnostics-desk.spec.tsx src/__tests__/app-shell.spec.tsx` 与 `npm run build` 验证。
- `KC-W2-004`：`In Progress`。V2 chat 路径继续收口为 conversation-first：`ContextRail` 在 `mainDesk === "chat"` 时会切到 session pulse 面板，加载 `/api/sessions`、高亮当前主会话，并把最近轨迹保持在聊天主舞台旁边；`MainStage` 也已切到 chat-specific 紧凑头部，把 active session / gateway / health / workspace 收敛为 dense stage chips，并把 timeline + composer 拉伸成更像 operator canvas 的主舞台。当前又补上了一轮视觉校准：chat 模式下 context hero 不再重复放大 `对话航道` 标题，而改为更轻的 `会话脉络` 辅助标题；shell spacing / radius / shadow / card density 已整体收紧；同时移动端顺序也已修正为 `GlobalRail -> MainStage -> ContextRail`，避免聊天主舞台在窄屏被上下文支持区压到首屏以下。当前切片已通过 `npm run typecheck`、`npm run test -- src/__tests__/chat-context-rail.spec.tsx src/__tests__/sessions-diagnostics-desk.spec.tsx src/__tests__/app-shell.spec.tsx`、`npm run build`、`npm run test:e2e -- tests/kc0213-control-plane-acceptance.spec.ts`，并通过 `agent-browser` 完成桌面/移动端布局复核；后续仍需继续让主舞台减少旧 page-like 痕迹，并决定下一个优先迁移成 list-detail pane 的 desk。
- `KC-W2-005`：`In Progress`。Pane 迁移已扩展到前两块高频 desk。`SessionsDiagnosticsDesk` 现为 session rail + focused detail stage + timeline/export sibling panels，并补上 selection hydration guard，切换 session 时不再先闪出旧 summary / timeline；`InboxApprovalDesk` 也已改成左侧 stacked inbox/approval queues、右侧 inbox focus / approval focus 双 detail panel，并在存在关联关系时联动选中对象，同时保留 `approval-approve` / `approval-reject` / `inbox-status-*` 等稳定 test ids。当前切片已通过 `npm run typecheck`、`npm run test -- src/__tests__/inbox-approval-desk.spec.tsx src/__tests__/chat-context-rail.spec.tsx src/__tests__/sessions-diagnostics-desk.spec.tsx src/__tests__/app-shell.spec.tsx`、`npm run build` 与 `npm run test:e2e -- tests/kc0210-inbox-approval.spec.ts tests/kc0213-control-plane-acceptance.spec.ts` 验证。
- `KC-W2-006`：`In Progress`。`ModelsSettingsDesk` 已完成第一刀 pane 化：左侧 `models-list` 保持 `li` 结构与现有 `model-default` / `model-delete-*` selectors，不破坏现有 Playwright 依赖；右侧则新增 focused model detail/composer stage，默认会自动聚焦 default/first endpoint，并补上 `settings-sections` stage index 指向 runtime/update/risk panels。为避免一次性打碎现有验收流，`settings-form`、`settings-update-watch` 与 `settings-risk-briefing` 目前仍全部挂载可见，但已经收进统一的右侧 stage 栈。当前切片已通过 `npm run typecheck`、`npm run test -- src/__tests__/models-settings-desk.spec.tsx src/__tests__/inbox-approval-desk.spec.tsx src/__tests__/sessions-diagnostics-desk.spec.tsx src/__tests__/chat-context-rail.spec.tsx src/__tests__/app-shell.spec.tsx`、`npm run build` 与 `npm run test:e2e -- tests/kc0212-models-settings.spec.ts tests/kc0213-control-plane-acceptance.spec.ts` 验证。
- 下一步：继续推进 `KC-W2-004` 的 chat 主舞台收口，并为 `KC-W2-007` 选择下一个最值得 pane 化的 desk（优先 `Channels / Plugins / Automations / Canvas` 中的高频面）。

## Epic G：Model Hub

- provider registry 数据模型
- custom endpoint CRUD
- default model policy
- primary / fallback route
- model capability 标签
- endpoint health check

## Epic H：Automation Engine

- automation definition 数据模型
- durable scheduler
- run history
- retry policy
- Inbox result 投递
- `HEARTBEAT.md` 编译流程

## Epic I：Canvas

- Canvas artifact 数据模型
- Canvas 页面注册
- artifact 与 thread/task 绑定
- index / state 管理
- Web UI 嵌入式渲染

## Epic J：Plugin Host

- plugin manifest loader
- plugin registry
- plugin state machine
- stdio / HTTP MCP 启动器
- plugin log capture
- plugin permissions UI

## Epic K：Channel Hub

- channel connector 抽象
- Telegram connector
- generic webhook connector
- thread binding
- delivery rule
- channel policy
- inbound/outbound 审计

## Epic L：Desktop Shell

- Electron 外壳工程
- tray
- notification bridge
- deep-link 到 chat / inbox / canvas
- gateway 启停协调
- 自动升级方案预研

## Epic M：Security 与稳定性

- Keychain 集成
- device identity 完整化
- backup / restore
- crash recovery
- plugin trust model
- sandbox policy UI 提示

## 第一批推荐启动任务

按实施顺序，建议先做下面 12 个任务：

1. 建立 `KodaClaw.sln` 与核心项目骨架 `Completed`
2. 实现 `KodaClaw.Contracts` `Completed`
3. 实现 `KodaClaw.Workspace` 初始化器 `Completed`
4. 实现 `KodaClaw.Gateway` 最小服务 `Completed`
5. 实现 `KodaClaw.Runtime` 的 `main` session `Completed`
6. 实现 `chat` SSE API `Completed`
7. 实现 `kodaclaw-web` 的聊天页 `Completed`
8. 接入 session 恢复 `Completed`
9. 实现 `BOOTSTRAP.md` 驱动的首次启动流程 `Completed`
10. 实现 diagnostics 基线 `Completed`
11. 实现迭代 1 端到端验收包 `Completed`
12. 实现 Control Plane Inbox 基线 `Completed` *(KC-0201: Inbox contracts + `SqliteInboxRepository` + workspace-local `config/control-plane.db`)*

## 迭代 2：Control Plane Beta

- KC-0201：`Completed`。`InboxItem` / `InboxItemKind` / `InboxItemStatus` / `InboxQuery` / `IInboxRepository` 已落地，`SqliteInboxRepository` 在工作区根目录下初始化 `config/control-plane.db`，并通过 unit / integration / solution 三级验证。
- KC-0202：`Completed`。`Approval` / `ApprovalKind` / `ApprovalStatus` / `ApprovalQuery` / `IApprovalRepository` 已落地，`SqliteApprovalRepository` 复用工作区 `config/control-plane.db`，支持 `Pending -> Approved/Rejected/Canceled` 的状态流转规则，并通过 unit / integration / solution 三级验证。
- KC-0203：`Completed`。`MainSessionService` 已接入 SDK 原生 approval flow：高风险 tool 会落 `Approval` + `InboxItem`，live session 内通过同一 agent 实例继续执行；在 crash-style resume 后，未完成审批会被标记为 stale/canceled，并通过 runtime integration + solution 验证。
- KC-0204：`Completed`。Gateway 已提供 `/api/inbox`、`/api/inbox/{id}`、`PATCH /api/inbox/{id}/status`，支持过滤、详情与状态更新，并通过 contract / integration / solution 三级验证。
- KC-0205：`Completed`。Gateway 已提供 `/api/approvals`、`/api/approvals/{id}`、`POST /api/approvals/{id}/approve`、`POST /api/approvals/{id}/reject`；其中 approve 依赖 live session continuation，reject 在 live session 已丢失时可直接终态化 pending approval，并通过 contract / integration / solution 三级验证。
- KC-0206：`Completed`。Gateway 已提供 `/api/sessions`、`/api/sessions/{id}`，基于 `JsonAgentStore` 的 `meta.json` / messages / tool-calls 组装 session summary/detail，并通过 integration / solution 验证。
- KC-0207：`Completed`。Gateway 已提供 `/api/diagnostics/recent` 与 `/api/diagnostics/timeline` 的统一查询管道，支持 `correlationId` / `sessionId` / `source` / `eventType` / `level` / `limit` 过滤，并通过 unit / contract / integration / solution 验证。
- KC-0208：`Completed`。`ModelProviderKind` / `ModelEndpoint` / `CreateModelEndpointRequest` / `UpdateModelEndpointRequest` / `ModelsQueryResponse` / `IModelRegistryRepository` 已落地，`SqliteModelRegistryRepository` 复用工作区 `config/control-plane.db` 持久化 `model_endpoints`，Gateway 已提供 `/api/models`、`/api/models/{id}`、`POST /api/models`、`PUT /api/models/{id}`、`DELETE /api/models/{id}`、`POST /api/models/{id}/default`，并通过 contract / unit / integration / solution + web baseline 全量验证。
- KC-0209：`Completed`。`KodaClawSettings` / `ThemeMode` / `ISettingsRepository` 已落地，`SqliteSettingsRepository` 复用工作区 `config/control-plane.db`，提供默认快照加载、覆盖保存与 quiet-hours 校验，并通过 unit / integration / solution 三级验证。
- KC-0210：`Completed`。`kodaclaw-web` 主工作台已集成 `InboxApprovalDesk`，提供 inbox/approval 双列表、状态筛选、approve/reject 决策与回流刷新，且通过 component tests + `tests/kc0210-inbox-approval.spec.ts` + 全量 `npm run test:e2e` 验证。
- KC-0211：`Completed`。`kodaclaw-web` 主工作台已集成 `SessionsDiagnosticsDesk`，支持 session 切换、detail / diagnostics timeline 联动和刷新，并通过 component tests + `tests/kc0211-sessions-diagnostics.spec.ts` + 全量 `npm run test:e2e` 验证。
- KC-0212：`Completed`。`kodaclaw-web` 主工作台已集成 `ModelsSettingsDesk`，支持模型端点 CRUD / default 切换与工作区设置保存；主线程补齐了 `GET /api/settings` / `PUT /api/settings` Gateway API，并通过 `SettingsApiIntegrationTests` + component tests + `tests/kc0212-models-settings.spec.ts` + 全量 solution/web 验证。
- KC-0213：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration2AcceptanceIntegrationTests.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/tests/kc0213-control-plane-acceptance.spec.ts`，把 approval pause/decide、inbox 回流、sessions/diagnostics 审计、settings 保存串成 Iteration 2 验收基线，并补充 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_2_ACCEPTANCE_PACK.md` 文档；已通过定向 + 全量 solution/web 回归。
- `KC-0301`：`Completed`。`AutomationDefinition` / `AutomationSchedule` / `AutomationRunRecord` / `IAutomationDefinitionRepository` / `IAutomationRunRepository` 已落地，`KodaClaw.Automation` 复用工作区 `config/control-plane.db` 持久化 `automation_definitions` 与 `automation_runs`，并通过 automation unit + contract + solution 三级验证。
- `KC-0302`：`Completed`。`KodaClaw.Automation` 已落地可控时钟与 durable scheduler 基线：默认禁用的 hosted scheduler、`RunOnceAsync` / `TickAsync` 手动入口、`FakeAutomationClock`、stale `Queued/Running` run 恢复失败与 `FailureRetryDelay` 重试语义，并通过 scheduler integration + solution 回归验证。
- `KC-0303`：`Completed`。`HeartbeatAutomationCompiler` 已落地，支持冻结 schedule 文法（`hourly 2h` / `daily 09:00` / `weekdays 09:00` / `weekly mon,wed,fri 18:30`），输出共享 `AutomationDefinition` contract，生成 deterministic id，规范化 workspace-relative `inputs` 并拒绝 path traversal，通过 parser contract tests + solution 验证。
- `KC-0304`：`Completed`。`AutomationSessionService` 已落地，提供 `SessionKind.Automation` 会话句柄、最小上下文加载（`AGENTS.md` / `IDENTITY.md` / `SOUL.md` / `USER.md` / `HEARTBEAT.md` + `InputPaths`），并支持把规范化相对路径解析到 `workspace/` 下，通过 runtime integration + solution 验证。
- `KC-0305`：`Completed`。Automation 调度完成后会稳定 upsert `InboxItemKind.AutomationResult`，固定 id 规则 `automation-result-<runId>`、来源 `automation.scheduler`、路由 `/automations/<automationId>`，并在 payload 中携带 `automationId` / `runId` / `status` / `summary` / `errorMessage`，通过 scheduler integration + solution 验证。
- `KC-0306`：`Completed`。`CanvasArtifactKind` / `CanvasArtifact` / `CanvasArtifactQuery` / `ICanvasArtifactRepository` 已落地，`SqliteCanvasArtifactRepository` 复用工作区 `config/control-plane.db` 初始化 `canvas_artifacts` 表，支持最小 CRUD/List 过滤，并强制 `EntryPath` / `AssetDirectory` 为 `workspace/canvas` 下的 workspace-relative 路径；已通过 canvas repo unit tests + solution 回归验证。
- `KC-0307`：`Completed`。Gateway 已提供 `/api/automations`、`/api/automations/{id}`、`/api/automations/{id}/runs`、`PATCH /api/automations/{id}`、`/api/canvas`、`/api/canvas/default`、`/api/canvas/{id}`、`POST /api/canvas`、`/api/canvas/fs/{**path}`，并通过新增 contract + integration tests、Iteration 3 smoke 与 solution 回归验证。
- `KC-0308`：`Completed`。`kodaclaw-web` 主工作台已集成 `AutomationsDesk`，支持列表、过滤、详情、recent runs、enable/disable、refresh，并通过 component tests + `tests/kc0308-automations.spec.ts` + 全量 `npm run test:e2e` 验证。
- `KC-0309`：`Completed`。`kodaclaw-web` 主工作台已集成 `CanvasDesk`，支持 artifact 列表/过滤、default entry、iframe 预览、metadata 侧栏与空状态 fallback，并通过 component tests + `tests/kc0309-canvas.spec.ts` + 全量 `npm run test:e2e` 验证。
- `KC-0310`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration3AcceptanceIntegrationTests.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_3_ACCEPTANCE_PACK.md`，把 heartbeat -> automation run -> inbox result -> canvas publish/query/fs 串成 Iteration 3 验收基线，并通过定向 + 全量 solution/web 回归。
- 下一波：转入 Iteration 4 规划，优先冻结 plugin/channel/desktop 的 contract 与写集，再决定新的并行波次。

## 迭代 4：Plugin Platform（Wave 0/1/2/3/4 已完成）

- Wave 0：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_4_FREEZE.md` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/adr/ADR-0008-plugin-platform-v1.md`，冻结了 Iteration 4 的范围、contract surface、验收门槛与 subagent 写集。
- 范围冻结：Iteration 4 v1 只要求 `tool` 插件跑通完整运行链路；`channel` / `memory` / `ui` 类型本期只要求 manifest 可识别、registry/Web 可展示，不进入 host/runtime 验收主路径。
- 传输冻结：首期只强制 `stdio` MCP transport；`http` / `streamableHttp` / `sse` 允许保留类型定义，但不计入本期退出标准。
- 授权冻结：插件首次 trust 统一复用 `ApprovalKind.PluginAuthorization` + `InboxItemKind.PluginRequest`，不新起单独插件审批系统。
- Wave 1：`Completed`。主线程先落 shared plugin contracts / test project references，再通过 subagents 并行完成 manifest + permissions + registry 三块实现，最终 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 全绿收口。
- Wave 2：`Completed`。主线程整合 `plugin-runtime-worker` 的 hosting / diagnostics 结果，补齐 `AddKodaClawPluginHost()`、stdio fixture server 与 plugin host integration tests，最终再次通过 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 回归。
- Wave 3：`Completed`。主线程补齐 `/api/plugins/*` Gateway contract / service / integration 面，并整合 runtime tool injection 与 `kodaclaw-web` plugin manager desk；本波已通过 `PluginApiContractsTests`、`PluginToolInjectionIntegrationTests|PluginApiIntegrationTests`、`npm run typecheck`、`npm run build`、`npm test`、`npx playwright test tests/kc0407-plugins.spec.ts` 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 收口。
- `KC-0401`：`Completed`。`PluginManifest` shared contract、manifest loader / normalizer / validator 已落地，覆盖 `plugin.json` v1 round-trip、stdio valid case、required fields / invalid id / empty tool capabilities 等 contract tests，并通过定向 contract tests + solution regression。
- `KC-0402`：`Completed`。`SqlitePluginRegistryRepository` 已落地，复用工作区 `config/control-plane.db` 初始化 `plugins` 表，支持 upsert / get / list(filter) / delete，并通过 unit tests + solution regression。
- `KC-0403`：`Completed`。`PluginLifecycleHost`、`IPluginLifecycleHost`、`PluginHostOptions` 与 `AddKodaClawPluginHost()` 已落地，支持 stdio plugin start / stop / restart / health probe 前置接入、namespaced tool catalog 输出与失败隔离；并通过 `PluginLifecycleHostIntegrationTests` + solution regression。
- `KC-0404`：`Completed`。`PluginPermissionPolicy` 已落地，支持 permission normalization、risk summary、path/token validation，并通过定向 contract tests + solution regression。
- `KC-0405`：`Completed`。`MainSessionService` 已在 fresh main session 创建路径注入 `Trusted + Enabled + Running + Tool` 的 namespaced plugin tools，并确保 stop / degraded 插件不会继续出现在新 session 的有效工具列表中；已通过 `PluginToolInjectionIntegrationTests`、定向 plugin API 集成验证与 solution regression。
- `KC-0406`：`Completed`。`SqlitePluginLogRepository`、plugin health / degraded 监督链路与 stdio fixture health drill 已落地，支持 per-plugin logs、restart count、diagnostics evidence，并通过 `SqlitePluginLogRepositoryTests`、`PluginHealthIntegrationTests` 与 solution regression。
- `KC-0407`：`Completed`。`kodaclaw-web` 已集成 `PluginsDesk`，支持列表 / 过滤、详情、权限摘要、日志证据、install / discover、trust / enable / disable / start / stop 与 settings 占位，并通过 component tests、`tests/kc0407-plugins.spec.ts`、`npm run build` 与 solution-level regression。
- `KC-0408`：`Completed`。已新增 bundled fixture plugin 清单 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/Plugins`、集成 smoke `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration4AcceptanceIntegrationTests.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_4_ACCEPTANCE_PACK.md`，覆盖 bundled discover -> trust/enable/start -> fresh-session injection -> stop/degraded isolation 链路，并通过定向 plugin contract/integration/frontend 回归、`npm run test:e2e` 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`。
- 下一波：Iteration 4 已完整收口；Iteration 5 的 channels 范围现已冻结，下一步进入 Wave 1 foundation，再把 desktop / trust model 留给后续切片。

## 迭代 5：Channels（Wave 0/1/2/3/4 已完成）

- Wave 0：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_5_FREEZE.md` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/adr/ADR-0009-channels-v1.md`，冻结了 Iteration 5 的范围、connector 策略、session / memory 边界、delivery rule、最小 API 面与并行写集。
- 范围冻结：Iteration 5 v1 只要求两条 connector 路径进入验收：一个真实 `telegram` connector 和一个合成 `generic-webhook` connector；其余渠道继续留在后续切片或 plugin 化阶段。
- 架构冻结：channels 长期仍是 plugin-capable domain，但 Iteration 5 的首批 connector 由 `KodaClaw.ChannelHub` 直接托管，不要求复用 Iteration 4 尚未冻结的 `channel` plugin runtime。
- 会话冻结：所有外部消息只能进入 `SessionKind.ChannelDirectMessage` 或 `SessionKind.ChannelGroup`；不允许落入 `main` session，也不允许群聊读取主记忆。
- 交付冻结：outbound 统一走 `DeliveryRule`，并复用 `ApprovalKind.ChannelDelivery` + `InboxItemKind.ChannelUpdate`，不新增独立渠道审批栈。
- `KC-0501`：`Completed`。已新增 channel shared contracts：`ChannelConnectorKind`、`ChannelAccountState`、`ChannelThreadType`、`ChannelEventType`、`DeliveryMode`、`ChannelEventEnvelope`、`ChannelThreadDetail`、`IChannelConnector` 等冻结对象，并补充 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Channels/ChannelContractsTests.cs`；已通过定向 contract tests、`dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.ChannelHub/KodaClaw.ChannelHub.csproj` 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 验证。
- `KC-0502`：`Completed`。已落地 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.ChannelHub/SqliteChannelHubDatabase.cs`、`SqliteChannelAccountRepository.cs`、`SqliteThreadBindingRepository.cs` 与 `AddKodaClawChannelHub()`，把 `channel_accounts` / `thread_bindings` 两张表纳入 workspace-local `config/control-plane.db`；并通过 `SqliteChannelAccountRepositoryTests`、`SqliteThreadBindingRepositoryTests` 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 验证。
- `KC-0503`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.ChannelHub/ChannelPolicyEngine.cs`、`ChannelPolicyDecision.cs` 与 `tests/KodaClaw.UnitTests/ChannelHub/ChannelPolicyEngineTests.cs`，冻结了 DM / group 默认 policy、mute 判定、direct-reply 判定与 memory boundary（群聊不读 `USER` / 长期记忆，channel v1 不读 `MEMORY.md`）；并通过定向 unit tests 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 验证。
- `KC-0504`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.ChannelHub/ChannelDeliveryGovernanceService.cs`、`ChannelDeliveryDisposition.cs`、`ChannelDeliveryEvaluationResult.cs` 与 `tests/KodaClaw.IntegrationTests/ChannelHub/ChannelDeliveryGovernanceIntegrationTests.cs`，把 `DeliveryRule` 接入现有 approval / inbox / diagnostics 基线，形成 `AutoSend` 直发与 `DraftApproval` / `RequireApproval` 落 `ApprovalKind.ChannelDelivery` + `InboxItemKind.ChannelUpdate` 的最小治理闭环；并通过定向 integration tests 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 验证。
- `KC-0505`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.ChannelHub/Connectors/Telegram/TelegramConnector.cs`、`TelegramConnectorConfiguration.cs`、`TelegramConnectorOptions.cs`、`TelegramApiContracts.cs`、`ITelegramApiClient.cs`、`HttpTelegramApiClient.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/ChannelHub/TelegramConnectorIntegrationTests.cs`，并由主线程补齐 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.ChannelHub/ServiceCollectionExtensions.cs` 的 connector 注册与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/Program.cs` 的 connector 能力标记；已通过 `dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.ChannelHub/KodaClaw.ChannelHub.csproj`、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter TelegramConnectorIntegrationTests` 与最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 验证。
- `KC-0506`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.ChannelHub/Connectors/Webhook/GenericWebhookConnector.cs`、`GenericWebhookPayloadParser.cs`、`GenericWebhookConnectorConfiguration.cs`、`GenericWebhookInboundDispatchResult.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/ChannelHub/GenericWebhookConnectorIntegrationTests.cs`，并由主线程补齐 `/api/channels/accounts`、`/api/channels/webhook/{accountId}/events`、`ChannelEventIngestionService` 与 thread/detail 查询整合；已通过定向 contract/integration tests、`dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj` 与最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 验证。
- `KC-0507`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Runtime/IChannelSessionService.cs`、`ChannelSessionService.cs`、`ChannelSessionOptions.cs` 与 runtime DI 注册，把 `channel-dm` / `channel-group` session 组合层接入 `KodaClaw.Runtime`；当前会按 `ChannelPolicy` 保守加载 `AGENTS.md` / `IDENTITY.md` / `SOUL.md`、私聊 `USER.md`、以及 `workspace/channels/<bindingId>/SUMMARY.md`（若存在），同时拒绝把群聊升级为读取 `USER.md` / `MEMORY.md`。主线程还补齐了 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/Program.cs` 的 session-kind 推断与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/GatewaySessionsIntegrationTests.cs` 的覆盖；已通过 `dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Runtime/KodaClaw.Runtime.csproj`、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "ChannelSessionServiceIntegrationTests|GatewaySessionsIntegrationTests|ChannelApiIntegrationTests"` 与最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 验证。
- `KC-0508`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/src/components/ChannelsDesk.tsx`，并补齐 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/src/types/contracts.ts` 的 channels DTO、`src/lib/api.ts` 的 `/api/channels/*` fetch helper、`src/__tests__/channels-desk.spec.tsx`、`src/__tests__/app-shell.spec.tsx` 与 `tests/kc0508-channels.spec.ts`；当前主工作台已能查看 connector/account 概览、thread 列表筛选、policy、delivery rule、pending approval/draft 摘要与 recent audit。已通过 `npm run build`、`npm run test`、`npx playwright test tests/kc0508-channels.spec.ts` 与最新 `npm run test:e2e` 验证。
- `KC-0509`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.ChannelHub/Audit/SqliteChannelAuditRepository.cs`、`ChannelAuditQueryService.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.UnitTests/ChannelHub/SqliteChannelAuditRepositoryTests.cs`，并由主线程补齐 `/api/channels/threads/{bindingId}/audit`、thread detail recent-audit 聚合与 webhook correlation diagnostics；已通过 ChannelHub unit slice、`ChannelApiIntegrationTests` 与最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 验证。
- Wave 4：`Completed`。主线程补齐 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.ChannelHub/ChannelDeliveryApprovalService.cs`、`ChannelDeliveryApprovalDispatchResult.cs`、`tests/KodaClaw.IntegrationTests/ChannelHub/ChannelDeliveryApprovalIntegrationTests.cs` 与 Gateway approve/reject dispatch wiring，把 `ApprovalKind.ChannelDelivery` 从草稿落库真正收口到 approve -> connector outbound / reject -> no-send + audit 闭环；随后新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration5DirectMessageAcceptanceIntegrationTests.cs`、`Iteration5GroupSafetyAcceptanceIntegrationTests.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_5_ACCEPTANCE_PACK.md`，并通过定向 acceptance smoke、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`、`npm run build`、`npm run test`、`npm run test:e2e` 收口。
- `KC-0510`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration5DirectMessageAcceptanceIntegrationTests.cs`，冻结 Telegram DM -> binding reuse -> isolated `ChannelDirectMessage` session -> `USER.md` + thread summary prompt enrichment -> approval approve -> Telegram outbound + audit evidence 的整条验收链路；并通过 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "ChannelDeliveryApprovalIntegrationTests|Iteration5DirectMessageAcceptanceIntegrationTests"`、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_5_ACCEPTANCE_PACK.md` 验证。
- `KC-0511`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration5GroupSafetyAcceptanceIntegrationTests.cs`，冻结 group inbound -> binding reuse -> isolated `ChannelGroup` session -> 不读取 `USER.md` / `MEMORY.md` -> `RequireApproval` reject -> no outbound + audit evidence 的安全验收链路；并通过 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter Iteration5GroupSafetyAcceptanceIntegrationTests`、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_5_ACCEPTANCE_PACK.md` 验证。
- 下一波：Iteration 5 已完整收口；随后已完成 Iteration 6 Desktop Shell 的 Wave 0 规划冻结，当前转入 Wave 1 foundation 准备阶段。

## 迭代 6：Desktop Shell（Wave 0 已完成）

- Wave 0：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_6_FREEZE.md` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/adr/ADR-0010-desktop-shell-v1.md`，冻结了 Iteration 6 的桌面壳范围、runtime config bridge、Gateway attach-or-launch 边界、通知与 launch target 复用策略，以及分波写集。
- Wave 1：`Completed`。主线程已收口 `kodaclaw-desktop` Electron 工程骨架、`BrowserWindow` dev/prod/placeholder 加载策略、preload bridge，以及 `kodaclaw-web` 对 desktop runtime config / launch target 的适配；本波已通过 `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run typecheck && npm run build && npm run package:smoke`、`cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test && npm run test:e2e` 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 收口。
- Wave 2：`Completed`。主线程已收口 `AttachOnly` / `ManagedChild` Gateway 生命周期桥、managed child 健康等待与 restart、tray/menu、hide-on-close 恢复语义、全局快捷键，以及 `npm run smoke:managed-gateway` 的桌面 smoke 验证；本波已通过 `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:managed-gateway && npm run package:smoke`、`cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test && npm run test:e2e` 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 收口。
- Wave 3：`Completed`。主线程已收口基于 `/api/settings` + `/api/approvals` + `/api/inbox` 的通知轮询、quiet-hours 复用、route/protocol/startup-arg 到 launch-target 的解析，以及 `smoke:wave3` 的 fixture-based 桌面 smoke；本波已通过 `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test && npm run smoke:wave3 && npm run smoke:managed-gateway && npm run package:smoke`、`cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test && npm run test:e2e` 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 收口。
- Wave 4：`Completed`。`KC-0607` 的 macOS-first packaging smoke 与 `KC-0608` 的 acceptance pack 已全部收口；Iteration 6 当前已通过 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_6_ACCEPTANCE_PACK.md` 固化最终回归矩阵、dogfood drill 与退出标准。
- 范围冻结：Iteration 6 v1 的核心不是做第二套产品前端，而是让 `kodaclaw-desktop` 作为 Electron shell 承载现有 `kodaclaw-web`，并把主窗口、tray、通知、deep-link、Gateway 生命周期桥做实。
- 架构冻结：继续坚持 `desktop is shell, gateway is brain`；Electron 主进程只负责窗口、系统集成、Gateway 附着/启动与运行时配置，不承载 SDK runtime 或业务状态事实源。
- 配置冻结：renderer 不再只依赖 Vite 编译时变量读取 Gateway 配置；desktop 必须通过 preload/runtime bridge 注入 `gatewayUrl`、`gatewayToken`、平台信息和 launch target。
- 通知冻结：桌面通知继续复用现有 `Inbox` / `Approvals` / `Settings` / quiet hours，不新建独立桌面通知数据库或通知控制面。
- 平台冻结：首个强验收平台优先是 macOS；同时尽量保持代码与脚本跨平台中立。
- `KC-0601`：`Completed`。已建立 `kodaclaw-desktop` Electron 工程骨架、开发脚本、BrowserWindow、preload 与基础 packaging smoke，并通过 `npm run typecheck` / `npm run build` / `npm run package:smoke` + 最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`。
- `KC-0602`：`Completed`。desktop 已支持 `AttachOnly` / `ManagedChild` 两种 Gateway 生命周期模式，包含 managed child `dotnet run` 启动、健康等待、restart 与退出协调，并通过 `npm run smoke:managed-gateway` + 最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`。
- `KC-0603`：`Completed`。web renderer 已支持从 preload 获得 `gatewayUrl` / `gatewayToken` / `initialTarget` / `gatewayLifecycleMode`，并通过 desktop bridge 单测、全量 web build/test/e2e 与最新 solution 回归。
- `KC-0604`：`Completed`。桌面壳已补齐 tray/menu 入口、`Show KodaClaw` / `Open Chat` / `Open Inbox` / `Open Canvas` / `Open Channels` / `Restart Gateway` / `Quit` 菜单、hide-on-close 恢复、以及 `CommandOrControl+Shift+K` 全局快捷键，并通过桌面 smoke + packaging smoke 收口。
- `KC-0605`：`Completed`。桌面壳已实现 `/api/settings` + `/api/approvals` + `/api/inbox` 的通知轮询、quiet-hours 抑制、approval/inbox 去重与 notification click -> launch target 路由，并通过 pure desktop tests + `smoke:wave3` 收口。
- `KC-0606`：`Completed`。桌面壳已支持 startup args（`--kodaclaw-route=` / `--kodaclaw-target=`）、`kodaclaw://...` protocol payload、single-instance 二次启动转发，以及 route -> desk/entityId 映射，并通过 `smoke:wave3` + `smoke:managed-gateway` 收口。
- `KC-0607`：`Completed`。已通过 `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run package:smoke` 完成 macOS-first `electron-builder --dir` 打包 smoke，并确认关闭自动签名探测后可稳定收口。
- `KC-0608`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_6_ACCEPTANCE_PACK.md`，冻结桌面 dogfood drill、回归矩阵、通知/launch-target 验收与最终退出标准，并与状态文档同步。
- 下一波：Iteration 6 Desktop Shell v1 已完整收口；如需继续推进，应先单独冻结下一个迭代或硬化主题（例如签名分发、auto-update、Keychain、protocol registration hardening）。

## 迭代 7：Hardening（Wave 0 已完成）

- Wave 0：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_FREEZE.md` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/adr/ADR-0011-hardening-v1.md`，冻结了 Iteration 7 的 Keychain-first、secret-ref、device identity、backup/import/export、crash repair、plugin trust hardening、update scaffold、sandbox risk UI 与 diagnostic bundle 边界。
- Wave 1：`Completed`。主线程已完成 `KC-0701` secret-store foundation 与 `KC-0703` device identity completion，并通过定向 contract/integration 与 solution-level 回归。
- Wave 2：`Completed`。主线程已完成 `KC-0702` + `KC-0704` + `KC-0705`：secret migration/report、backup/export/import v1 与 startup repair / crash residue evidence 已全部收口。Wave 2 现已补齐 `BackupManifest` / `RepairChecklist` / `StartupRepairReportResponse` 合同、`WorkspaceBackupService`、自动启动的 `WorkspaceRepairService` / `StartupRepairHostedService`、`/api/system/backup-export` / `/api/system/backup-import/preflight` / `/api/system/backup-import` / `/api/system/startup-repair-report`，以及 `config/import-repair-report.json` / `config/startup-repair-report.json` 双 repair evidence。该波次已通过 `BackupContractsTests`、`StartupRepairContractsTests`、`BackupApiIntegrationTests`、`StartupRepairApiIntegrationTests`、最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 与 `kodaclaw-web` build/test 验证。
- Wave 3：`Completed`。主线程已完成 `KC-0706` + `KC-0707` + `KC-0708` + `KC-0709`：plugin trust hardening、manual-first update watch、sandbox risk briefing 与 default-redacted diagnostic bundle export 全部收口。Wave 3 现已补齐 `POST /api/diagnostics/bundle-export`、`manifest.json` / `redaction-summary.json` bundle schema、`SessionsDiagnosticsDesk` 导出入口，以及最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`、`cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test && npm run test:e2e` 的全量回归。
- 范围冻结：Iteration 7 v1 的核心不是再加新产品面，而是把现有 Gateway / Web / Desktop / Plugin / Channel / Automation 产品补齐安全、恢复、迁移与运维闭环。
- 安全冻结：生产 secrets 默认进入 OS Keychain 或等价 secure store；配置只保留 `SecretRef` 与脱敏 metadata，env 只保留给 bootstrap / tests / migration fallback。
- 恢复冻结：export/import 默认排除 raw secrets，并统一通过 preflight + repair checklist 处理 device mismatch、missing secrets 与 crash residue。
- 发布冻结：update mechanism 首期只做版本检查、通道信息、发布说明与手动升级入口，不承诺 silent auto-update。
- `KC-0701`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/SecretRef.cs`、`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/SecretDescriptor.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/ISecretStore.cs`，冻结共享 secret-store 合同；`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Workspace/PlatformSecretStore.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Workspace/MacOsKeychainCommandRunner.cs` 提供 `memory` / `env` / `keychain` provider；`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/GatewayAuthTokenAccessor.cs` 让 Gateway 优先解析 `KODACLAW_GATEWAY_TOKEN_SECRET_REF` / `Gateway:TokenSecretRef`，未命中时回退到 legacy token。已通过 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter SecretStoreContractTests`、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter GatewayAuthIntegrationTests` 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`。
- `KC-0702`：`Completed`。模型迁移第一阶段、ChannelHub 迁移子阶段与 PluginHost 迁移子阶段均已收口，并进一步补齐 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/SecretMigrationState.cs`、`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/SecretMigrationItem.cs`、`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/SecretMigrationReport.cs`、`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/SecretMigrationReportService.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/Program.cs`，让 Gateway 可以通过 `/api/system/secret-migration-report` 生成脱敏 migration evidence，并持久化到 `config/secret-migration-report.json`；`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/Workspace/SecretMigrationReportContractTests.cs`、`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Gateway/SecretMigrationReportIntegrationTests.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/src/types/contracts.ts` 已同步覆盖 / 对齐。已通过 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter SecretMigrationReportContractTests`、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter SecretMigrationReportIntegrationTests`、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 与 `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test`。
- `KC-0703`：`Completed`。已扩展 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/DeviceIdentity.cs` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/WorkspaceSnapshot.cs`，补齐 fingerprint / rotation / metadata 字段；`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Workspace/WorkspaceService.cs` 现会在读取 legacy `identity/device.json` 时自动补齐新 metadata 并回写。已通过 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter WorkspaceServiceContractTests` 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`。
- `KC-0704`：`Completed`。已落地 backup manifest、`WorkspaceBackupService`、`/api/system/backup-export` / `/api/system/backup-import/preflight` / `/api/system/backup-import`、脱敏 control-plane export、pristine-workspace restore 与 repair checklist/report；已通过 `BackupContractsTests`、`BackupApiIntegrationTests`、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 与 `kodaclaw-web` build/test。
- `KC-0705`：`Completed`。已落地统一 startup repair engine：Gateway 启动时会自动执行 `WorkspaceRepairService`，取消 stale approvals / channel delivery residue、失败化 queued/running automation runs、收口 stale plugin runtime state、清空 unsupported approval-wait main session active pointer，并把结果同步到 `config/startup-repair-report.json`、`startup-repair-latest` inbox alert 与 `/api/system/startup-repair-report`。已通过 `StartupRepairContractsTests`、`StartupRepairApiIntegrationTests`、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 与 `kodaclaw-web` build/test。
- `KC-0706`：`Completed`。已落地 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/PluginTrustEvidence.cs`、`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.PluginHost/Trust/PluginTrustEvaluator.cs`、SQLite `trust_evidence_json` 持久化与 Web `PluginsDesk` trust evidence 面；插件现在会在 discover / install / detail / trust / start 路径自动生成 digest evidence，并在存在 `plugin.signature.json` sidecar 且校验通过时把 trust 升级为 `Signed`。bundled fixture acceptance 也已升级为 signed case。已通过 `PluginApiContractsTests`、`PluginTrustEvaluatorTests`、`SqlitePluginRegistryRepositoryTests`、`PluginApiIntegrationTests`、`PluginToolInjectionIntegrationTests`、`Iteration4AcceptanceIntegrationTests`、最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`，以及 `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test && npx playwright test tests/kc0407-plugins.spec.ts`。
- `KC-0707`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/UpdateAvailability.cs`、`UpdateReleaseChannel.cs`、`UpdateCheckRequest.cs`、`UpdateComponentState.cs`、`UpdateStateResponse.cs`、`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/UpdateStateService.cs` 与 Gateway `GET /api/system/update-state` / `POST /api/system/update-check`，并把 update evidence 持久化到 `config/update-state.json`；同时 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop/src/main.ts` / `preload.ts` / `desktop-shell-types.ts` 已补齐 `appVersion` / `releaseChannel` runtime bridge，`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/src/components/ModelsSettingsDesk.tsx` 已落地统一的 `Update Watch` 手动升级面，`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/Directory.Build.props` 也把产品基线版本统一到 `0.1.0`。已通过 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter UpdateStateContractsTests`、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter UpdateStateApiIntegrationTests`、`cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/models-settings-desk.spec.tsx`、`cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`、`cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0212-models-settings.spec.ts`、`cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test`、`cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:wave3` 与最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`。
- `KC-0708`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/SandboxRiskOverviewResponse.cs`、`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/SandboxRiskOverviewService.cs` 与 `/api/settings/sandbox-risk` 聚合接口，并把 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/src/components/ModelsSettingsDesk.tsx` 扩展为统一的 `Sandbox & Risk Briefing`。当前 UI 会明确展示 Local sandbox + boundary enforcement 的 best-effort 边界、SDK 支持但未启用的 Docker 选项、persisted approval posture、plugin 高风险权限与 channel outbound gating 风险；同时 `SandboxRiskContractsTests`、`SandboxRiskApiIntegrationTests`、`models-settings-desk.spec.tsx` 与 `tests/kc0212-models-settings.spec.ts` 已同步覆盖。已通过 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj --filter SandboxRiskContractsTests`、`dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter SandboxRiskApiIntegrationTests`、最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`，以及 `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test && npm run test:e2e`。
- `KC-0709`：`Completed`。已新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Contracts/DiagnosticBundleExportRequest.cs`、`DiagnosticBundleExportResponse.cs`、`DiagnosticBundleManifest.cs`、`DiagnosticBundleRedactionSummary.cs`、`DiagnosticBundleDesktopContext.cs`、`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/DiagnosticBundleService.cs` 与 Gateway `POST /api/diagnostics/bundle-export`，并把 bundle 默认写入 `cache/diagnostics/kodaclaw-diagnostic-bundle-<timestamp>.zip`。归档现包含 redacted settings snapshot、diagnostics recent/timeline、session `meta.json`、repair/update evidence、log summary、`manifest.json` 与 `redaction-summary.json`，并明确排除 raw secrets、`messages.json` / `tool-calls.json` 与 raw logs；`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web/src/components/SessionsDiagnosticsDesk.tsx` 也已提供导出入口与 desktop runtime context 桥接。已通过 `DiagnosticBundleContractsTests`、`DiagnosticBundleApiIntegrationTests`、`sessions-diagnostics-desk.spec.tsx`、`tests/kc0211-sessions-diagnostics.spec.ts`、最新 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 与 `kodaclaw-web` build/test/e2e。
- `KC-0710`：`Completed`。已补齐 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_ACCEPTANCE_PACK.md` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/Smoke/Iteration7AcceptanceIntegrationTests.cs`，把迁移、修复、导入导出、update、risk 与 diagnostic bundle 串成综合验收，并通过 contract/integration/web/desktop/solution 全量验证。
- `2026-03-19 release-closure hardening patch`：`Completed`。已对已收口的 Hardening v1 做最后一轮正确性加固：`SecretMigrationReportIntegrationTests` 改为隔离 config / unique env 注入；`WorkspaceBackupService` 在导入前先检视 zip manifest、entry 安全性、manifest/实际 entry 一致性与归档大小上限，并把 backup export / diagnostic bundle `archivePath` 限制在 workspace 内；`UpdateStateService` 与 Web `ModelsSettingsDesk` 现只允许绝对 `http/https` 外链。已通过定向 hardening integration suite、`models-settings-desk.spec.tsx`、web build 与最新串行 solution 回归。
- `2026-03-19 review follow-up patch`：`Completed`。已修复 review 中确认的三处真实缺口：`DiagnosticBundleService` 现会 redaction JSON / quoted secret 片段；`SecretMigrationReportService` 通过仓储分页完整扫描 channel / plugin 记录；`WorkspaceBackupService` 现会拒绝大小写碰撞的 archive entry，并把相对 `archivePath` 解析到目标 workspace root。已通过定向 `BackupApiIntegrationTests|DiagnosticBundleApiIntegrationTests|SecretMigrationReportIntegrationTests` 与最新串行 solution 回归。
- `2026-03-19 runtime config activation patch`：`Completed`。已补齐当前工作目录 `.env` / `.env.local` / `appsettings*.json` bootstrap，新增动态 runtime snapshot 解析与 `DynamicModelProvider`，并让 Runtime Control 中的 default endpoint 对新 chat/new session 无需重启 Gateway 即可生效；同时补齐了 root-level `appsettings.json` / `appsettings.Development.json`、`.env.example` 注释与专项配置文档。已通过 `GatewayConfigurationBootstrapIntegrationTests|ModelRuntimeBootstrapIntegrationTests`、后续 solution 回归与 web build/test 验证。
- 下一波：Iteration 7 Hardening v1 已完整收口；当前没有未关闭的 hardening 任务，若继续推进应先冻结下一轮产品迭代范围与 ADR。

## 迭代 8：Channels 会话闭环 + Bootstrap 草稿生成

- 范围冻结：本迭代补齐两条产品闭环——①外部消息经 inbound 事件、Runtime 执行后自动产生草稿/审批/直发；②Bootstrap 对话结束后由模型合成 `IDENTITY.md` / `SOUL.md` / `USER.md` 草稿，取代手动编辑工作流。
- 架构冻结：`ChannelTurnOrchestrator` 作为 channel 方向单一编排入口，串接 `ChannelEventIngestionService` → `IChannelSessionService.RunInboundTurnAsync` → `ChannelPolicyEngine` → `ChannelDeliveryGovernanceService` → `ChannelDeliveryDispatchService`；`BootstrapDraftService` 通过 `PromptBuilder` + `IModelProvider.CompleteAsync` 生成三份草稿 markdown。
- 交付冻结：outbound 路由继续复用 `TelegramConnector` / `GenericWebhookConnector`；bootstrap 草稿由 `POST /api/system/bootstrap-draft` 对外暴露，前端可提交对话记录或种子草稿获取生成结果。
- `KC-0801`：`Completed`。已新增 `src/KodaClaw.Contracts/{BootstrapDraftMessage,BootstrapDraftRequest,BootstrapDraftResult,IBootstrapDraftService}.cs`、`src/KodaClaw.Runtime/{BootstrapDraftOptions,BootstrapDraftService}.cs` 与 Gateway `POST /api/system/bootstrap-draft` 端点；服务通过 `PromptBuilder` 组装对话记录 / 种子草稿并调用 `IModelProvider.CompleteAsync` 生成 `IdentityMarkdown` / `SoulMarkdown` / `UserMarkdown`；已通过 `tests/KodaClaw.IntegrationTests/Gateway/BootstrapDraftApiIntegrationTests.cs`（2/2 PASS）、`tests/KodaClaw.IntegrationTests/Runtime/BootstrapDraftServiceIntegrationTests.cs` 与最新 `dotnet test KodaClaw.sln -m:1` 验证。
- `KC-0802`：`Completed`。已新增 `src/KodaClaw.ChannelHub/{ChannelTurnOrchestrator,ChannelDeliveryDispatchService,ChannelDeliveryDispatchResult,ChannelTurnOrchestrationResult}.cs` 与 `src/KodaClaw.Contracts/{ChannelTurnOutcome,ChannelTurnOutcomeKind}.cs`、`src/KodaClaw.Runtime/{ChannelReplyProposal,ChannelTurnExecutionResult}.cs`；`ChannelTurnOrchestrator` 已通过 `ChannelInboundGatewayService` 完整接入 channel inbound 处理路径，支持 `NoAction` / `DraftCreated` / `ApprovalRequested` / `Delivered` / `Failed` 五种 outcome；`ChannelDeliveryDispatchService` 按 `ConnectorKind` 路由至 `TelegramConnector` / `GenericWebhookConnector`，并在成功后更新 `ThreadBinding.LastOutboundAt`、写入 audit + diagnostics；已通过 channel integration tests（17/17 PASS）与最新 `dotnet test KodaClaw.sln -m:1` 验证。

## 迭代 9：Workspace Memory Live

- 范围冻结：修复主会话不加载 workspace 文件的结构性缺口，并实现 Agent 在对话中写回记忆的完整闭环。详见 `docs/ITERATION_9_FREEZE.md`。
- 架构冻结：主会话 `BuildSystemPromptAsync` 改为异步并加载 `AGENTS.md / IDENTITY.md / SOUL.md / USER.md / MEMORY.md / memory/YYYY-MM-DD.md`；新增 `WorkspaceMemoryAppendTool`（宿主进程工具，注入 `IWorkspaceService`，写 `workspace/memory/YYYY-MM-DD.md`）；`DefaultWorkspaceTemplates.Heartbeat()` 补充 `Nightly Memory Consolidation` 定义（`enabled: false`）。
- 交付冻结：`workspace_memory_append` 工具同时注入主会话和 Automation 会话；夜间整合 automation 通过读 `MEMORY.md` + `memory/YYYY-MM-DD.md` 后调用同一工具完成 `MEMORY.md` 覆写；Option B（会话结束后自动摘要）延迟不在本迭代。
- `KC-0901`：`Completed`。主会话 Workspace 上下文加载——`MainSessionService.BuildSystemPrompt()` 改为 `BuildSystemPromptAsync()`，加载六类 workspace 文件（AGENTS/IDENTITY/SOUL/USER/MEMORY/daily memory）后注入 `PromptBuilder.AddContextDocuments()`；已通过 `MainSessionWorkspaceContextIntegrationTests`（5/5 PASS）与 `dotnet test KodaClaw.sln -m:1` 验证。
- `KC-0902`：`Completed`。`workspace_memory_append` 工具——新增 `src/KodaClaw.Runtime/WorkspaceMemoryAppendTool.cs`，注册到 `MainSessionOptions.DefaultTools`（AutomationSessionOptions 复用相同列表）；`ServiceCollectionExtensions.cs` 注入真实工具实例；已通过 `WorkspaceMemoryAppendToolTests`（8/8 PASS）+ `WorkspaceMemoryAppendIntegrationTests`（5/5 PASS）+ `dotnet test KodaClaw.sln -m:1` 验证。
- `KC-0903`：`Completed`。夜间记忆整合模板——更新 `DefaultWorkspaceTemplates.Heartbeat()` 加入 `Nightly Memory Consolidation` 条目（schedule: daily 23:45, enabled: false）；template 结构已通过全量回归验证。

## 迭代 10：Workspace Protocol Write

- 范围冻结：让 Agent 能在主对话中语义化地 patch workspace 协议文件（IDENTITY/SOUL/USER/MEMORY/AGENTS），用户只说自然语言，Agent 自主决定写哪个文件与章节；同时修正 HEARTBEAT.md 模板中错误引用工具名的问题。详见 `docs/ITERATION_10_FREEZE.md`。
- 架构冻结：新增 `WorkspaceProtocolUpdateTool`（章节级 patch，非 append）；`target` 枚举限定五个协议文件；`section` 为空时替换 `#` 标题之后全部正文；section 不存在时末尾追加新章节；写入后触发 `workspace_protocol_updated` 诊断事件。
- 交付冻结：仅注入主会话；不做格式校验；修改在下次会话启动时生效（system prompt 读取时）；HEARTBEAT.md 模板同步修正引用错误。
- `KC-1001`：`Completed`。`workspace_protocol_update` 工具——新增 `src/KodaClaw.Runtime/WorkspaceProtocolUpdateTool.cs`（`public static ApplySectionPatch`，三种 patch 路径：无 section 替换正文、有 section 定点替换、section 不存在末尾追加）；注册到 `MainSessionOptions.DefaultTools` 与 `ServiceCollectionExtensions.cs`；`DefaultWorkspaceTemplates` 改为 `public`；已通过 `WorkspaceProtocolUpdateToolTests`（12/12 PASS）+ `WorkspaceProtocolUpdateIntegrationTests`（6/6 PASS）+ `dotnet test KodaClaw.sln -m:1` 验证。
- `KC-1002`：`Completed`。Heartbeat 模板修正——将 `Nightly Memory Consolidation` 中的引导语从 `workspace_memory_append` 改为 `workspace_protocol_update with target=memory`；补充 `WorkspaceTemplateContractTests`（8/8 PASS）验证模板正确引用工具、路径清洁、关键字段存在；已通过 `dotnet test KodaClaw.sln -m:1` 验证。

## 迭代 11：HEARTBEAT.md → SQLite 热更链路

- 范围冻结：打通 `HeartbeatAutomationCompiler` 到 `AutomationScheduler` 之间缺失的同步链路。用户手动编辑或 Agent 写入 `HEARTBEAT.md` 后，变更须自动反映到 SQLite，使 `AutomationScheduler.TickAsync()` 能够正确调度。详见计划文档。
- 架构冻结：新增 `IHeartbeatSyncService`（编译 + diff + upsert/delete）与 `HeartbeatFileWatcherHostedService`（启动即同步 + 文件变更监听 + 500ms debounce），均放在 `KodaClaw.Workspace` 模块，通过 `AddKodaClawWorkspace()` 注册；`IAutomationDefinitionRepository` 由 Automation 模块注册，DI 延迟解析无顺序冲突。
- 关键不变量：同步时保留 `LastRunAt / NextRunAt / LastRunStatus / LastError`，避免修改 HEARTBEAT.md 后意外重置调度状态；文件不存在时保守不清除已有定义；编译失败时保留现有定义并返回 `CompilationFailed=true`。
- `KC-1101`：`Completed`。新增 `src/KodaClaw.Workspace/HeartbeatSyncService.cs`，包含 `IHeartbeatSyncService` 接口与 `HeartbeatSyncResult` record；同步算法：读文件 → `HeartbeatAutomationCompiler.Compile()` → `IAutomationDefinitionRepository.ListAsync(Source=Heartbeat)` → upsert 保留调度状态 → delete 已删除条目；已通过 `HeartbeatSyncServiceTests`（5/5 PASS，L1 单元）+ `HeartbeatSyncIntegrationTests`（3/3 PASS，L2 真实 SQLite）+ `dotnet test KodaClaw.sln -m:1` 验证。
- `KC-1102`：`Completed`。新增 `src/KodaClaw.Workspace/HeartbeatFileWatcherHostedService.cs`，`BackgroundService` 在启动时立即执行一次 `SyncAsync()`，随后 `FileSystemWatcher` 监听 `workspace/HEARTBEAT.md` 的 `Changed/Created` 事件，通过 `BoundedChannel(1, DropOldest)` + 500ms debounce 防抖后触发下次同步；目录不存在时安全退出；`ServiceCollectionExtensions.cs` 注册两个新服务，`KodaClaw.Workspace.csproj` 添加 `Microsoft.Extensions.Hosting.Abstractions` / `Logging.Abstractions` 包引用；已通过 L0 编译 + 同上测试套件验证。

## 迭代 12：Agent 主动性输出三件套

- 范围冻结：补齐 Agent 主动输出的三条断链：写入自动化规则（HEARTBEAT.md）、发布 Canvas 内容、推送 Inbox 通知。详见 `docs/ITERATION_12_FREEZE.md`。
- 架构冻结：三个工具均放在 `KodaClaw.Runtime`；`workspace_protocol_update` TargetFileMap 加入 `heartbeat`；`canvas_upsert` 写文件到 `workspace/canvas/{id}/index.{ext}` + upsert `ICanvasArtifactRepository`；`inbox_create` 写入 `IInboxRepository`（Kind=Information，Source=agent）；同步更新 `DefaultWorkspaceTemplates` AGENTS.md 引导。
- 关键约束：`canvas_upsert` 路径模型与 `GatewayApp.CanvasFiles.TryResolveCanvasFilePath` 对齐，EntryPath 形如 `workspace/canvas/{id}/index.{ext}`；`ICanvasArtifactRepository` / `IInboxRepository` 均由先于 Runtime 注册的模块提供，DI 无顺序冲突。
- `KC-1201`：`Completed`（2026-03-20）。
  - User Outcome：Agent 对话中理解"每天帮我做 X"后，可通过 `workspace_protocol_update(target=heartbeat)` 在 HEARTBEAT.md 中添加/修改 automation section，触发热更链路后自动调度。
  - Scope：`WorkspaceProtocolUpdateTool.TargetFileMap` 加 `heartbeat` entry；更新工具 Description；补充 L1 单元测试 + L3 contract 测试。
  - Modules：`KodaClaw.Runtime`，`KodaClaw.Workspace`（templates）。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter WorkspaceProtocol` ✅，`dotnet test tests/KodaClaw.ContractTests --filter WorkspaceTemplate` ✅。
- `KC-1202`：`Completed`（2026-03-20）。
  - User Outcome：Agent 对话中可调用 `canvas_upsert` 把报告、任务看板、HTML 内容发布到 Canvas，用户在 Canvas 面板中可见。
  - Scope：新增 `CanvasUpsertTool.cs`；注册到 `DefaultTools` + `ServiceCollectionExtensions`；写文件 + upsert SQLite。
  - Modules：`KodaClaw.Runtime`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter CanvasUpsert` ✅，`dotnet test tests/KodaClaw.IntegrationTests --filter CanvasUpsert` ✅。
  - 实现备注：`AssetDirectory` 不含末尾 `/`（否则 `CanvasArtifactValidation` 路径段校验失败）。
- `KC-1203`：`Completed`（2026-03-20）。
  - User Outcome：Agent 对话中可调用 `inbox_create` 主动推送关键结论或待跟进事项到 Inbox，用户在 Inbox 面板中可见。
  - Scope：新增 `InboxCreateTool.cs`；注册到 `DefaultTools` + `ServiceCollectionExtensions`；更新 AGENTS.md 模板加三条工具引导。
  - Modules：`KodaClaw.Runtime`，`KodaClaw.Workspace`（templates）。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter InboxCreate` ✅，`dotnet test tests/KodaClaw.ContractTests --filter WorkspaceTemplate` ✅。

## 迭代 13：Inbox 读取、Bootstrap 写回、Inbox 未读徽标

- 范围冻结：闭合 Iter 12 输出链路的最后缺口：Agent 可读取 Inbox（解锁 Daily Inbox Digest 自动化）；Bootstrap 对话写回身份文件（首次上手闭环）；GlobalRail 展示 Inbox 未读计数徽标。详见 `docs/ITERATION_13_FREEZE.md`。
- `KC-1301`：`Completed`（2026-03-20）。
  - User Outcome：Agent（主会话 + 自动化 session）可调用 `inbox_read` 读取 Inbox 内容，Daily Inbox Digest 摘要任务从此有实际数据可处理，不再空转。
  - Scope：新增 `InboxReadTool.cs`，注册为 `inbox_read`，加入 `DefaultTools` + `ServiceCollectionExtensions`；依赖已有 `IInboxRepository.ListAsync()`。
  - Modules：`KodaClaw.Runtime`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter InboxRead` ✅，`dotnet test tests/KodaClaw.ContractTests --filter WorkspaceTemplate` ✅。
- `KC-1302`：`Completed`（2026-03-20）。
  - User Outcome：Bootstrap 对话结束后，Koda 调用 `workspace_protocol_update` 将收集到的用户信息写入 IDENTITY.md/SOUL.md/USER.md，下次启动即可感知用户偏好与 Koda 人格。
  - Scope：更新 `DefaultWorkspaceTemplates.Bootstrap()` 补写回指令；更新 `DefaultWorkspaceTemplates.Agents()` 补 identity/soul/user/inbox_read 使用说明；对应 L3 contract 测试（6 条新增）。
  - Modules：`KodaClaw.Workspace`（templates）。
  - Verification：`dotnet test tests/KodaClaw.ContractTests --filter WorkspaceTemplate` ✅。
- `KC-1303`：`Completed`（2026-03-20）。
  - User Outcome：GlobalRail 的 Inbox 导航项旁显示未读计数徽标，Agent 推送 `inbox_create` 后用户无需主动打开 Inbox 才能发现新通知。
  - Scope：新增 `useInboxUnreadCount` hook（30s 轮询，`status=Open&limit=50`），GlobalRail Inbox 项渲染徽标；`position: relative` 补到按钮，`.v2-global-rail__badge` 样式新增。无需后端改动（直接用 items.length）。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npm run typecheck` ✅，`npm test` 47/47 ✅。

## 迭代 14：Automation Live + Workspace Context Depth

- 范围冻结：激活自动化调度器、新增 workspace_read 工具、为 channel 会话写回线程摘要。详见 `docs/ITERATION_14_FREEZE.md`。专注后端，无前端变更。
- `KC-1401`：`Completed`（2026-03-20）。
  - User Outcome：Agent（主会话 + 自动化 session）可在对话中按需读取任意 workspace 协议文件，解锁 Nightly Memory Consolidation 自动化（动态读取当日 memory 文件）。
  - Scope：新增 `WorkspaceReadTool.cs`，注册为 `workspace_read`，加入 `DefaultTools` + `ServiceCollectionExtensions`；targets: identity/soul/user/memory/agents/heartbeat/daily_memory。
  - Modules：`KodaClaw.Runtime`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter WorkspaceRead`，`dotnet test tests/KodaClaw.IntegrationTests --filter WorkspaceRead`。
- `KC-1402`：`Completed`（2026-03-20）。
  - User Outcome：用户在 `PUT /api/settings` 中将 `automationsEnabled` 设为 `true` 后，HEARTBEAT.md 中 `enabled: true` 的 automation 会在下一个调度 tick 自动执行，无需重启 Gateway。
  - Scope：`KodaClawSettings` 增加 `AutomationsEnabled: bool = false`；Gateway 注册时 `AutomationSchedulerOptions.Enabled = true`；`AutomationScheduler.TickAsync()` 增加动态设置检查；contract test 验证字段存在。
  - Modules：`KodaClaw.Contracts`，`KodaClaw.Automation`，`KodaClaw.Gateway`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter AutomationScheduler`，`dotnet test tests/KodaClaw.IntegrationTests --filter AutomationScheduler`，`dotnet test tests/KodaClaw.ContractTests --filter Settings`。
- `KC-1403`：`Completed`（2026-03-20）。
  - User Outcome：Telegram/Webhook channel turn 完成后，`workspace/channels/<bindingId>/SUMMARY.md` 自动追加本次交互摘要，下次 channel session 启动时上下文更丰富。
  - Scope：新增 `IChannelThreadSummaryWriter` + `ChannelThreadSummaryWriter`，`ChannelTurnOrchestrator` 注入（可选）并在 `ExecutedTurn=true` 且非 Failed 时调用写回。
  - Modules：`KodaClaw.ChannelHub`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter ChannelThread`，`dotnet test tests/KodaClaw.IntegrationTests --filter ChannelThread`。

## 迭代 15：AutomationsEnabled UI + V2 Chat Stage 收口

- 范围冻结：补齐自动化引擎的用户可见开关（Settings toggle + AutomationsDesk Banner）；收口 V2 Chat 主舞台的 page-like 痕迹；ChannelsDesk / PluginsDesk V2 接入验收。纯前端，无后端变更。详见 `docs/ITERATION_15_FREEZE.md`。
- `KC-1501`：`Completed`。
  - User Outcome：用户在 Settings 页可直接开/关自动化引擎全局开关（`automationsEnabled`），无需 API 调用；AutomationsDesk 在引擎关闭时显示警告 Banner，提示用户前往 Settings 启用，避免用户以为自动化已运行实则全局禁用。
  - Scope：`ModelsSettingsDesk` Settings 区新增 toggle（`data-testid="settings-automations-enabled-toggle"`）；`AutomationsDesk` 顶部新增引擎状态 Banner（`data-testid="automations-engine-banner"`）；复用已有 `fetchSettings` / `updateSettings`；更新 L1 单元测试。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npm run typecheck` ✅，`npm test -- src/__tests__/automations-desk.spec.tsx src/__tests__/models-settings-desk.spec.tsx` ✅。
- `KC-1502`：`Completed`。
  - User Outcome：V2 Shell Chat 模式下主舞台全高铺满，Composer 固定底部，Timeline 占满中部，移除旧 page-like header metrics 冗余，真正实现 conversation-first canvas 感。
  - Scope：`V2Shell.tsx` / `MainStage.tsx` / CSS 调整；不改任何 data-testid；确认 `npm run test:e2e -- tests/kc0112-bootstrap-chat-resume.spec.ts` 稳定通过。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npm run typecheck` ✅，`npm run build` ✅，`npm run test:e2e` ✅。
- `KC-1503`：`Completed`。
  - User Outcome：ChannelsDesk / PluginsDesk 在 V2 GlobalRail 导航后视觉与 test-id 稳定，Playwright E2E 补全 desk 导航用例，不出现 data-kc-view 缺失导致的 flaky test。
  - Scope：`kc0508-channels.spec.ts` / `kc0407-plugins.spec.ts` 补导航入口测试用例（只验证 desk 可见，不重构 desk 内部）；确认 `data-kc-view="channels"` / `data-kc-view="plugins"` 存在。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npm run test:e2e -- tests/kc0508-channels.spec.ts tests/kc0407-plugins.spec.ts` ✅。

## 迭代 16：Channel Session 权限策略 + 渠道侧文字审批

- 范围冻结：固化 Channel/Automation session 的工具调用权限边界（永远 PermissionMode.Auto，不产生 mid-turn approval），并实现通过渠道文字回复完成草稿审批的基础链路。详见 `docs/ITERATION_16_FREEZE.md`。
- `KC-1601`：`Completed 2026-03-20`。
  - User Outcome：Channel session 中工具调用永远不会中断 Agent turn，不产生 mid-turn Approval 记录，用户无需在 Web UI 处理工具级审批，turn 执行流畅不挂起。
  - Scope：`ChannelSessionOptions` 和 `AutomationSessionOptions` 的 `Permissions` 硬编码 `Mode="auto", RequireApprovalTools=[]`，忽略外部配置；4 条单元测试断言该约束。
  - Modules：`src/KodaClaw.Runtime`。
  - Verification：Unit 191/191 ✓，Integration 206/206 ✓，Contract 84/84 ✓。
- `KC-1602`：`Completed 2026-03-20`。
  - User Outcome：Agent 生成草稿后，用户在 Telegram（或任意渠道）直接回复 `ok [TOKEN]` / `no [TOKEN]` 即可批准或拒绝草稿，无需打开 Web UI；批准后消息立刻发出，拒绝后留下审计记录。
  - Scope：（1）`ChannelDeliveryGovernanceService` payloadJson 加 token（SHA256 前 3 字节，6位大写 hex），`BuildApprovalToken` public static；（2）`ChannelDeliveryEvaluationResult` 加 `ApprovalToken` 字段；（3）`ChannelDeliveryDispatchService` 新增 `SendNotificationAsync()`（best-effort，无审计）；（4）新增纯静态 `ChannelApprovalResponseParser`（识别 ok/yes/approve/send / no/cancel/reject + 可选 6位 hex token）；（5）`ChannelTurnOrchestrator` 前置审批响应检测：单 pending 自动匹配、多 pending + 无 token 返回 hint、有 token 精确匹配；通过 `SendNotificationAsync` 发回审批通知。
  - Modules：`src/KodaClaw.ChannelHub`。
  - Verification：Unit 191/191 ✓（含 40 条 KC-1601/1602 专项）；Integration 206/206 ✓；Contract 84/84 ✓。`make test-solution` 全绿。

## 迭代 17：前端追平三件套

- 范围冻结：关闭三个「后端已通但前端不可见/不可配」缺口。详见 `docs/ITERATION_17_FREEZE.md`。
- `KC-1701`：`Completed`。
  - User Outcome：用户在 Canvas Desk 能看到 Agent 通过 `canvas_upsert` 发布的所有产物，点击后可直接阅读内容（Markdown 渲染 / HTML 沙盒预览）。
  - Scope：`CanvasDesk.tsx` 实现产物列表 + 右侧内容渲染面板；复用已有 `GET /api/canvas` 和 `GET /api/canvas/{id}` API。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npm run typecheck`、`npm run test` 通过。
- `KC-1702`：`Completed`（已有，无需实现）。
  - 调查确认：AutomationsDesk 中运行历史功能在 Iter 14 已实现，前端已有运行记录展示，无额外工作。
- `KC-1703`：`Completed`。
  - User Outcome：用户在 Channels Desk 可直接切换每个 thread binding 的 Delivery Mode（AutoSend / DraftApproval / RequireApproval），无需手动调 API 或改 SQLite。
  - Scope：后端：`thread_bindings` 加 `delivery_mode_override` 列 + `PATCH /api/channels/threads/{id}/settings` 端点；前端：Channels Desk 线程详情加模式选择器。
  - Modules：`src/KodaClaw.ChannelHub`、`src/KodaClaw.Gateway`、`apps/kodaclaw-web`。
  - Verification：`dotnet test KodaClaw.sln -m:1` 全量通过（206 + 191 + 84 = 481 tests）。

## 迭代 18：Channel Full Agent Mode + 主动推送

- 范围冻结：Channel session（DM + Group）从 JSON-only 协议切换到 Full Agent 模式（可调用工具、可分步回复），并新增 `channel_send` / `channel_list` 工具实现跨 session 主动推送。详见 `docs/ITERATION_18_FREEZE.md`。

- `KC-1801`：`Completed 2026-03-21`。
  - User Outcome：任意 session（DM/Group/Automation/Main）中 Agent 均可调用 `channel_send` 向已绑定渠道发消息，始终 AutoSend，无需走 DraftApproval 审批流。
  - Scope：新增 `src/KodaClaw.Runtime/ChannelSendTool.cs`，Args: `bindingId`(required), `text`(required)；调用 `ChannelDeliveryDispatchService.SendNotificationAsync`（best-effort，已有服务）；注册到全局 tool registry（`ServiceCollectionExtensions.cs`）；加入 `ChannelSessionOptions.Tools` 和 `AutomationSessionOptions.Tools`；`DefaultWorkspaceTemplates.Agents()` 加使用引导。
  - Modules：`src/KodaClaw.Runtime`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter ChannelSend`（L1 x4）；`dotnet build KodaClaw.sln`。

- `KC-1802`：`Completed 2026-03-21`。
  - User Outcome：Automation/Main session 的 Agent 可调用 `channel_list` 获取所有已绑定渠道的 `bindingId`，从而配合 `channel_send` 实现定向推送（如任务完成后发到用户的 Telegram DM）。
  - Scope：新增 `src/KodaClaw.Runtime/ChannelListTool.cs`，Args: `connectorKind?`(optional filter)；调用 `IThreadBindingRepository.QueryAsync`；返回 `[{ bindingId, displayTitle, connectorKind, threadType, lastInboundAt }]`；注册到 `AutomationSessionOptions.Tools` 和 `MainSessionOptions.DefaultTools`；`DefaultWorkspaceTemplates.Agents()` 加使用引导。
  - Modules：`src/KodaClaw.Runtime`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter ChannelList`（L1 x3）；`dotnet build KodaClaw.sln`。

- `KC-1803`：`Completed 2026-03-21`。
  - User Outcome：从 Telegram DM 或 Group 发消息后，Koda 能调用工具（fs_read、bash_run 等）多步推进并通过 `channel_send` 分步回复；体验与主会话等同。
  - Scope：（1）`ChannelSessionService.BuildInboundTurnPrompt` 去除 JSON 约束，改为"自由执行 + 用 channel_send 回复"引导语；（2）`ChannelSessionService.RunInboundTurnAsync` 去除 `ParseProposal(JSON)` 调用，直接以 RunAsync 成功/失败决定 outcome；（3）`ChannelTurnOrchestrator.ProcessInboundAsync` 去除 proposal 解析 → Dispatch 路径（仍保留审批响应检测和 RequireExplicitMention 前置门），改为 run → 记录 `Delivered`/`NoAction` outcome；（4）更新 system prompt：Group 仍保留 RequireExplicitMention 引导，系统提示中明确注入 BindingId 供 agent 调用 channel_send 使用。
  - Modules：`src/KodaClaw.Runtime`、`src/KodaClaw.ChannelHub`。
  - Verification：`dotnet test tests/KodaClaw.IntegrationTests --filter ChannelFullAgent`（L2 x4）；`dotnet test KodaClaw.sln -m:1`；L5 Dogfood：Telegram DM 发"帮我列出工作目录" → Koda 调用 fs_list 并回复。

## 迭代 19：Skills 系统集成

- 范围冻结：接入 SDK 已完整实现的 Skills 系统，建立三层加载路径（内置 / workspace / 全局），打通 Agent 自安装 skill 的完整闭环，并提供 Web SkillsDesk 可视化入口。详见 `docs/ITERATION_19_FREEZE.md`。

- `KC-1901`：`Completed 2026-03-21`。
  - User Outcome：KodaClaw 开箱即有一个内置 skill（`koda-workspace`，KodaClaw workspace 协议完整指南），Agent 可直接 skill_list 发现并激活；用户将 SKILL.md 写入 `workspace/skills/` 即完成"安装"，下次 session 自动发现。
  - Scope：（1）在项目根新建 `skills/koda-workspace/SKILL.md`（frontmatter: name/description/license + Markdown body 覆盖 workspace 协议要点）；（2）`.csproj` 将 `skills/**` 设为 Content CopyToOutputDirectory；（3）`WorkspaceService.InitializeAsync()` 创建 `workspace/skills/` 目录；（4）新增 `IWorkspaceService.GetSkillsPaths()` 返回三层路径列表（`[AppDir/skills, ~/.agents/skills, workspaceRoot/workspace/skills]`，不存在的路径自动跳过）；（5）`DefaultWorkspaceTemplates.Agents()` 补充 skills 自安装引导（skill_list/skill_activate/skill_resource + fs_write 创建流程）。
  - Modules：`src/KodaClaw.Workspace`、`src/KodaClaw.Runtime`、`skills/`（新目录）。
  - Verification：`dotnet test tests/KodaClaw.ContractTests --filter Skills`（L3 x3，验证内置 skill 格式、路径约定、AGENTS.md 引导存在）；`dotnet build KodaClaw.sln`。

- `KC-1902`：`Completed 2026-03-21`。
  - User Outcome：主会话、Channel session（DM/Group）、Automation session 中，`skill_list` 真正返回已发现的 skills，`skill_activate` 成功注入领域知识到 session context；三个工具不再静默无效。
  - Scope：`MainSessionService.CreateAgentConfig()`、`ChannelSessionService.CreateAgentConfig()`、`AutomationSessionService.CreateAgentConfig()` 均加入 `Skills = new SkillsConfig { Paths = _workspaceService.GetSkillsPaths(), ValidateOnLoad = true }`；`IWorkspaceService` 注入三类 session service。
  - Modules：`src/KodaClaw.Runtime`。
  - Verification：`dotnet test tests/KodaClaw.IntegrationTests --filter SkillsIntegration`（L2 x4：main session skill_list 返回 koda-workspace；skill_activate 执行不报错；channel session skills 可用；automation session skills 可用）；`dotnet test KodaClaw.sln -m:1`。

- `KC-1903`：`Completed 2026-03-21`。
  - User Outcome：Web SkillsDesk 列出三层可用 skills，标注来源（built-in / workspace / global），用户可浏览 name/description/path，了解当前可激活哪些 skill。
  - Scope：（1）Gateway `GET /api/skills`：扫描三层路径，聚合并返回 `[{ name, description, source, path, hasResources }]`（source 枚举：BuiltIn/Workspace/Global）；（2）新增 `SkillsDesk.tsx` 组件：列表 + 来源标签，无激活入口（激活由 Agent 在对话中完成）；（3）GlobalRail 添加 skills 导航入口。
  - Modules：`src/KodaClaw.Gateway`、`apps/kodaclaw-web`。
  - Verification：`dotnet test tests/KodaClaw.IntegrationTests --filter SkillsApiIntegration`（L2 x2）；`npm run typecheck`；`npm run test`（SkillsDeskSpec）；`dotnet test KodaClaw.sln -m:1`。

## Bug Fixes（迭代间补录）

- `KC-BUG-001`：`Completed 2026-03-21`。
  - 症状：`skill_list` 始终返回 0 个 skills，三层路径均无法被发现。
  - 根因：`LocalSandbox.EnforceBoundary = true` + `WorkingDirectory = sessionDirectory` 会对 sessionDirectory 外的所有路径抛 `UnauthorizedAccessException`；`SkillsLoader` 的 `DiscoverAsync` 静默吞掉该异常，导致 0 discoveries。
  - 修复：`MainSessionService`、`ChannelSessionService`、`AutomationSessionService` 的 `CreateAgentConfig` 和 resume 路径 `AgentConfigOverrides` 均加入 `SandboxOptions.AllowPaths = skillsPaths`。
  - 受影响模块：`src/KodaClaw.Runtime`。
  - Verification：`dotnet test KodaClaw.sln -m:1`（494 tests 全通过）。

- `KC-BUG-002`：`Completed 2026-03-21`。
  - 症状：`workspace/channels/{id}/SUMMARY.md` 每行内容全部相同（`Agent turn completed; reply delivered via channel_send.`），对下一个 session 无上下文参考价值。
  - 根因：Iteration 18 切换到 Full Agent Mode 后，`ChannelTurnOrchestrator` 无法感知 Agent 内部调用 `channel_send` 发出的文本，退化为硬编码字符串。
  - 修复：新增 `IChannelSendCapture`（singleton，ConcurrentDictionary 按 bindingId 队列）；`ChannelSendService.SendAsync` 成功后调用 `capture.Record`；`ChannelTurnOrchestrator` 在 turn 完成后调用 `GetAndClear` 并构建 `user: "..." → koda: "..."` 格式的对话摘要。
  - 受影响模块：`src/KodaClaw.ChannelHub`。
  - Verification：`dotnet test KodaClaw.sln -m:1`（494 tests 全通过）。

## 迭代 20：Memory Pipeline & Session Continuity

- 范围冻结：打通记忆流水线的四处断点——启用 SDK ContextManager 防止主会话 token 超限、nightly consolidation 改为默认开启、SUMMARY.md 滚动窗口防止 channel 记忆膨胀、主会话加载昨天的 daily memory 消除跨日期缺口。详见 `docs/ITERATION_20_FREEZE.md`。

- `KC-2001`：`Completed 2026-03-21`。
  - User Outcome：主会话连续使用 200+ 轮后，SDK 自动压缩旧对话历史，会话继续正常工作，不因 context 爆满崩溃或行为退化。
  - Scope：`MainSessionOptions` 新增 `CompressionModel`（默认 `claude-haiku-4-5-20251001`）、`ContextMaxTokens`（默认 80_000）、`ContextCompressToTokens`（默认 50_000）；`MainSessionService.CreateAgentConfig` 加入 `Context = new ContextManagerOptions { ... }`；resume 路径 `AgentConfigOverrides` 同步加 Context 字段。
  - Modules：`src/KodaClaw.Runtime`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter MainSessionOptions`（L1）；`dotnet build KodaClaw.sln`。

- `KC-2002`：`Completed 2026-03-21`。
  - User Outcome：新创建 workspace 的 HEARTBEAT.md 中，`Nightly Memory Consolidation` 默认 `enabled: true`，多天使用后 MEMORY.md 自动积累已学到的事实，无需用户手动启用。
  - Scope：`DefaultWorkspaceTemplates.Heartbeat()` 中 `Nightly Memory Consolidation` 的 `enabled: false` → `enabled: true`；同步更新 contract test golden 文件。
  - Modules：`src/KodaClaw.Workspace`、`tests/KodaClaw.ContractTests`。
  - Verification：`dotnet test tests/KodaClaw.ContractTests --filter WorkspaceTemplate`（L3）；`dotnet build KodaClaw.sln`。

- `KC-2003`：`Completed 2026-03-21`。
  - User Outcome：无论 channel 聊了多久，SUMMARY.md 始终保持最近 30 轮对话（60 行）；注入到 system prompt 时不会因文件过大而截断最新内容。
  - Scope：`ChannelThreadSummaryWriter.WriteAsync` 写入后读取文件行数，若超过 60 行则删除头部多余行（保留尾部 60 行）；并发安全（单文件写入已是顺序调用，无并发问题）。
  - Modules：`src/KodaClaw.ChannelHub`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter ChannelThreadSummaryWriter`（L1，含截断边界 x3）；`dotnet build KodaClaw.sln`。

- `KC-2004`：`Completed 2026-03-21`。
  - User Outcome：跨天对话或间隔一天后重新使用时，昨天写入的 `memory/YYYY-MM-DD.md` 内容仍会出现在主会话 system prompt 中，不会出现记忆断层。
  - Scope：`MainSessionService.BuildSystemPromptAsync` 在加载 `memory/{今天}.md` 后，额外尝试加载 `memory/{昨天}.md`（文件不存在时静默跳过）；两个文件都受 `seenPaths` 去重保护。
  - Modules：`src/KodaClaw.Runtime`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter MainSessionBuildSystemPrompt`（L1，今天 + 昨天均存在 x1，只有昨天 x1，都不存在 x1）；`dotnet build KodaClaw.sln`。

- `KC-BUG-003`：`Completed 2026-03-21`。
  - 症状：`ChannelSessionService` 和 `AutomationSessionService` 创建的 Agent 没有 Context 压缩保护，channel 长跑会话或 automation 任务的 token 会无限累积，最终触达模型硬性上限。
  - 根因：Iteration 20 补齐 `MainSessionService` 时遗漏了另外两个 session 类型，也未在对应 Options 类中添加压缩配置字段。
  - Scope：`ChannelSessionOptions` / `AutomationSessionOptions` 新增 `CompressionModel`、`ContextMaxTokens`、`ContextCompressToTokens` 字段（与 Iter 20 保持一致的默认值）；`ChannelSessionService.CreateAgentConfig` / resume `AgentConfigOverrides`、`AutomationSessionService.CreateAgentConfig` 均补加 `Context = new ContextManagerOptions { ... }`。
  - 受影响模块：`src/KodaClaw.Runtime`。
  - Verification：`dotnet build KodaClaw.sln`（L0）；`dotnet test tests/KodaClaw.UnitTests -m:1`（L1，210/210）。

## 迭代 21：Context Intelligence & Session Lifecycle

范围冻结：见 `docs/ITERATION_21_FREEZE.md`（2026-03-21）。

- `KC-2101`：`Completed 2026-03-21`。
  - User Outcome：使用任何 provider（Anthropic / OpenAI）和任何模型，上下文压缩都能可靠工作；不同 context window 大小的模型触发阈值自动适配；用户无需配置任何压缩相关参数。
  - Scope：删除三个 Options 类中的 `CompressionModel` / `ContextMaxTokens` / `ContextCompressToTokens`；新增 `ContextCompressionTriggerRatio`（默认 0.75）/ `ContextCompressionTargetRatio`（默认 0.40）；`ModelEndpointConfig` 加 `ContextWindowSize`（默认 128_000）；三个 SessionService 的 `CreateAgentConfig` 从 ModelHub 读 window size 后按比例计算阈值，不设 `CompressionModel`（SDK 用主模型）。
  - Modules：`src/KodaClaw.Runtime`、`src/KodaClaw.ModelHub`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter SessionOptions`（L1）；`dotnet test tests/KodaClaw.IntegrationTests --filter ContextManager`（L2）；`dotnet build KodaClaw.sln`。

- `KC-2102`：`Completed 2026-03-21`。
  - User Outcome：Nightly Memory Consolidation 执行后，已整合的 `memory/YYYY-MM-DD.md` 日记文件被自动删除；`workspace/memory/` 目录只保留最近 7 天的文件，磁盘占用可预期。
  - Scope：`DefaultWorkspaceTemplates.Heartbeat()` 的 Nightly Consolidation section 补充 `fs_rm` 清理指令和 7 天保留说明。
  - Modules：`src/KodaClaw.Workspace`。
  - Verification：`dotnet test tests/KodaClaw.ContractTests --filter WorkspaceTemplate`（L3）；`dotnet build KodaClaw.sln`。

- `KC-2103`：`Completed 2026-03-21`。
  - User Outcome：长期使用后 MEMORY.md 保持合理大小（目标 <200 行），不会撑爆 system prompt；旧的、已被覆盖的事实自动被淘汰。
  - Scope：`DefaultWorkspaceTemplates.Heartbeat()` 的 Nightly Consolidation section 补充 90 天保留规则和大小约束描述。
  - Modules：`src/KodaClaw.Workspace`。
  - Verification：`dotnet test tests/KodaClaw.ContractTests --filter WorkspaceTemplate`（L3）；`dotnet build KodaClaw.sln`。

- `KC-2104`：`Completed 2026-03-21`。
  - User Outcome：用户可在 Web UI 点击"新对话"按钮，清空当前 session 历史，开启全新对话；workspace 记忆文件（MEMORY.md 等）不受影响。
  - Scope：`POST /api/sessions/rotate` Gateway 端点（删除当前 main session SDK store，不触动 workspace 文件）；V2 shell GlobalRail 或 Chat header 加"新对话"按钮（含确认弹窗，`data-testid="new-session-button"`）。
  - Modules：`src/KodaClaw.Gateway`、`src/KodaClaw.Runtime`、`apps/kodaclaw-web`。
  - Verification：`dotnet test tests/KodaClaw.IntegrationTests --filter SessionRotate`（L2）；`npx playwright test --grep new-session`（L4）；`dotnet build KodaClaw.sln`。

## 迭代 22：Channel-First 体验补全

范围冻结：见 `docs/ITERATION_22_FREEZE.md`（2026-03-21）。

- `KC-2201`：`Pending`。
  - User Outcome：Telegram thread 超过 7 天未活动后，下一条消息开启全新 channel session，不再 resume 陈旧 SUMMARY.md，Koda 不会出现"记忆混乱"。
  - Scope：`ChannelSessionOptions` 新增 `SessionTimeoutDays`（默认 7）；`ChannelSessionService.EnsureChannelSessionAsync` 在 resume 前检查 `ThreadBinding.LastMessageAt`，超期则清除 `ActiveSessionId` 走新建路径；0 = 永不过期。
  - Modules：`src/KodaClaw.Runtime`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter ChannelSessionTimeout`（L1 x4）；`dotnet test tests/KodaClaw.IntegrationTests --filter ChannelSessionTimeout`（L2 x2）；`dotnet build KodaClaw.sln`。

- `KC-2202`：`Pending`。
  - User Outcome：ChannelsDesk 每 30 秒自动刷新，有 pending channel delivery approval 时 GlobalRail Channels 导航项显示角标，用户无需手动 F5 才能发现新 thread。
  - Scope：`ChannelsDesk.tsx` 加 30s 轮询（`useInterval`）；GlobalRail Channels 项读取 pending approval count，>0 时渲染 `.v2-global-rail__badge`；加 `data-testid="channels-refresh-indicator"`。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npm run typecheck`；`npm test -- src/__tests__/channels-desk.spec.tsx`；`npm run build`。

- `KC-2203`：`Pending`。
  - User Outcome：用户在 ChannelsDesk Web UI 中即可添加 Telegram bot（填入 bot token）、设置 delivery rule，无需改配置文件或重启 Gateway，connector 自动热启动。
  - Scope：后端新增 `POST /api/channels/accounts`、`PATCH /api/channels/accounts/{id}`、`DELETE /api/channels/accounts/{id}`；`ChannelConnectorHostedService` 响应热重载信号（start/stop 单个 connector）；前端 ChannelsDesk 加 "Add Channel" 向导（选类型 → 填配置 → 设 delivery rule → 保存）和账号编辑入口；Telegram bot token 通过 Keychain 存储（SecretRef 模式）。
  - Modules：`src/KodaClaw.ChannelHub`、`src/KodaClaw.Gateway`、`apps/kodaclaw-web`。
  - Verification：`dotnet test tests/KodaClaw.IntegrationTests --filter ChannelAccountApi`（L2 x4）；`npm test -- src/__tests__/channels-desk.spec.tsx`；`dotnet test KodaClaw.sln -m:1`。

- `KC-2204`：`Pending`。
  - User Outcome：收到 ChannelDelivery approval 的 macOS 桌面通知时，通知上直接有"✓ 发送" / "✗ 不发送"按钮，点击后无需打开 Web 即完成审批。
  - Scope：Desktop `main.ts` 对 `ApprovalKind.ChannelDelivery` 类型通知附加 macOS notification action buttons；监听 `notification-action` 事件后调用 `POST /api/approvals/{id}/approve|reject`（使用已有 Keychain token）；其他 ApprovalKind 保持原有跳 Web 行为不变。
  - Modules：`apps/kodaclaw-desktop`。
  - Verification：`cd apps/kodaclaw-desktop && npm run test`；`npm run smoke:wave3`；`npm run build`。

- `KC-2205`：`Pending`。
  - User Outcome：AutomationsDesk 每个 automation 条目旁有"▶ 立即执行"按钮，点击后立即触发一次执行，runs 列表自动刷新显示新记录，用户无需等待 cron 调度即可测试 automation。
  - Scope：后端新增 `POST /api/automations/{id}/trigger`（创建 Queued run，`AutomationScheduler` 下次 tick 时执行，返回 `{ ok, runId }`）；前端 AutomationsDesk 加触发按钮（`data-testid="automation-trigger-{id}"`），enabled 状态可点击，点击后 optimistic UI + 刷新 runs 列表。
  - Modules：`src/KodaClaw.Automation`、`src/KodaClaw.Gateway`、`apps/kodaclaw-web`。
  - Verification：`dotnet test tests/KodaClaw.IntegrationTests --filter AutomationTrigger`（L2 x3）；`npm test -- src/__tests__/automations-desk.spec.tsx`；`dotnet test KodaClaw.sln -m:1`。

- `KC-2206`：`Pending`。
  - User Outcome：Telegram 长线程使用后，Koda 对早期对话内容不再"失忆"——超阈值时旧条目以 LLM 段落摘要保留，而非直接截断丢弃。
  - Scope：`ChannelSessionOptions` 新增 `SummaryCompressionThreshold`（默认 80）、`SummaryCompressionTargetLines`（默认 40）；`ChannelThreadSummaryWriter.WriteAsync` 超阈值时调用 `IModelProvider.CompleteAsync` 生成段落摘要，替换头部条目为 `## Compressed History (timestamp)` 块 + 保留尾部 40 行；LLM 调用失败时回退到截断（不阻塞 turn）。
  - Modules：`src/KodaClaw.ChannelHub`、`src/KodaClaw.Runtime`。
  - Verification：`dotnet test tests/KodaClaw.UnitTests --filter ChannelThreadSummaryWriter`（L1，含压缩路径 x3、失败回退 x1）；`dotnet test KodaClaw.sln -m:1`。

- `KC-2207`：`Pending`。
  - User Outcome：CanvasDesk 打开 `contentType=markdown` 的 artifact 时，内容以富文本 HTML 直接渲染（支持表格、任务列表、代码块），不依赖 iframe；HTML artifact 保持 sandbox iframe。
  - Scope：前端 CanvasDesk detail panel 根据 `contentType` 分路：`markdown` → 调用 `/api/canvas/{id}/entry` 获取原文，用 `react-markdown` + `remark-gfm` 渲染，`data-testid="canvas-artifact-content"`；`html` → 保持现有 iframe；前端依赖新增 `react-markdown`、`remark-gfm`。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npm test -- src/__tests__/canvas-desk.spec.tsx`；`npm run typecheck`；`npm run build`。

## 迭代 23：引导程序基础数据层

范围冻结：见 `docs/ITERATION_23_FREEZE.md`（2026-03-21）。

- `KC-2301`：`Completed`（2026-03-21）。
  - User Outcome：用户在添加模型时可从主流模型预设列表中直接选择，无需手动填写模型名称、context window 等参数；Models Settings Desk 体验大幅简化。
  - Scope：内置静态预设列表（≥10 个，覆盖 Anthropic / OpenAI / DeepSeek / Google / Ollama），存为嵌入资源 `Resources/model-presets.json`；新增 `ModelPresetService`；Gateway `GET /api/models/presets`、`GET /api/models/presets/{presetId}`。
  - Modules：`src/KodaClaw.Contracts`、`src/KodaClaw.Gateway`。
  - Verification：`dotnet build KodaClaw.sln` ✓（0 警告 0 错误）；`npm run typecheck` ✓；`npm run test` ✓（50 tests）。

- `KC-2302`：`Completed`（2026-03-21）。
  - User Outcome：用户填写 API Key 后立刻点击"测试连接"，几秒内得到明确的成功/失败反馈，不需要等到第一次真实对话才发现配置错误。
  - Scope：新增 `ModelConnectionTestService`（发送 max_tokens=1 的最小 API 调用）；Gateway `POST /api/models/test-connection`；API Key 不持久化，仅在请求生命周期内使用。
  - Modules：`src/KodaClaw.Gateway`。
  - Verification：`dotnet build KodaClaw.sln` ✓（0 警告 0 错误）；`npm run typecheck` ✓。

- `KC-2303`：`Completed`（2026-03-21）。
  - User Outcome：引导程序中用户可从 6 个内置 Persona 模板选择 Koda 的人格风格，不需要从空白开始描述；每个模板有清晰的标语、描述和 SOUL.md 预览。
  - Scope：内置 6 个模板（极简执行者 / 深度分析师 / 创意伙伴 / 耐心向导 / 务实顾问 / 均衡默认），存为嵌入资源 `Resources/persona-presets.json`；新增 `PersonaPresetService`；Gateway `GET /api/workspace/persona-presets`、`GET /api/workspace/persona-presets/{presetId}`；`POST /api/onboarding/apply-persona` 写入 SOUL.md / IDENTITY.md。
  - Modules：`src/KodaClaw.Contracts`、`src/KodaClaw.Gateway`。
  - Verification：`dotnet build KodaClaw.sln` ✓（0 警告 0 错误）；`npm run typecheck` ✓；`npm run test` ✓（50 tests）。

- `KC-2304`：`Completed`（2026-03-21）。
  - User Outcome：用户在引导程序中途关闭窗口后，下次打开从中断步骤续接，不需要重头来过。
  - Scope：`OnboardingState` 数据模型（currentStepId / completedSteps / selectedPresetId 等）；`OnboardingStateService` 读写 `config/onboarding.json`；`GET /api/onboarding/state`、`PUT /api/onboarding/state`、`POST /api/onboarding/complete`、`POST /api/onboarding/reset`。
  - Modules：`src/KodaClaw.Contracts`、`src/KodaClaw.Workspace`、`src/KodaClaw.Gateway`。
  - Verification：`dotnet build KodaClaw.sln` ✓（0 警告 0 错误）；`npm run typecheck` ✓；`npm run test` ✓（50 tests）。

## 迭代 24：首次使用引导程序

范围冻结：见 `docs/ITERATION_24_FREEZE.md`（2026-03-21）。前置依赖：Iter 22（Channel 账号绑定 API）+ Iter 23（全部四项）。

- `KC-2401`：`Pending`。
  - User Outcome：全新安装的 KodaClaw 启动后自动进入引导程序，已完成引导的用户直接进入主界面；引导程序可从 Settings 重新触发。
  - Scope：`App.tsx` 启动时检查 `GET /api/onboarding/state`，`isCompleted=false` 时渲染全屏 `OnboardingShell`；右上角提供"跳过"入口；Settings 页加"重新引导"按钮（调用 `POST /api/onboarding/reset`）。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npm test -- src/__tests__/onboarding-shell.spec.tsx`；`npm run typecheck`。

- `KC-2402`：`Pending`。
  - User Outcome：引导程序第一步选择界面语言，点击即选，立刻影响后续所有引导文案及 Koda 默认回复语言。
  - Scope：全屏居中两张大卡片（中文 / English）；点击后写 `localStorage["kodaclaw.locale"]`，推进 onboarding 状态到 `model` 步骤。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npm test -- src/__tests__/onboarding-shell.spec.tsx`（语言切换后 locale 正确）。

- `KC-2403`：`Pending`。
  - User Outcome：用户在引导中选择 Provider → 看到推荐模型 → 填入 API Key → 立刻测试连通 → 成功后配置自动保存，整个流程不超过 3 分钟。
  - Scope：Provider 选择卡片（5 个）→ 模型列表（来自预设 API）→ API Key 输入框 + "如何获取 Key"展开说明 + "测试连接"按钮 → 连通性测试结果 → 成功后调用 `POST /api/models` 保存并设为 default；消费 KC-2301 / KC-2302。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npm test -- src/__tests__/onboarding-shell.spec.tsx`（mock 连通性测试成功/失败分支）；`npx playwright test tests/kc2401-onboarding.spec.ts`（L4）。

- `KC-2404`：`Pending`。
  - User Outcome：用户选择一种 Koda 人格模板后，SOUL.md 立刻被写入对应内容，下一次主会话启动时 Koda 的行为风格即生效。
  - Scope：6 张 Persona 卡片（来自预设 API）；选中后展开 SOUL.md 前 5 条准则预览；"使用这个风格"调用 `POST /api/onboarding/apply-persona`（写入 SOUL.md）；"让 Koda 自己来了解我"跳过（不写 SOUL.md，Bootstrap 对话决定）；消费 KC-2303。
  - Modules：`apps/kodaclaw-web`、`src/KodaClaw.Gateway`（apply-persona 端点）。
  - Verification：`npm test -- src/__tests__/onboarding-shell.spec.tsx`（apply-persona 后 onboarding state 更新）。

- `KC-2405`：`Pending`。
  - User Outcome：在引导程序中完成 Telegram Bot 创建和绑定，包含 BotFather 步骤说明、token 验证和 Delivery Rule 选择，整个流程可在引导内完成，无需跳转其他页面。
  - Scope：4 个子步骤内嵌向导（说明 → BotFather 步骤 → Token 输入 + 验证 → Delivery Rule 选择）；`POST /api/channels/test-telegram-token`（新增，验证 token 有效返回 bot 名称）；成功后调用 `POST /api/channels/accounts`（KC-2203）；步骤可整体跳过。
  - Modules：`apps/kodaclaw-web`、`src/KodaClaw.Gateway`。
  - Verification：`npm test -- src/__tests__/onboarding-shell.spec.tsx`（跳过路径；mock token 验证成功路径）。

- `KC-2406`：`Pending`。
  - User Outcome：引导完成后看到配置摘要和 3 条具体行动建议，点击"开始使用"直接进入主对话界面，立刻可以和 Koda 交谈。
  - Scope：完成页展示配置摘要（已选模型 / 人格 / 是否绑定 Telegram）+ 动态行动建议列表（根据已完成步骤生成）；"开始使用"调用 `POST /api/onboarding/complete` 后跳转到 `mainDesk="chat"`。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npx playwright test tests/kc2401-onboarding.spec.ts`（完整引导流程 E2E）。

- `KC-2407`：`Pending`。
  - User Outcome：在 Models Settings Desk 添加模型时，可从预设列表一键填充模型参数，不再需要手动查阅模型名称和 context window 大小。
  - Scope：ModelsSettingsDesk 添加模型表单上方加"从预设选择"区域（Provider 下拉 → 模型列表）；选中后自动填充 modelId / baseUrl / contextWindowSize；保留手动修改能力；复用连通性测试按钮。
  - Modules：`apps/kodaclaw-web`。
  - Verification：`npm test -- src/__tests__/models-settings-desk.spec.tsx`；`npx playwright test tests/kc0212-models-settings.spec.ts`。
