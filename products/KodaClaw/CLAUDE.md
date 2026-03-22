# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 产品概览

KodaClaw 是运行在用户本机的个人 Agent OS，基于 Kode Agent SDK 构建，通过本地 loopback Gateway 服务 Web UI 和 Electron 桌面壳。

核心设计原则：
- **local-first**：所有状态（workspace、插件、自动化、渠道）存于本地磁盘或 SQLite
- **inspectable autonomy**：Agent 动作可见、可暂停、可通过 Inbox 审批
- **workspace as protocol**：人格、记忆、规则是 `~/.kodaclaw/` 下的普通文件
- **MCP-first plugins**：通过 Model Context Protocol 扩展能力

相关文档：`docs/PRODUCT.md`（用户流程和对象模型）、`docs/ARCHITECTURE.md`（完整架构图）

## 构建与测试命令

### 后端（.NET 10）

```bash
make test-solution          # 全量后端测试（串行，必须用 -m:1）
make test-contract          # API 契约与 workspace 结构验证
make test-integration       # 模块集成 + Gateway API 测试
make test-unit              # 纯逻辑单元测试

# 运行单个测试
dotnet test tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj \
  --filter "FullyQualifiedName~BootstrapFlow"

make run-gateway            # 启动本地 Gateway（http://127.0.0.1:5076）
WORKSPACE_ROOT=~/.kodaclaw-dev make run-gateway   # 使用独立 workspace
```

### Web 前端（React + Vite）

```bash
cd apps/kodaclaw-web
npm run dev         # 开发服务器（http://127.0.0.1:4173）
npm run build       # 生产构建
npm run test        # Vitest 单元测试
npm run test:e2e    # Playwright E2E（需要 Gateway 运行中）
```

### 桌面壳（Electron）

```bash
make dev-desktop-attach    # 附加到已运行的 Gateway（AttachOnly 模式）
make dev-desktop-managed   # 桌面壳自行启动 Gateway（ManagedChild 模式）
```

### 全量回归

```bash
make verify-all    # 后端 + Web + Desktop 全量
```

## 架构

```
kodaclaw-web / kodaclaw-desktop
        ↓ HTTP + SSE
KodaClaw.Gateway（ASP.NET Core，loopback）
        ↓ DI 组合
┌──────────────────────────────────────────┐
│ KodaClaw.Runtime    Agent 工厂 / 会话服务  │
│ KodaClaw.ControlPlane  审批 / Inbox       │
│ KodaClaw.PluginHost    MCP 插件生命周期    │
│ KodaClaw.ChannelHub    Telegram / Webhook │
│ KodaClaw.Automation    cron / heartbeat   │
│ KodaClaw.ModelHub      模型路由 / 密钥    │
│ KodaClaw.Workspace     ~/.kodaclaw 文件   │
│ KodaClaw.Storage       SQLite 仓储        │
└──────────────────────────────────────────┘
        ↓
Kode.Agent SDK（父仓库 /src/Kode.Agent.Sdk/）
```

### 模块职责

| 模块 | 职责 |
|------|------|
| `KodaClaw.Contracts` | API DTO、枚举、manifest 契约，无业务逻辑 |
| `KodaClaw.Storage` | SQLite 仓储与迁移 |
| `KodaClaw.Workspace` | `~/.kodaclaw` 初始化，文件协议读写 |
| `KodaClaw.ModelHub` | 模型 provider 注册、endpoint 路由、Keychain 密钥存储 |
| `KodaClaw.Runtime` | Agent 工厂，三类 session 服务（chat / channel / automation），SSE 适配 |
| `KodaClaw.PluginHost` | 插件发现、manifest 校验、MCP 进程托管、生命周期 |
| `KodaClaw.ChannelHub` | 外部渠道连接器、消息路由、delivery 审批派发 |
| `KodaClaw.Automation` | HEARTBEAT.md 编译、cron 调度、后台任务执行 |
| `KodaClaw.ControlPlane` | 审批、Inbox、设置持久化、诊断 |
| `KodaClaw.Gateway` | 入口、DI 组合、端点映射（partial class 拆分） |

### Gateway 代码组织

端点按领域拆分为 partial class，位于 `src/KodaClaw.Gateway/Endpoints/`（如 `GatewayApp.ChannelEndpoints.cs`）。服务注册、中间件、CORS、SSE 在 `Infrastructure/`。

### 关键文件

