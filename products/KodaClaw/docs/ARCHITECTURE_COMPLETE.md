# KodaClaw 完整架构图

> 文档版本：Iteration 16（2026-03-21）
> 覆盖范围：后端模块、Web 前端、API 端点、Agent 工具、数据流、存储策略

---

## 1. 系统总览

KodaClaw 是运行在用户本机的个人 Agent OS。核心构成：

- **KodaClaw Gateway**（loopback ASP.NET Core）：所有能力的唯一出入口
- **Kode Agent SDK**：Agent runtime 内核（消息循环、工具执行、事件总线、存储）
- **kodaclaw-web**（React + Vite）：Web 控制台，在浏览器和 Electron 壳中共用
- **kodaclaw-desktop**（Electron）：桌面壳，托盘、通知、生命周期管理

```
┌─────────────────────────────────────────────────────────────────┐
│  用户界面层                                                       │
│                                                                   │
│  kodaclaw-web（浏览器 / Electron Renderer）                       │
│  ┌──────────┐ ┌─────────┐ ┌──────────────────────────────────┐  │
│  │GlobalRail│ │MainStage│ │ContextRail（Chat / 其他 Desk）    │  │
│  │  导航    │ │  8 Desk │ │  信息面板 / 快捷操作              │  │
│  └──────────┘ └─────────┘ └──────────────────────────────────┘  │
│                                                                   │
│  kodaclaw-desktop（Electron Main）                                │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │  托盘 · 通知 · 深链 · Gateway 生命周期（ManagedChild/Attach）│ │
│  └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────┬───────────────────────┘
                       HTTP + SSE         │
                       http://127.0.0.1:5076
                                          │
┌─────────────────────────────────────────▼───────────────────────┐
│  KodaClaw.Gateway（loopback ASP.NET Core）                        │
│                                                                   │
│  Endpoints/                                                       │
│  ├── SystemEndpoints       /api/system/*                         │
│  ├── ChatDiagnosticsEndpoints /api/chat/* · /api/diagnostics/*   │
│  ├── SessionEndpoints      /api/sessions/*                       │
│  ├── InboxEndpoints        /api/inbox/*                          │
│  ├── ApprovalEndpoints     /api/approvals/*                      │
│  ├── ModelEndpoints        /api/models/*                         │
│  ├── SettingsEndpoints     /api/settings/*                       │
│  ├── AutomationEndpoints   /api/automations/*                    │
│  ├── PluginEndpoints       /api/plugins/*                        │
│  ├── ChannelEndpoints      /api/channels/*                       │
│  └── CanvasEndpoints       /api/canvas/*                         │
│                                                                   │
│  Infrastructure/                                                  │
│  ├── 认证（Bearer token · OS Keychain）                           │
│  ├── CORS（loopback only）                                        │
│  ├── SSE 适配（ChatStream → EventSource）                         │
│  ├── CorrelationContext                                           │
│  └── DotEnv 配置加载                                              │
└──────────────────────────┬──────────────────────────────────────┘
                           │ DI 组合
    ┌──────────────────────┼────────────────────────────────┐
    │                      │                                │
    ▼                      ▼                                ▼
┌──────────┐   ┌────────────────┐   ┌─────────────────────────────┐
│KodaClaw  │   │KodaClaw.Runtime│   │  其余产品模块                │
│.Workspace│   │                │   │                              │
│          │   │  Agent 工厂     │   │  ControlPlane（Inbox/审批）  │
│  文件协议 │   │  三类 Session  │   │  PluginHost（MCP 进程托管）  │
│  Heartbeat│   │  六类工具      │   │  ChannelHub（外部渠道）       │
│  Compiler │   │  SSE 适配      │   │  Automation（cron/heartbeat）│
│  FileWatch│   │               │   │  ModelHub（provider 路由）   │
└────┬─────┘   └───────┬────────┘   └──────────────┬──────────────┘
     │                 │                             │
     │                 ▼                             │
     │        ┌────────────────┐                    │
     │        │ Kode.Agent SDK │                    │
     │        │                │                    │
     │        │ AgentConfig    │                    │
     │        │ EventBus       │                    │
     │        │ MessageQueue   │                    │
     │        │ BreakpointMgr  │                    │
     │        │ ToolRegistry   │                    │
     │        │ PermissionMgr  │                    │
     │        │ McpToolProvider│                    │
     │        │ JsonAgentStore │                    │
     │        │ LocalSandbox   │                    │
     │        └───────┬────────┘                    │
     │                │                             │
     ▼                ▼                             ▼
┌────────────────────────────────────────────────────────────────┐
│  存储层                                                          │
│                                                                  │
│  文件系统            JsonAgentStore      SQLite         OS Keychain│
│  ~/.kodaclaw/        ./.kode/agent-id/   products.db    Keychain  │
│  workspace/*.md      messages.json                      API keys  │
│  canvas/{id}/*.md    events.json                        tokens    │
│  mcp.json            snapshots/                                   │
└────────────────────────────────────────────────────────────────┘
```

