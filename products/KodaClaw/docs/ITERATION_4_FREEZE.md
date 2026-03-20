# Iteration 4 Freeze: Plugin Platform v1

Last updated: 2026-03-19
Status: `Frozen` / `Accepted`

这份文档用于冻结 Iteration 4 的产品范围、contract surface、验收标准与并行写集，确保后续 subagent 可以在不互相猜边界的情况下并行实现。

## 1. 目标

Iteration 4 的目标不是一次性把完整插件生态做完，而是把 KodaClaw 的长期扩展总线真正立起来：

- 不改核心代码即可新增一组 MCP 工具能力
- 插件具备被发现、安装、信任、启停、观测的完整闭环
- 插件故障不会拖垮宿主
- Runtime 能按策略把插件工具注入 session
- Web 有可管理的 plugin manager desk

## 2. 冻结后的首期范围

### 2.1 本期必须做实的能力

- `plugin.json` manifest v1
- 本地目录发现与本地路径安装
- bundled plugin 发现
- 插件注册、状态持久化、启停
- `stdio` MCP 插件宿主
- namespaced MCP tools 注入 Runtime
- 权限声明、trust/enable 基线、审批复用
- per-plugin logs、health、degraded 标记
- Plugin manager Web 页面
- 至少 1 个内置 fixture plugin

### 2.2 本期只做“识别与展示”，不做完整运行链路的能力

以下类型允许出现在 manifest 中，但 Iteration 4 不要求端到端运行：

- `channel`
- `memory`
- `ui`

它们在本期的要求只有：

- manifest 校验能识别这些类型
- registry / Web 页面能展示这些类型和 capability
- 不进入完整 host -> runtime -> web 插件执行验收链

换句话说，Iteration 4 v1 真正跑通的只有 `tool` 插件。

### 2.3 本期明确非目标

- 远程插件市场
- 签名校验链路
- 热升级 / 回滚
- 复杂 UI sandbox / iframe bridge
- HTTP / Streamable HTTP / SSE 插件作为验收主路径
- 渠道型插件的外部消息路由闭环

## 3. 复用现有 SDK / 产品基线的结论

现有代码已经具备以下可复用基线：

- `KodaClaw.Contracts` 已预留：`SessionKind.Plugin`、`ApprovalKind.PluginAuthorization`、`InboxItemKind.PluginRequest`、`CanvasArtifactKind.PluginPanel`
- `KodaClaw.Workspace` 已创建 `workspace/plugins` 目录与 `config/plugins.json` 占位文件
- `KodaClaw.Runtime` 已通过 `IToolRegistry` + `AgentConfig.Tools` 控制工具可见性
- SDK 已具备 `Kode.Agent.Mcp`：`McpConfig`、`McpClientManager`、`McpToolProvider`，可直接复用 MCP 工具接入能力
- Control Plane 已具备 approvals / inbox / diagnostics / settings / shared SQLite baseline，可承载插件状态、授权与审计

这意味着 Iteration 4 不需要发明一套新的插件执行内核；重点是把 PluginHost、Gateway、Runtime 注入与 Web 管理面串起来。

## 4. 冻结的 contract 决策

## 4.1 Manifest v1

Manifest 必填字段冻结为：

- `id`
- `name`
- `version`
- `types`
- `runtime`
- `permissions`
- `capabilities`

Manifest 选填字段冻结为：

- `display`
- `healthcheck`
- `configSchema`

Manifest 示例语义继续沿用 `docs/PLUGIN_SPEC.md`，但 Iteration 4 的验收只强制 `runtime.transport = stdio`。

## 4.2 Runtime transport 策略

- Iteration 4 必须做实：`stdio`
- Iteration 4 可保留类型定义但不要求验收闭环：`http` / `streamableHttp` / `sse`

这样既不阻断未来扩展，也不会把首期实现面铺得过大。

## 4.3 生命周期模型

Iteration 4 不把文档里的全状态机直接压成一个大枚举，而是冻结成两层状态：

### 持久化状态

- `Installed`
- `TrustState`：`Untrusted` / `Trusted`
- `Enabled`：`true` / `false`

### 运行态状态

- `Stopped`
- `Starting`
- `Running`
- `Degraded`

并且补充运行态字段：

- `LastStartedAt`
- `LastStoppedAt`
- `LastHealthAt`
- `RestartCount`
- `LastError`