| 文件 | 说明 |
|------|------|
| `src/KodaClaw.Gateway/Program.cs` | 10 行入口，调用 `GatewayApp.Build(args)` |
| `src/KodaClaw.Runtime/ChannelSessionService.cs` | 渠道 session 编排 |
| `src/KodaClaw.Runtime/BootstrapDraftService.cs` | Bootstrap 引导草稿生成 |
| `src/KodaClaw.Workspace/WorkspaceService.cs` | Workspace 文件协议 |
| `src/KodaClaw.ChannelHub/ChannelTurnOrchestrator.cs` | 渠道 turn 执行管道 |
| `apps/kodaclaw-web/src/App.tsx` | Web 入口，挂载 AppShell |
| `apps/kodaclaw-web/src/shell/AppShell.tsx` | 二栏布局壳（Sidebar + MainContent） |
| `apps/kodaclaw-web/src/shell/Sidebar.tsx` | 左侧导航栏（Lucide 图标 + desk 列表） |
| `apps/kodaclaw-web/src/shell/MainContent.tsx` | 主内容区（DeskPageHeader + desk 路由） |

## 配置

Gateway 按以下顺序加载配置：
1. `cwd` 下的 `.env` / `.env.local`（DotEnv）
2. `cwd` 下的 `appsettings.json` / `appsettings.{Environment}.json`

关键环境变量：
- `KODACLAW_WORKSPACE_ROOT` — workspace 目录（默认 `~/.kodaclaw`）
- `KODACLAW_GATEWAY_URL` — 绑定地址（默认 `http://127.0.0.1:5076`）
- `KODACLAW_DESKTOP_GATEWAY_LIFECYCLE_MODE` — `AttachOnly` | `ManagedChild`

API 密钥通过 `KodaClaw.ModelHub` 存储在 OS Keychain，不写入配置文件。

## 测试基础设施

- **Contract 测试**：校验 API payload 和 workspace 文件结构，无需启动 Gateway，工具：FluentAssertions Golden file
- **Integration 测试**：通过 `WebApplicationFactory` 启动完整 Gateway，覆盖真实 HTTP 流程，无需外部服务
- **Unit 测试**：纯逻辑，无 I/O，工具：xUnit + Moq
- **E2E 测试**：需要 `make run-gateway` 和 `npm run dev` 同时运行，工具：Playwright

测试文件与被测模块镜像命名，如 `BootstrapDraftServiceIntegrationTests.cs` 对应 `BootstrapDraftService`。

## Workspace 协议

Agent 在 session 启动时从 `~/.kodaclaw/` 读取以下文件：

```
workspace/
  IDENTITY.md    # 人格定义
  SOUL.md        # 行为准则
  USER.md        # 用户画像
  MEMORY.md      # 长期记忆索引
  HEARTBEAT.md   # 自动化规则（从 YAML 编译而来）
  mcp.json       # 插件 MCP server 定义
config/
  models.json    # 模型 endpoint 配置
  plugins.json   # 已安装插件
  gateway.json   # Gateway 认证 token
```

注意：Workspace 文件在 session 启动时读入 system prompt。Agent 可通过 `workspace_protocol_update`（target: identity/soul/user/memory/agents/heartbeat）和 `workspace_memory_append` 在对话中写回文件，写入后在下一个 session 启动时生效（heartbeat 除外，热更立即生效）。详细协议见 `docs/WORKSPACE_SPEC.md`。

## 迭代流程

开始任何工作前先判断类型，三种类型流程重量不同：

| 类型 | 判断标准 | 流程 |
|------|---------|------|
| **新功能** | 新增用户可感知的能力 | 完整流程（FREEZE → KC 条目 → 实现 → L0-L5 → Dogfood） |
| **Bug Fix** | 修复已有功能的错误行为 | 轻量流程（KC-BUG 条目 → 定向验证） |
| **优化/重构** | 改代码质量、性能或 UX，不增加新能力 | 按范围分级（小改直接做，大改走专项前缀） |

### 新功能：完整 Capability Slice 流程

**在开始实现任何新功能之前，必须：**
1. 确认 `docs/ITERATION_N_FREEZE.md` 已存在并已冻结该功能范围（不允许事后补写）
2. 在 `docs/IMPLEMENTATION_BACKLOG.md` 登记 KC 条目，包含：User Outcome、Scope、Modules、Verification 命令
3. 垂直打通，不允许只写中间层——例如审批流必须一次性打通 UI → Gateway → Runtime → Store → Inbox 可见

```
FREEZE doc（范围/契约/非目标） ← 必须先于实现确认
    ↓
KC-XXXX 条目写入 BACKLOG
    ↓
实现（Contract → Scaffold → Vertical）
    ↓
分层验证 L0–L5
    ↓
高风险能力完成后立即触发小 Dogfood（不等到迭代末）
    ↓
ACCEPTANCE_PACK 验收矩阵
```

**完成标准（缺一不可）：**
1. 用户路径打通，不只是后端逻辑
2. 关键 contract 已写入文档或测试
3. 对应层级测试补齐，验证命令写入 BACKLOG 条目
4. diagnostics / event 可观察
5. 文档与实现一致