---

## 2. 后端模块详情

### 2.1 模块职责表

| 模块 | 主要文件 | 核心职责 |
|------|---------|---------|
| `KodaClaw.Contracts` | `*Request.cs` `*Response.cs` 等 | API DTO、枚举、接口定义，无业务逻辑 |
| `KodaClaw.Storage` | `SqliteStorageDatabase.cs` `SqliteCanvasArtifactRepository.cs` | SQLite 连接、迁移、Canvas 仓储 |
| `KodaClaw.Workspace` | `WorkspaceService.cs` `HeartbeatAutomationCompiler.cs` `HeartbeatFileWatcherHostedService.cs` `HeartbeatSyncService.cs` `BootstrapService.cs` `PlatformSecretStore.cs` | `~/.kodaclaw` 初始化、workspace 文件读写、HEARTBEAT.md 编译、热更新监听、OS Keychain 封装 |
| `KodaClaw.ModelHub` | `ServiceCollectionExtensions.cs` | 模型 provider 注册、endpoint 路由、API key 管理 |
| `KodaClaw.Runtime` | `MainSessionService.cs` `ChannelSessionService.cs` `AutomationSessionService.cs` `BootstrapDraftService.cs` `PromptBuilder.cs` 六类工具 | Agent 工厂、三类 session、prompt 组装、SSE 适配、内置工具 |
| `KodaClaw.PluginHost` | `ServiceCollectionExtensions.cs` | 插件发现、MCP 进程生命周期、权限管理（stub 实现中） |
| `KodaClaw.ChannelHub` | `ChannelTurnOrchestrator.cs` `ChannelEventIngestionService.cs` `ChannelPolicyEngine.cs` `ChannelDeliveryGovernanceService.cs` `ChannelDeliveryDispatchService.cs` | 外部消息入站处理、channel session 编排、delivery 审批 |
| `KodaClaw.Automation` | `AutomationSchedulerHostedService.cs` `SqliteAutomationDefinitionRepository.cs` `SqliteAutomationRunRepository.cs` | cron 调度、任务执行、run 记录 |
| `KodaClaw.ControlPlane` | `SqliteInboxRepository.cs` `SqliteApprovalRepository.cs` `InMemoryDiagnosticsService.cs` | Inbox、审批单、诊断事件 |
| `KodaClaw.Gateway` | `GatewayApp.*.cs`（12 个 partial class） | loopback HTTP/SSE 入口，所有端点的 DI 组合根 |

---

### 2.2 KodaClaw.Runtime 工具清单

Runtime 内置 6 个 Agent 可调用工具，注入 `MainSessionOptions.DefaultTools`：