这样更适合 SQLite 持久化、API 序列化和 Web 页面展示，也更利于后续把 `Disabled`、`Stopped`、`Degraded` 等语义拆清。

## 4.4 权限与授权模型

- 所有插件第一次从 `Untrusted -> Trusted` 都必须走统一授权链
- 授权模型复用现有 `ApprovalKind.PluginAuthorization` 与 `InboxItemKind.PluginRequest`
- Web 不新起一套独立审批系统；插件授权仍通过 Control Plane 进入统一 inbox / approval surface
- 高风险权限在授权说明中必须明确展示，但不新增独立审批类型

Iteration 4 持续展示的权限类别冻结为：

- `filesystem`
- `network`
- `notifications`
- `background`
- `channels`
- `uiPanels`
- `secrets`

## 4.5 Runtime 工具注入规则

只允许以下插件工具被注入 session：

- manifest 校验通过
- 已注册到 registry
- `TrustState = Trusted`
- `Enabled = true`
- `RuntimeState = Running`

工具注入规则冻结为：

- 工具必须经过 PluginHost -> MCP manager -> Runtime，不允许插件直接改 Runtime
- 工具名必须 namespaced，格式固定为 `mcp__<pluginId>__<toolName>`
- Runtime 每次创建 session 时都根据当前插件快照构建“有效工具列表”
- timeline / diagnostics / tool metadata 必须保留 `pluginId` 与 `source = plugin`

## 4.6 安装来源与发现路径

Iteration 4 只支持两类安装来源：

- `Bundled`
- `LocalDirectory`

发现路径冻结为：

- `~/.kodaclaw/workspace/plugins/<pluginId>/`
- 产品 bundled plugins 目录

允许通过 Gateway API 提交一个本地路径进行安装，但不做远程拉取。

## 4.7 Plugin settings / UI 占位

Iteration 4 的“插件设置页”只做到：

- 展示 manifest 中的 `configSchema` 摘要
- 展示当前配置快照 / 原始 JSON 占位
- 提供未来设置面板挂载点说明

不做任意 UI 插件执行容器。

## 5. 建议冻结的 shared contract surface

下列 contract 建议在实现开始前由主线程先落到 `KodaClaw.Contracts`：

| 类型 | 建议名称 | 说明 |
| --- | --- | --- |
| Enum | `PluginType` | `Tool` / `Channel` / `Memory` / `Ui` |
| Enum | `PluginTransportKind` | `Stdio` / `Http` / `StreamableHttp` / `Sse` |
| Enum | `PluginInstallSource` | `Bundled` / `LocalDirectory` |
| Enum | `PluginTrustState` | `Untrusted` / `Trusted` |
| Enum | `PluginRuntimeState` | `Stopped` / `Starting` / `Running` / `Degraded` |
| Record | `PluginManifest` | 对应 manifest v1 的共享 DTO |
| Record | `PluginRuntimeSpec` | transport、command/url、args、env、headers |
| Record | `PluginPermissionSet` | permissions 归一化表示 |
| Record | `PluginCapabilitySet` | tools / channels / uiPanels / memoryProviders |
| Record | `PluginRecord` | registry 持久化实体 |
| Record | `PluginSummary` | 插件列表页 DTO |
| Record | `PluginDetail` | 插件详情页 DTO |
| Record | `PluginLogEntry` | 插件日志行 DTO |
| Record | `PluginQuery` | `/api/plugins` 过滤条件 |
| Record | `PluginsQueryResponse` | 插件列表响应 |
| Record | `InstallLocalPluginRequest` | 本地安装请求 |
| Record | `PluginStateUpdateRequest` | trust / enable / disable / start / stop 动作请求 |
| Interface | `IPluginRegistryRepository` | 插件注册/状态持久化接口 |
| Interface | `IPluginLogRepository` | 插件日志查询接口 |

实现注意：

- `PluginRecord` 应承载“manifest 快照 + 持久化状态 + 运行态摘要”，避免 Gateway 再做过多拼装
- `PluginManifest` 与 `plugin.json` 的一一映射要在 contract tests 中冻结
- Web `src/types/contracts.ts` 必须和 .NET shared contract 同步

## 6. Gateway API v1 冻结面

Iteration 4 建议冻结以下最小 API 面：