### Bug Fix：轻量流程

1. 在 BACKLOG 登记条目，格式：`KC-BUG-XXX | 症状 | 根因 | 受影响模块`
2. 不需要 FREEZE doc
3. 只跑受影响的最低验证层 + L0（编译不能挂）：
   - 逻辑错误 → L1 单元测试
   - API 行为错误 → L2 集成测试
   - UI 行为错误 → L4 Playwright 定向测试
4. 修复后更新 BACKLOG 条目为 Completed，附复现步骤和验证命令

### 优化/重构：按范围分级

- **小改**（单文件 / 单模块内）：直接做，无需 KC 条目，L0 通过即可
- **中改**（跨 2-3 个模块，不改 API contract）：BACKLOG 简短条目 + 受影响模块 L2 + L0
- **大改**（跨迭代、改动 API/schema/UI 结构）：专项前缀（如 `KC-W2-xxx`），BACKLOG 单独分组，必须跑 L4 全量回归（`npm run test:e2e`）

### 分层验证（L0–L5）

| 层级 | 内容 | 工具 |
|-----|------|------|
| L0 | 编译、typecheck | `dotnet build`, `npm run typecheck` |
| L1 | 单元测试：policy、路由、schema 校验 | xUnit + Moq |
| L2 | 集成测试：模块协同、Gateway API | `WebApplicationFactory` + SQLite |
| L3 | 契约 / Golden 测试：API schema、SSE 序列、workspace 结构 | FluentAssertions snapshot |
| L4 | 端到端：从 UI 打通到 Store | Playwright |
| L5 | Dogfood / 人工验收：产品体验与连续性 | 真实 Gateway + Web |

**必须过 L5 Dogfood 的模块**：session 恢复、workspace 加载、approvals、plugin lifecycle、channels、automations、canvas。

### 任务编号规则

- `KC-XYYY`：常规迭代新功能（如 `KC-0507` = 迭代 5 第 7 个）
- `KC-BUG-XXX`：Bug Fix
- `KC-W2-XXX` 等专项前缀：横跨迭代的大规模重构

**有 untracked 文件但无对应 KC 条目，说明流程未被遵守，必须先补登记再继续。**

### 执行注意

- **`docs/IMPLEMENTATION_BACKLOG.md` 是事实源**，`IMPLEMENTATION_STATUS.md` 是汇总视图，不要在两处独立维护状态
- **Dogfood 不等迭代末**，每完成一个高风险能力立即触发一次小 Dogfood
- **FREEZE doc 必须在实现启动前确认**，不允许事后补写

## 当前进行中工作（WIP）

无进行中条目。

**近期完成**（Iter 36，2026-03-22）：
- 飞书 / Lark Channel 连接器（KC-3601~3609）
  - `ChannelConnectorKind.Feishu = 2`，`FeishuApiContracts.cs` 全套 WS/REST DTO（KC-3601）
  - `IFeishuApiClient` / `HttpFeishuApiClient`：双 token 缓存（app/tenant，5 分钟提前刷新），文本 + 图片发送（KC-3602）
  - `FeishuWebSocketClient`：长连接 + 30s 心跳 + 500 条 LRU 去重 + 指数退避重连 + 3s 内即时 ACK（KC-3603）
  - `FeishuConnector` + `FeishuConnectorConfiguration` + `FeishuConnectorOptions`：StartAsync/StopAsync/SendAsync；@ 提及解析；ExternalThreadId `{type}:{id}` 格式（KC-3604）
  - `ChannelInboundGatewayService` + `ChannelConnectorHostedService` 路由泛化：Start/Stop/Reload 全部按 ConnectorKind switch（KC-3605）
  - Gateway 端点：Feishu 连接器描述符、`POST /api/channels/test-feishu-credentials`；`ReconcileChannelAccountRuntimeAsync` / `SandboxRiskOverviewService.SupportsOutbound` / `SecretMigrationReportService.BuildFeishuChannelItemAsync` 补齐（KC-3606）
  - 测试：10 个单元测试（FeishuConnectorConfigurationTests）+ 3 个契约测试（Feishu JSON 序列化）+ 3 个集成测试（Feishu CRUD API）+ KodaClaw.ChannelHub `InternalsVisibleTo`（KC-3607）
  - 前端：`contracts.ts` 加 `"Feishu"`；`api.ts` 加 `testFeishuCredentials()`；`ChannelsDesk.tsx` 飞书账号表单（AppId + AppSecret + 测试按钮）（KC-3608）
  - 前端 onboarding：`ChannelSetupWizard.tsx` 重构为连接器选择 → Telegram/飞书分支流程（引导 + 凭证输入 + 测试 + 交付模式）（KC-3609）