| 工具名 | 文件 | 权限 | 用途 |
|--------|------|------|------|
| `workspace_protocol_update` | `WorkspaceProtocolUpdateTool.cs` | 非只读 / 无需审批 | 更新 workspace 协议文件（target: identity/soul/user/memory/agents/heartbeat） |
| `workspace_memory_append` | `WorkspaceMemoryAppendTool.cs` | 非只读 / 无需审批 | 向 MEMORY.md 追加记忆条目 |
| `workspace_read` | `WorkspaceReadTool.cs` | 只读 | 读取 workspace 文件内容 |
| `inbox_create` | `InboxCreateTool.cs` | 非只读 / 无需审批 | 创建 Inbox 事项（Kind=Information） |
| `inbox_read` | `InboxReadTool.cs` | 只读 | 读取 Inbox 条目列表 |
| `canvas_upsert` | `CanvasUpsertTool.cs` | 非只读 / 无需审批 | 创建或更新 Canvas 产物（markdown/html，写文件 + SQLite） |

---

### 2.3 Session 类型

```
SessionKind
├── Main          ← 主对话（IChatSessionService / IMainSessionService）
│                    prompt: IDENTITY + SOUL + USER + MEMORY + AGENTS + HEARTBEAT
│                    tools: 全部默认工具 + canvas_upsert + inbox_create + workspace_*
│
├── ChannelDirectMessage   ── ChannelGroup
│   ↑                         （IChannelSessionService）
│   渠道 session，按 ChannelPolicy 决定加载哪些 workspace 文件
│   delivery 受 ChannelDeliveryGovernanceService 控制
│
└── Automation    ← 后台任务（IAutomationSessionService）
                    从 AutomationDefinition 加载 prompt/tools
                    结果写入 InboxItem
```

---

## 3. API 端点全览

### 3.1 /api/system/*

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/system/health` | 返回 `{ name, status, mode }` |
| GET | `/api/system/bootstrap-state` | 返回 workspace 初始化状态 |

### 3.2 /api/chat/* 和 /api/diagnostics/*

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/chat/stream` | SSE 流式对话（主 session） |
| GET | `/api/chat/timeline` | 主 session 消息历史 |
| GET | `/api/diagnostics/events` | 诊断事件查询 |
| GET | `/api/diagnostics/stream` | SSE 诊断实时流 |

### 3.3 /api/sessions/*

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/sessions` | 所有 session 摘要 |
| GET | `/api/sessions/{id}` | session 详情 |

### 3.4 /api/inbox/* 和 /api/approvals/*

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/inbox` | Inbox 条目列表（可分页） |
| PATCH | `/api/inbox/{id}/status` | 更新条目状态（Read/Dismissed/Done） |
| GET | `/api/approvals` | 待审批列表 |
| POST | `/api/approvals/{id}/approve` | 批准 |
| POST | `/api/approvals/{id}/reject` | 拒绝 |

### 3.5 /api/models/* 和 /api/settings/*

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/models` | 模型端点列表 |
| POST | `/api/models` | 新增模型端点 |
| PUT | `/api/models/{id}` | 更新模型端点 |
| DELETE | `/api/models/{id}` | 删除模型端点 |
| POST | `/api/models/{id}/set-default` | 设置默认端点 |
| GET | `/api/settings` | 获取全局设置（含 `automationsEnabled`） |
| PUT | `/api/settings` | 保存全局设置 |
| GET | `/api/settings/sandbox-risk-overview` | 沙箱风险简报（插件 + 渠道风险汇总） |
| GET | `/api/settings/update-state` | 组件更新状态检查 |

### 3.6 /api/automations/*

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/automations/definitions` | 自动化定义列表 |
| PATCH | `/api/automations/definitions/{id}` | 更新定义（enable/disable/prompt） |
| GET | `/api/automations/runs` | 运行记录列表 |