- `GET /api/plugins`
- `GET /api/plugins/{id}`
- `POST /api/plugins/install-local`
- `POST /api/plugins/discover`
- `POST /api/plugins/{id}/trust`
- `POST /api/plugins/{id}/enable`
- `POST /api/plugins/{id}/disable`
- `POST /api/plugins/{id}/start`
- `POST /api/plugins/{id}/stop`
- `GET /api/plugins/{id}/logs`

API 约束：

- `detail` 必须带出 manifest、permission summary、runtime state、health summary、available tools
- `discover` 只扫描冻结目录，不做递归远程来源
- `trust` 只改变信任状态；真正启用与运行分别走 `enable` / `start`
- `logs` 至少支持 `limit` 过滤；时间排序按倒序

## 7. 观测 / 故障隔离冻结点

Iteration 4 必须满足以下可验证行为：

- 每个插件有独立日志流
- 插件启动失败不会导致 Gateway 退出
- 插件运行失败会留下 diagnostics 记录
- 超过重启上限后标记为 `Degraded`
- `Degraded` 插件不会继续向 Runtime 注入工具
- 插件授权、启停、崩溃都应可被 UI 观察到

## 8. 验收门槛

Iteration 4 退出标准冻结为：

1. 用户可以安装或发现一个本地 `tool` 插件
2. 用户可以在 Web 中看到该插件的 manifest、权限、状态、日志
3. 用户可以完成 trust -> enable -> start 的完整链路
4. Runtime 新建 session 后能够看到 namespaced plugin tools
5. 插件停止或降级后，相关工具不会继续暴露给新 session
6. fixture plugin 故障不会拖垮宿主，并能在 diagnostics / logs 中看到证据
7. 至少 1 个 bundled fixture plugin 通过 Iteration 4 acceptance pack

## 9. 任务拆解与独立验证

下表是 Iteration 4 实现切片的冻结版拆解。每个任务都必须能独立验证，且写集尽量单一。

| ID | 任务 | 主要写集 | 独立验证 | 备注 |
| --- | --- | --- | --- | --- |
| `KC-0401` | Plugin manifest schema / validator | `src/KodaClaw.Contracts`、`src/KodaClaw.PluginHost/Manifest`、`tests/KodaClaw.ContractTests/Plugins/PluginManifestContractsTests.cs` | `dotnet test ... --filter PluginManifestContractsTests` | 冻结 `plugin.json` v1 |
| `KC-0402` | Plugin registry / storage | `src/KodaClaw.Contracts`、`src/KodaClaw.PluginHost/Registry`、`tests/KodaClaw.UnitTests/PluginHost/PluginRegistryRepositoryTests.cs` | `dotnet test ... --filter PluginRegistryRepositoryTests` | 复用 `config/control-plane.db` |
| `KC-0403` | Plugin lifecycle host | `src/KodaClaw.PluginHost/Hosting`、`tests/KodaClaw.IntegrationTests/PluginHost/PluginLifecycleHostIntegrationTests.cs` | `dotnet test ... --filter PluginLifecycleHostIntegrationTests` | 首期只要求 `stdio` |
| `KC-0404` | Plugin permissions model | `src/KodaClaw.Contracts`、`src/KodaClaw.PluginHost/Permissions`、`tests/KodaClaw.ContractTests/Plugins/PluginPermissionContractsTests.cs` | `dotnet test ... --filter PluginPermissionContractsTests` | 复用 approval / inbox |
| `KC-0405` | Runtime plugin tool injection | `src/KodaClaw.Runtime`、`tests/KodaClaw.IntegrationTests/Runtime/PluginToolInjectionIntegrationTests.cs` | `dotnet test ... --filter PluginToolInjectionIntegrationTests` | 冻结 namespaced tools |
| `KC-0406` | Plugin logs / health | `src/KodaClaw.PluginHost/Diagnostics`、`tests/KodaClaw.IntegrationTests/PluginHost/PluginHealthIntegrationTests.cs` | `dotnet test ... --filter PluginHealthIntegrationTests` | 失败 -> `Degraded` |
| `KC-0407` | Plugin manager Web | `apps/kodaclaw-web/src/components/PluginsDesk.tsx`、`apps/kodaclaw-web/src/__tests__/plugins-desk.spec.tsx`、`apps/kodaclaw-web/tests/kc0407-plugins.spec.ts` | `npm run test` + `npx playwright test tests/kc0407-plugins.spec.ts` | settings 只做占位 |
| `KC-0408` | Fixture plugin + acceptance pack | `tests/Fixtures/Plugins`、`tests/KodaClaw.IntegrationTests/Smoke/Iteration4AcceptanceIntegrationTests.cs`、`docs/ITERATION_4_ACCEPTANCE_PACK.md` | `dotnet test ... --filter Iteration4AcceptanceIntegrationTests` + web E2E | 至少 1 个 bundled plugin |