**近期完成**（Iter 34，2026-03-21）：
- 多模态内容基础层（KC-3401~3408）
  - ModelCapabilitySet flags enum（TextChat/ToolCalling/Vision/ImageGeneration/TTS/STT/Embeddings），替换 `SupportsToolCalling: bool`（KC-3401）
  - SQLite 迁移追加 `capabilities` 列，`ResolveDefaultForAsync(ModelCapabilitySet)` 接口及实现（KC-3402）
  - Frontend Models Desk 7 项能力勾选 UI + 预设携带推荐 capabilities（KC-3403）
  - SDK `ImageContent : ContentBlock`（base64/URL），AnthropicProvider / OpenAIProvider 映射，MessageQueue 多模态重载（KC-3404）
  - `IMediaStore` / `LocalMediaStore`，`MediaMeta` / `MediaReference` contracts，`GET /api/media/{id}` 端点（KC-3405）
  - `IGenerationService` / `OpenAIImageGenerationService`（DALL-E 3），`GenerateImageTool`，`CanvasArtifactKind.Image`（KC-3406）
  - Frontend CanvasDesk Image kind 渲染（`<img>` 替代 iframe），`canvas-preview-image` CSS（KC-3407）
  - `ChannelOutboundDraft.MediaAttachments`，`channel_send` 工具新增 `mediaId?`，`TelegramConnector` sendPhoto 路径，InboxApprovalDesk 缩略图预览（KC-3408）

**近期完成**（Iter 32-33，2026-03-21）：
- Chat 历史会话面板（KC-3201~3202）：后端 `POST /api/sessions/{id}/resume` 端点 + 前端 SessionHistoryPanel（历史列表、恢复按钮、30s 轮询）
- Automations 启用/禁用 Toggle（KC-3301）：AutomationsDesk 工具栏 toggle，实时 read-then-write 写入 settings
- Inbox AutomationResult 专属渲染（KC-3302）：tab 过滤（全部/审批/自动化结果）+ AutomationResult 卡片（Zap 图标、仅"标为已读"操作）

**近期完成**（Iter 31，2026-03-21）：
- CSS Token 系统性补全（KC-3101）：app-shell.css / ControlPlaneDesk.css / index.css 所有硬编码字号/间距全量替换为 token，48/48 测试通过。

**近期完成**（Iter 26-27，2026-03-21）：
- 自然语言 workspace 引导 + Settings Desk（KC-2601~2707）
  - 删除 BootstrapPanel + bootstrap mode，前端简化为单一 main 模式
  - 后端 workspace readiness API（`GET /api/workspace/readiness`）
  - WorkspaceReadinessService：检测 IDENTITY/SOUL/USER.md 是否仍为默认内容
  - 主 session system prompt 自动注入引导 section（当 hasAnyGap=true）
  - `workspace_protocol_update` 完成后自动轮转 session（`RequestWorkspaceRotation`）
  - Settings Desk：工作区身份编辑（GET/PUT /api/workspace/file）+ PersonaSelector 集成
  - Settings Desk：连接 section（ChannelSetupWizard）+ 偏好 section（LocaleToggle 从 header 移入）+ 系统 section（重新引导 / 清除身份）
  - Sidebar 重组：技能移入"能力层"组，新增"设置"独立入口

**近期完成**（Iter 25，2026-03-21）：
- 前端全面重构（Claude Desktop 风格）（KC-W2-010~014）
  - 新 CSS Design System：amber 品牌色 + 中性背景，移除 grain/glass 纹理
  - 新二栏布局：Sidebar 240px + 全宽 MainContent，替代三栏 V2Shell
  - App.tsx 瘦身：981 行 → ~238 行（抽取 app-strings.ts + useBootstrap hook）
  - Onboarding CSS 对齐 amber 品牌色
  - 删除 shell-v1/、shell-v2/、DeskHeader、SystemStatusCard、shell-variant

**近期完成**（Iter 22-24，2026-03-21）：
- Canvas Desk UI：`react-markdown` 渲染面板已完成（KC-2207）
- Channel session 时效策略 + SUMMARY.md 语义压缩（KC-2201/KC-2206）
- ChannelsDesk 30s 自动刷新 + 账号管理向导（KC-2202/KC-2203）
- Automation 手动触发按钮（KC-2205）
- 桌面 ChannelDelivery 审批快捷操作（KC-2204）
- 模型预设库 + API Key 连通性测试（KC-2301/KC-2302）
- Persona 模板库 6 套 + Onboarding 状态持久化（KC-2303/KC-2304）
- 首次使用引导程序全流程（KC-2401~KC-2407）