### 3.7 /api/plugins/*

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/plugins` | 插件列表摘要 |
| GET | `/api/plugins/{id}` | 插件详情（manifest + trust + health + tools） |
| POST | `/api/plugins/{id}/enable` | 启用插件 |
| POST | `/api/plugins/{id}/disable` | 禁用插件 |
| POST | `/api/plugins/{id}/start` | 启动插件进程 |
| POST | `/api/plugins/{id}/stop` | 停止插件进程 |
| POST | `/api/plugins/{id}/trust` | 授予信任（验证签名证据） |
| GET | `/api/plugins/{id}/logs` | 插件日志（`?limit=N`） |

### 3.8 /api/channels/*

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/channels/connectors` | 渠道连接器目录（含 implemented/supportsInbound 等） |
| GET | `/api/channels/accounts` | 已注册渠道账号列表 |
| GET | `/api/channels/threads` | 渠道线程列表（可过滤） |
| GET | `/api/channels/threads/{bindingId}` | 线程详情（binding + policy + session + audit） |
| GET | `/api/channels/threads/{bindingId}/audit` | 线程审计日志 |

### 3.9 /api/canvas/*

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/canvas` | Canvas 产物列表 |
| GET | `/api/canvas/{id}` | Canvas 产物详情（含文件内容） |
| POST | `/api/canvas` | 手动创建 Canvas 条目 |

---

## 4. Web 前端架构

### 4.1 Shell 架构（V2 三段式）

```
App.tsx（shell 变体路由：V2Shell / LegacyShell）
    │
    └── V2Shell（当前默认）
          │
          ├── GlobalRail（左侧导航 · 58px 高 / 可折叠）
          │   └── 8 个 Desk Tab + 系统状态
          │
          ├── MainStage（中央主区域）
          │   ├── is-chat 模式（对话优先）
          │   │   ├── V2ChatStageHead（标题 + 系统状态卡）
          │   │   └── V2StageSurface--chat
          │   │       └── shell-workbench
          │   │           ├── MessageTimeline（消息流 · overflow-y:auto）
          │   │           └── ChatComposer（输入区）
          │   │
          │   └── is-desk 模式（各功能 Desk）
          │       ├── InboxApprovalDesk       data-kc-view="inbox"
          │       ├── SessionsDiagnosticsDesk data-kc-view="sessions"
          │       ├── ModelsSettingsDesk      data-kc-view="models"
          │       ├── AutomationsDesk         data-kc-view="automations"
          │       ├── ChannelsDesk            data-kc-view="channels"
          │       ├── PluginsDesk             data-kc-view="plugins"
          │       └── CanvasDesk              data-kc-view="canvas"
          │
          └── ContextRail（右侧上下文面板）
              ├── ChatContextRail（is-chat 时）
              │   └── Inbox 摘要 · Session 信息 · 快捷操作
              └── （其他 Desk 时为空或折叠）
```

### 4.2 Desk 组件清单

| Desk 组件 | `data-kc-view` | 主要功能 |
|-----------|---------------|---------|
| `ChatComposer` + `MessageTimeline` | — | 主对话、SSE 流式渲染、工具调用展示、审批按钮 |
| `InboxApprovalDesk` | `inbox` | Inbox 事项列表、审批决策（approve/reject） |
| `SessionsDiagnosticsDesk` | `sessions` | 会话历史、诊断事件、SSE 实时流 |
| `ModelsSettingsDesk` | `models` | 模型端点 CRUD、全局设置（含 `automationsEnabled` toggle）、沙箱风险简报、更新观察台 |
| `AutomationsDesk` | `automations` | 自动化定义列表、运行记录、引擎关闭 Banner |
| `ChannelsDesk` | `channels` | 连接器目录、账号列表、线程列表、线程详情 + 审计日志 |
| `PluginsDesk` | `plugins` | 插件列表、插件详情（manifest/trust/health/tools）、lifecycle 操作（enable/disable/start/stop/trust） |
| `CanvasDesk` | `canvas` | Canvas 产物列表（前端 UI 面板待完整实现） |

### 4.3 关键 CSS 模式（V2 Shell）

```css
/* 全屏 Chat 模式 */
.v2-main-stage.is-chat {
  gap: 0;
  padding: 16px;
  grid-template-rows: auto minmax(0, 1fr);
  height: calc(100vh - 58px);   /* 58px = GlobalRail 高度 */
}