## 10. 推荐的实现波次

### Wave 0：主线程收口（本轮已完成）

- 冻结 `docs/ITERATION_4_FREEZE.md`
- 冻结 `docs/adr/ADR-0008-plugin-platform-v1.md`
- 同步 backlog / status / parallel docs

### Wave 1：Plugin foundation（已完成并验证）

已完成：

- `KC-0401`
- `KC-0404`
- `KC-0402`

结果：manifest、permissions、registry 三块基础合同都已落地，并已通过定向 tests 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` solution 回归。

### Wave 2：Host runtime baseline（已完成并验证）

已完成：

- `KC-0403`
- `KC-0406`

结果：stdio plugin host、per-plugin logs、health probe、restart budget、degraded 标记与 MCP fixture server 已落地，并已通过定向 unit / integration tests 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` solution 回归。

### Wave 3：Gateway / Runtime / Web（已完成并验证）

已完成：

- `KC-0405`
- `KC-0407`

结果：`/api/plugins/*` Gateway API、fresh-session runtime tool injection 与 `kodaclaw-web` plugin manager desk 已全部落地，并已通过 `PluginApiContractsTests`、`PluginToolInjectionIntegrationTests|PluginApiIntegrationTests`、`npm run typecheck`、`npm run build`、`npm run test`、`npx playwright test tests/kc0407-plugins.spec.ts` 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 回归。

### Wave 4：Acceptance（已完成并验证）

已完成：

- `KC-0408`
- solution + frontend + acceptance 全量回归

结果：bundled fixture manifests、`Iteration4AcceptanceIntegrationTests` 与 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_4_ACCEPTANCE_PACK.md` 已落地，并已通过 `dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/KodaClaw.PluginFixtureServer/KodaClaw.PluginFixtureServer.csproj`、plugin contract / integration targeted tests、`npm run test:e2e` 与 `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1` 回归。

## 11. Subagent 并行写集方案

主线程仍负责：

- shared contract 定稿
- ADR
- Gateway / Runtime / Web 的最终集成
- 全量验证与文档同步

冻结后建议采用如下 worker 分工：

| Worker | 任务 | 主要写集 |
| --- | --- | --- |
| `plugin-core-worker` | `KC-0401` + `KC-0402` | `src/KodaClaw.PluginHost/Manifest`、`src/KodaClaw.PluginHost/Registry`、对应 unit/contract tests |
| `plugin-runtime-worker` | `KC-0403` + `KC-0406` | `src/KodaClaw.PluginHost/Hosting`、`src/KodaClaw.PluginHost/Diagnostics`、对应 integration tests |
| `gateway-runtime-worker` | `KC-0405` | `src/KodaClaw.Gateway`、`src/KodaClaw.Runtime`、对应 Gateway/Runtime integration tests |
| `web-worker` | `KC-0407` | `apps/kodaclaw-web/src/components/PluginsDesk.tsx`、component tests、Playwright tests |
| `qa-worker` | `KC-0408` | `tests/Fixtures/Plugins`、`tests/KodaClaw.IntegrationTests/Smoke`、`docs/ITERATION_4_ACCEPTANCE_PACK.md` |

并行约束：

- `KodaClaw.Contracts` 由主线程先落 shared DTO，再允许其他 worker 消费
- `KC-0407` 可在 API shape 冻结后先用 mock adapter 起稿，但合并前必须对齐真实 API
- `KC-0408` 必须最后收口，不抢前面模块的 contract 定义权

## 12. 下一步

Iteration 4 已完整验收并冻结。下一步应当：

1. 保持当前 plugin platform v1 语义稳定，不再在本 ADR 范围内扩张
2. 为 post-plugin 产品能力重新冻结新的写集、ADR 与验收门槛
3. 在新的冻结文档上再决定下一轮 subagent 并行组合