/* 隐藏 Chat 模式中的页面指标 chips */
.v2-main-stage.is-chat .v2-chat-stage-chips { display: none; }

/* 消息流充满剩余高度 */
.v2-stage-surface--chat .shell-workbench {
  grid-template-rows: minmax(0, 1fr) auto;
  height: 100%;
}
.v2-stage-surface--chat .timeline__body {
  min-height: 0;
  overflow-y: auto;
}
```

---

## 5. 核心数据流

### 5.1 主对话流（Chat）

```
用户输入
  │  POST /api/chat/stream { message, sessionId? }
  ▼
ChatDiagnosticsEndpoints → IChatSessionService (MainSessionService)
  │
  ├── PromptBuilder: 加载 workspace 文件组装 system prompt
  │     IDENTITY.md + SOUL.md + USER.md + MEMORY.md + AGENTS.md + HEARTBEAT.md
  │
  ├── DynamicModelProvider: 从 ModelHub 选取当前默认端点
  │
  ├── Kode.Agent SDK: 执行对话循环
  │     AgentConfig → EventBus → MessageQueue
  │     → AnthropicProvider / OpenAI provider
  │     → ToolRegistry（内置工具 + MCP 工具）
  │     → BreakpointManager（JsonAgentStore 持久化）
  │
  ├── SSE 适配: SDK EventBus → Server-Sent Events → EventSource（浏览器）
  │     TextChunkEvent → delta chunks
  │     ToolStartEvent / ToolEndEvent → tool timeline
  │     ApprovalRequestEvent → 审批请求
  │     DoneEvent → 流结束
  │
  └── 结果写回（可选）
        inbox_create → SqliteInboxRepository
        canvas_upsert → 文件系统 + SqliteCanvasArtifactRepository
        workspace_protocol_update → ~/.kodaclaw/workspace/*.md
```

### 5.2 渠道消息流（Channel）

```
外部消息（Telegram/Webhook）
  │  POST /api/channels/{kind}/inbound
  ▼
ChannelEventIngestionService
  │  解析 ChannelEventEnvelope，查找或创建 ThreadBinding
  ▼
ChannelPolicyEngine
  │  ChannelPolicy → 加载策略（muted/requireMention/loadIdentity 等）
  ▼
ChannelTurnOrchestrator
  │  调用 IChannelSessionService (ChannelSessionService)
  │  → Kode.Agent SDK 执行
  │  → ChannelReplyProposal（建议回复内容）
  ▼
ChannelDeliveryGovernanceService
  │  DeliveryMode = DraftApproval → 创建 Approval → 等待用户确认
  │  DeliveryMode = AutoSend → 直接发送
  │  DeliveryMode = RequireApproval → 创建 Approval
  ▼
ChannelDeliveryDispatchService
  │  （审批通过后）发送消息到外部渠道
  ▼
ChannelAuditEntry 写入 SQLite
```

### 5.3 HEARTBEAT.md 自动化热同步流

```
用户或 Agent 修改 ~/.kodaclaw/workspace/HEARTBEAT.md
  │  （workspace_protocol_update target=heartbeat）
  ▼
HeartbeatFileWatcherHostedService（FileSystemWatcher）
  │  检测到文件变化
  ▼
HeartbeatSyncService
  │  HeartbeatAutomationCompiler: 解析 YAML section → AutomationDefinition[]
  │  SqliteAutomationDefinitionRepository.UpsertAsync()
  ▼
AutomationSchedulerHostedService（后台 cron 循环）
  │  按 cron 表达式触发
  ▼
IAutomationSessionService.RunAsync()
  │  Kode.Agent SDK 执行自动化任务
  ▼
结果 → SqliteAutomationRunRepository（run 记录）
     → inbox_create（写入 Inbox 通知用户）
```

### 5.4 Bootstrap 引导流（首次启动）

```
Gateway 启动 → GET /api/system/bootstrap-state
  │  requiresBootstrap = true
  ▼
Web 显示 BootstrapPanel
  │  用户确认身份配置意图
  ▼
BootstrapDraftService.GenerateDraftAsync()
  │  使用最轻量 prompt 生成 IDENTITY/SOUL/USER 草稿
  ▼
用户在 Chat 中确认 → workspace_protocol_update 写入文件
  │
  ▼
WorkspaceService 标记 workspaceInitialized = true
  │
  ▼
下一次 session 启动时加载完整 workspace
```

---

## 6. 存储架构

### 6.1 存储职责划分

```
存储类型          用途                              位置
─────────────────────────────────────────────────────────────────
文件系统          workspace 协议文件               ~/.kodaclaw/workspace/
                  Canvas 产物                     ~/.kodaclaw/workspace/canvas/{id}/
                  插件本地资源                     ~/.kodaclaw/workspace/plugins/{id}/
                  mcp.json                        ~/.kodaclaw/workspace/mcp.json

JsonAgentStore   Agent 消息历史                   ./.kode/{agent-id}/
（SDK 内置）      tool calls                      messages.json
                  SDK 事件流                      events.json
                  断点快照                        snapshots/

SQLite           产品控制面状态                   ~/.kodaclaw/config/products.db
（KodaClaw 专用）  ├── inbox_items
                  ├── approvals
                  ├── automation_definitions
                  ├── automation_runs
                  ├── canvas_artifacts
                  ├── model_endpoints
                  ├── plugin_records
                  ├── channel_accounts
                  └── channel_thread_bindings

OS Keychain      敏感密钥                        macOS Keychain
（PlatformSecretStore） ├── gateway auth token
                  ├── model API keys
                  ├── channel bot token
                  └── plugin secrets
```

### 6.2 Workspace 文件协议（Agent 可读写）

```
~/.kodaclaw/
├── workspace/
│   ├── IDENTITY.md       ← Agent 身份定义（workspace_protocol_update target=identity）
│   ├── SOUL.md           ← 行为准则（target=soul）
│   ├── USER.md           ← 用户画像（target=user）
│   ├── MEMORY.md         ← 长期记忆索引（target=memory / workspace_memory_append）
│   ├── AGENTS.md         ← 工具使用引导（target=agents）
│   ├── HEARTBEAT.md      ← 自动化规则（target=heartbeat · 热更新生效）
│   ├── mcp.json          ← MCP server 定义
│   ├── canvas/           ← Canvas 产物文件（canvas_upsert 写入）
│   │   └── {id}/
│   │       └── index.md  ← 产物内容（markdown 或 html）
│   └── memory/
│       └── YYYY-MM-DD.md ← 近期会话记忆片段
└── config/
    ├── models.json       ← 模型端点配置缓存
    ├── plugins.json      ← 已安装插件列表
    ├── gateway.json      ← Gateway 配置
    └── update-state.json ← 组件更新检查结果
```

---

## 7. 插件体系

### 7.1 MCP-first 插件模型

```
插件安装（LocalDirectory / Bundled）
  │  plugin.manifest.json + plugin.signature.json
  ▼
PluginHost 插件发现
  │  manifest 解析 → PluginRecord 存入 SQLite
  ▼
信任授权（trust action）
  │  TrustEvidence: SignatureSidecar（Signed）| LocalDigest（DigestOnly）
  ▼
启用 + 启动（enable + start actions）
  │  进程托管（Stdio transport → PluginRuntimeState: Running）
  ▼
MCP 工具注册到 SDK
  │  namespaced: mcp__{pluginId}__{toolName}
  ▼
Agent 会话中可调用插件工具
```

### 7.2 插件风险分层

```
PluginPermissionSet 字段         风险级别
─────────────────────────────────────────
network: true                    高风险（SandboxRiskOverview.pluginRisk）
background: true                 中风险
secrets: [...]                   高风险
filesystem: [...]                中风险
channelAccess: true              中风险
```

---

## 8. 渠道体系

### 8.1 当前支持的渠道连接器

| ConnectorKind | supportsInbound | supportsOutbound | 状态 |
|---------------|----------------|-----------------|------|
| Telegram | ✓ | ✓ | 已实现 |
| GenericWebhook | ✓ | ✗ | 已实现 |
| QQ / WhatsApp / WeCom / DingTalk | — | — | 后续插件化 |

### 8.2 Delivery Mode（回复管控）

| 模式 | 行为 |
|------|------|
| `AutoSend` | 直接发送，无需审批 |
| `DraftApproval` | 生成草稿 → Inbox 审批 → 用户可在渠道内回复 `ok`/`no` 或在 Web UI 确认后发送 |
| `RequireApproval` | 创建 Approval 条目，显式审批后发送 |

**默认值（`ChannelEventIngestionService.CreateDefaultDeliveryRule`）：**
- 私聊（DirectMessage）→ `AutoSend`
- 群组（Group）→ `DraftApproval`

**渠道文字审批（KC-1602）：** DraftApproval 模式下，Agent 生成草稿后自动向同一 Thread 发送审批通知（含 6 位 hex token）。用户回复 `ok [TOKEN]` 批准或 `no [TOKEN]` 拒绝，无需打开 Web UI。Token 由 `SHA256(draftId)[0..3]` 生成，多个待审批并存时通过 token 精确匹配。详见 `docs/TELEGRAM_ONBOARDING.md`。

---

## 9. 安全模型

```
边界层              机制
────────────────────────────────────────────────────────────
网络隔离            Gateway 仅监听 127.0.0.1:5076（loopback only）
UI 认证             Bearer token（存 OS Keychain）
Session 隔离        每个 session 独立 store 目录 + 事件日志
外部动作审批         DraftApproval / RequireApproval 模式
沙箱（best-effort） LocalSandbox: 边界检查 + 工作目录限制
沙箱（隔离级别）    DockerSandbox: SDK 支持但非默认 runtime profile
插件权限            信任授权 + 显式 enable/start 两步操作
密钥存储            OS Keychain（不写入 workspace 文件）
渠道隐私边界         ChannelPolicy 控制记忆加载范围（不继承主会话长期记忆）
```

---

## 10. 测试架构

### 10.1 分层测试策略

| 层级 | 工具 | 覆盖内容 |
|------|------|---------|
| L0 编译 | `dotnet build` + `npm run typecheck` | 类型安全、编译错误 |
| L1 单元 | xUnit + Moq（后端）/ Vitest + RTL（前端） | 逻辑分支、组件渲染 |
| L2 集成 | `WebApplicationFactory` + SQLite | Gateway API 端到端、模块协作 |
| L3 契约 | FluentAssertions Golden file | API payload schema、workspace 文件结构 |
| L4 E2E | Playwright | UI 用户流程（需 Gateway + Web 同时运行） |
| L5 Dogfood | 真实环境人工验收 | 产品体验连续性 |

### 10.2 测试文件映射

```
tests/KodaClaw.UnitTests/
├── Runtime/
│   ├── WorkspaceProtocolUpdateToolTests.cs
│   ├── CanvasUpsertToolTests.cs
│   ├── InboxCreateToolTests.cs
│   └── ...
├── Workspace/
│   └── HeartbeatAutomationCompilerTests.cs
└── ...

tests/KodaClaw.IntegrationTests/
├── Bootstrap/
├── Runtime/
│   └── CanvasUpsertIntegrationTests.cs
├── ChannelHub/
└── ...

tests/KodaClaw.ContractTests/
├── Api/
└── Workspace/
    └── WorkspaceTemplateContractTests.cs

apps/kodaclaw-web/src/__tests__/
├── app-shell.spec.tsx
├── automations-desk.spec.tsx      ← KC-1501
├── models-settings-desk.spec.tsx  ← KC-1501
├── channels-desk.spec.tsx
├── plugins-desk.spec.tsx
├── canvas-desk.spec.tsx
├── inbox-approval-desk.spec.tsx
├── sessions-diagnostics-desk.spec.tsx
└── chat-context-rail.spec.tsx

apps/kodaclaw-web/tests/            ← Playwright E2E
├── kc0407-plugins.spec.ts          ← KC-1503
└── kc0508-channels.spec.ts         ← KC-1503
```

---

## 11. 桌面壳架构

```
kodaclaw-desktop（Electron）
│
├── Main Process（Node.js）
│   ├── GatewayLifecycleManager
│   │   ├── AttachOnly 模式：外部已运行 Gateway，桌面壳附加
│   │   └── ManagedChild 模式：桌面壳自行 spawn Gateway 进程
│   ├── TrayManager（系统托盘图标 + 菜单）
│   ├── NotificationManager（系统通知）
│   ├── DeepLinkHandler（自定义 URL scheme）
│   └── LaunchAtStartup（开机启动）
│
└── Renderer Process（Chromium）
    └── 加载 kodaclaw-web（与浏览器版完全相同的 React 应用）
        └── 通过 contextBridge 获取桌面桥接 API
```

---

## 12. 配置加载顺序

```
Gateway 启动
  1. DotEnv 加载 .env / .env.local（cwd）
  2. appsettings.json / appsettings.{Environment}.json
  3. 环境变量覆盖

关键环境变量：
  KODACLAW_WORKSPACE_ROOT         默认 ~/.kodaclaw
  KODACLAW_GATEWAY_URL            默认 http://127.0.0.1:5076
  KODACLAW_DESKTOP_GATEWAY_LIFECYCLE_MODE   AttachOnly | ManagedChild
  OPENAI_API_KEY / ANTHROPIC_API_KEY（或存 OS Keychain）
```

---

## 13. 当前功能完成度（Iter 16）

### 已完整实现

| 功能域 | 状态 |
|--------|------|
| 主对话（SSE 流式）+ session 恢复 | ✅ |
| Inbox + Approval 审批流 | ✅ |
| Sessions + Diagnostics 诊断 | ✅ |
| Model Hub（多 endpoint + 默认切换） | ✅ |
| Settings（automationsEnabled toggle + 沙箱风险简报 + 更新观察台） | ✅ |
| Automations（HEARTBEAT.md 编译 + cron 调度 + 运行记录 + 引擎关闭 Banner） | ✅ |
| Channels（Telegram + Webhook + AutoSend/DraftApproval 完整链路） | ✅ |
| 渠道文字审批（KC-1602）：渠道内 ok/no + token 审批，无需 Web UI | ✅ |
| Channel/Automation session 无 mid-turn 工具审批（KC-1601） | ✅ |
| Plugins（manifest + trust + lifecycle + logs） | ✅ |
| Canvas（canvas_upsert 工具 + SQLite + 文件系统） | ✅ 后端 |
| Agent 主动工具（canvas_upsert / inbox_create / workspace_read / heartbeat） | ✅ |
| Bootstrap 引导草稿生成 | ✅ |
| V2 Shell 三段式布局 + 对话优先 CSS | ✅ |
| 桌面壳（Electron + Gateway 生命周期） | ✅ |

### 已知缺口（待后续迭代）

| 缺口 | 说明 | 优先级 |
|------|------|--------|
| Canvas Desk 产物渲染面板 | `canvas_upsert` 工具已通，Web 前端无产物查看/渲染 UI | P1 |
| Automations 运行历史面板 | `automationsEnabled` toggle 已通，前端无历史运行列表视图 | P1 |
| Channels Delivery Mode 配置 UI | 目前只能通过 API 或 SQLite 修改 delivery mode，Channels Desk 无 per-binding toggle | P1 |
| Plugins onboarding UX | 入口存在，缺少首次安装引导流程 | P2 |
