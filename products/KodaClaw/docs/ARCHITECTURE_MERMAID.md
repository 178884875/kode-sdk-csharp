# KodaClaw 架构图（Mermaid）

> 文档版本：Iteration 16（2026-03-21）

---

## 1. 系统总览

```mermaid
graph TB
    subgraph UI["用户界面层"]
        WEB["kodaclaw-web<br/>React + Vite"]
        DESKTOP["kodaclaw-desktop<br/>Electron"]
    end

    subgraph GATEWAY["KodaClaw.Gateway（loopback :5076）"]
        direction TB
        EP_SYS["SystemEndpoints<br/>/api/system/*"]
        EP_CHAT["ChatDiagnosticsEndpoints<br/>/api/chat/* · /api/diagnostics/*"]
        EP_SESS["SessionEndpoints<br/>/api/sessions/*"]
        EP_INBOX["InboxEndpoints<br/>/api/inbox/*"]
        EP_APPR["ApprovalEndpoints<br/>/api/approvals/*"]
        EP_MODEL["ModelEndpoints<br/>/api/models/*"]
        EP_SET["SettingsEndpoints<br/>/api/settings/*"]
        EP_AUTO["AutomationEndpoints<br/>/api/automations/*"]
        EP_PLUG["PluginEndpoints<br/>/api/plugins/*"]
        EP_CHAN["ChannelEndpoints<br/>/api/channels/*"]
        EP_CANVAS["CanvasEndpoints<br/>/api/canvas/*"]
    end

    subgraph MODULES["产品模块"]
        RT["KodaClaw.Runtime<br/>Agent 工厂 · 三类 Session · 六类工具"]
        CP["KodaClaw.ControlPlane<br/>Inbox · Approval · Diagnostics"]
        WS["KodaClaw.Workspace<br/>文件协议 · Heartbeat · Keychain"]
        MH["KodaClaw.ModelHub<br/>Provider 路由 · 模型注册"]
        PH["KodaClaw.PluginHost<br/>MCP 进程托管 · 权限管理"]
        CH["KodaClaw.ChannelHub<br/>渠道绑定 · 消息路由 · Delivery"]
        AU["KodaClaw.Automation<br/>cron 调度 · Heartbeat 同步"]
        ST["KodaClaw.Storage<br/>SQLite 仓储"]
        CT["KodaClaw.Contracts<br/>DTO · 枚举 · 接口"]
    end

    SDK["Kode.Agent SDK<br/>EventBus · ToolRegistry · JsonAgentStore · Sandbox"]

    subgraph STORE["存储层"]
        FS["文件系统<br/>~/.kodaclaw/workspace/"]
        JSTORE["JsonAgentStore<br/>.kode/{agent-id}/"]
        SQLITE["SQLite<br/>products.db"]
        KEYCHAIN["OS Keychain<br/>API Keys · Tokens"]
    end

    WEB -->|HTTP + SSE| GATEWAY
    DESKTOP -->|HTTP + SSE| GATEWAY
    GATEWAY --> MODULES
    RT --> SDK
    PH -->|MCP stdio/http| EXT_PLUGIN["外部插件进程"]
    CH -->|Webhook/Bot API| EXT_CHAN["Telegram · Webhook"]
    SDK --> JSTORE
    WS --> FS
    WS --> KEYCHAIN
    CP --> SQLITE
    AU --> SQLITE
    PH --> SQLITE
    CH --> SQLITE
    MH --> SQLITE
    ST --> SQLITE
```

---

## 2. Runtime 内部结构

```mermaid
graph LR
    subgraph RT["KodaClaw.Runtime"]
        direction TB
        MSS["MainSessionService<br/>主对话 Session"]
        CSS["ChannelSessionService<br/>渠道 Session"]
        ASS["AutomationSessionService<br/>自动化 Session"]
        BDS["BootstrapDraftService<br/>引导草稿生成"]
        PB["PromptBuilder<br/>workspace 文件 → system prompt"]
        DMP["DynamicModelProvider<br/>ModelHub → SDK provider"]

        subgraph TOOLS["内置 Agent 工具（DefaultTools）"]
            T1["workspace_protocol_update<br/>target: identity/soul/user/memory/agents/heartbeat"]
            T2["workspace_memory_append"]
            T3["workspace_read"]
            T4["inbox_create"]
            T5["inbox_read"]
            T6["canvas_upsert<br/>写文件 + SQLite"]
        end
    end

    SDK["Kode.Agent SDK"]
    WS["Workspace"]
    CP["ControlPlane"]
    ST["Storage"]

    MSS --> PB
    MSS --> DMP
    CSS --> PB
    ASS --> PB
    PB --> WS
    DMP --> MH["ModelHub"]
    MSS --> SDK
    CSS --> SDK
    ASS --> SDK
    SDK --> TOOLS
    T1 --> WS
    T2 --> WS
    T3 --> WS
    T4 --> CP
    T5 --> CP
    T6 --> ST
    T6 --> FS["文件系统"]
```

---

## 3. Web 前端 Shell 架构

```mermaid
graph TB
    APP["App.tsx<br/>Shell 变体路由"]

    subgraph V2["V2Shell（当前默认）"]
        direction LR
        GR["GlobalRail<br/>左侧导航<br/>8 个 Desk Tab"]
        MS["MainStage<br/>中央主区域"]
        CR["ContextRail<br/>右侧面板"]
    end

    subgraph CHAT_MODE["is-chat 模式"]
        HEAD["V2ChatStageHead<br/>标题 + 状态"]
        TL["MessageTimeline<br/>消息流（overflow-y: auto）"]
        CC["ChatComposer<br/>输入区"]
    end

    subgraph DESK_MODE["is-desk 模式（各功能 Desk）"]
        D1["InboxApprovalDesk<br/>data-kc-view=inbox"]
        D2["SessionsDiagnosticsDesk<br/>data-kc-view=sessions"]
        D3["ModelsSettingsDesk<br/>data-kc-view=models"]
        D4["AutomationsDesk<br/>data-kc-view=automations"]
        D5["ChannelsDesk<br/>data-kc-view=channels"]
        D6["PluginsDesk<br/>data-kc-view=plugins"]
        D7["CanvasDesk<br/>data-kc-view=canvas"]
    end

    APP --> V2
    MS --> CHAT_MODE
    MS --> DESK_MODE
    CR --> CCR["ChatContextRail<br/>Inbox 摘要 · Session 信息"]
    LEGACY["LegacyShell（V1 备用）"]
    APP --> LEGACY
```

---

## 4. 主对话数据流

```mermaid
sequenceDiagram
    participant U as 用户（浏览器）
    participant GW as Gateway
    participant RT as Runtime
    participant PB as PromptBuilder
    participant SDK as Kode.Agent SDK
    participant LLM as LLM（Anthropic/OpenAI）
    participant TOOL as 内置工具
    participant STORE as 存储层

    U->>GW: POST /api/chat/stream { message }
    GW->>RT: IChatSessionService.StreamAsync()
    RT->>PB: 加载 workspace 文件组装 system prompt
    PB-->>RT: IDENTITY + SOUL + USER + MEMORY + AGENTS + HEARTBEAT
    RT->>SDK: AgentConfig + EventBus 启动
    SDK->>LLM: 发送消息（流式）
    LLM-->>SDK: TextChunk（streaming）
    SDK-->>GW: TextChunkEvent → SSE delta
    GW-->>U: Server-Sent Events 流

    alt Agent 调用内置工具
        LLM->>SDK: tool_use（如 canvas_upsert）
        SDK-->>GW: ToolStartEvent → SSE
        GW-->>U: 工具开始提示
        SDK->>TOOL: 执行工具
        TOOL->>STORE: 写入文件 / SQLite
        STORE-->>TOOL: ok
        TOOL-->>SDK: ToolResult
        SDK-->>GW: ToolEndEvent → SSE
        GW-->>U: 工具完成提示
    end

    SDK->>STORE: JsonAgentStore 持久化 session
    SDK-->>GW: DoneEvent → SSE end
    GW-->>U: 流结束
```

---

## 5. 渠道消息流

```mermaid
flowchart TD
    EXT["外部消息\nTelegram / Webhook"] -->|POST /api/channels/（kind）/inbound| INGEST

    subgraph CH["KodaClaw.ChannelHub"]
        INGEST["ChannelEventIngestionService\n解析消息 · 查找/创建 ThreadBinding"]
        POLICY["ChannelPolicyEngine\n加载 ChannelPolicy\n检查 muted / requireMention"]
        ORCH["ChannelTurnOrchestrator\n编排 turn 执行"]
        GOV["ChannelDeliveryGovernanceService\n决定 DeliveryMode"]
        DISP["ChannelDeliveryDispatchService\n发送到外部渠道"]
    end

    RT["ChannelSessionService\nKode.Agent SDK 执行"]
    APPR["ApprovalRepository\nInbox 审批条目"]

    INGEST --> POLICY
    POLICY -->|通过| ORCH
    POLICY -->|muted| DROP["丢弃"]
    ORCH --> RT
    RT -->|ChannelReplyProposal| GOV

    GOV -->|DraftApproval| APPR
    GOV -->|AutoSend| DISP
    GOV -->|RequireApproval| APPR

    APPR -->|用户批准| DISP
    DISP --> EXT2["发送消息\n回 Telegram / Webhook"]
    DISP --> AUDIT["ChannelAuditEntry\n写入 SQLite"]
```

---

## 6. HEARTBEAT.md 自动化热同步

```mermaid
flowchart LR
    USER["用户 / Agent\n修改 HEARTBEAT.md"] -->|workspace_protocol_update\ntarget=heartbeat| FILE

    subgraph WS["KodaClaw.Workspace"]
        FILE["HEARTBEAT.md\n~/.kodaclaw/workspace/"]
        WATCH["HeartbeatFileWatcherHostedService\nFileSystemWatcher"]
        SYNC["HeartbeatSyncService"]
        COMP["HeartbeatAutomationCompiler\nYAML section → AutomationDefinition"]
    end

    subgraph AU["KodaClaw.Automation"]
        REPO["SqliteAutomationDefinitionRepository\nUpsert 定义"]
        SCHED["AutomationSchedulerHostedService\ncron 循环检查"]
        RUNREPO["SqliteAutomationRunRepository\n记录运行结果"]
    end

    RT["AutomationSessionService\nKode.Agent SDK 执行任务"]
    INBOX["InboxCreateTool\n写入 Inbox 通知用户"]

    FILE --> WATCH
    WATCH -->|文件变化| SYNC
    SYNC --> COMP
    COMP --> REPO
    REPO --> SCHED
    SCHED -->|cron 触发| RT
    RT --> INBOX
    RT --> RUNREPO
```

---

## 7. Bootstrap 引导流

```mermaid
flowchart TD
    START["Gateway 启动"] --> CHECK

    CHECK{"GET /api/system/bootstrap-state\nrequiresBootstrap?"}
    CHECK -->|false| MAIN["进入主控制台\n正常使用"]
    CHECK -->|true| PANEL

    PANEL["Web 显示 BootstrapPanel\n用户确认配置意图"]
    PANEL --> DRAFT["BootstrapDraftService\n生成 IDENTITY / SOUL / USER 草稿"]
    DRAFT --> CHAT["Bootstrap 对话\n用户与 Koda 确认身份"]
    CHAT --> WRITE["workspace_protocol_update\n写入 IDENTITY.md · SOUL.md · USER.md"]
    WRITE --> MARK["WorkspaceService\n标记 workspaceInitialized = true"]
    MARK --> MAIN
```

---

## 8. 插件生命周期

```mermaid
stateDiagram-v2
    [*] --> Discovered: 插件目录扫描 / 安装
    Discovered --> Untrusted: manifest 解析成功
    Untrusted --> Signed: POST /api/plugins/{id}/trust\n验证签名证据
    Untrusted --> Disabled: 无需信任（DigestOnly）
    Signed --> Disabled: 默认 enabled=false

    Disabled --> Enabled: POST /api/plugins/{id}/enable
    Enabled --> Disabled: POST /api/plugins/{id}/disable

    Enabled --> Running: POST /api/plugins/{id}/start\nMCP 进程启动
    Running --> Stopped: POST /api/plugins/{id}/stop
    Stopped --> Running: POST /api/plugins/{id}/start

    Running --> Error: 进程崩溃 / healthcheck 失败
    Error --> Running: 自动重启（restartCount++）
```

---

## 9. 存储架构

```mermaid
graph TB
    subgraph FS["文件系统（~/.kodaclaw/）"]
        WS_FILES["workspace/\nIDENTITY.md · SOUL.md · USER.md\nMEMORY.md · AGENTS.md · HEARTBEAT.md\nmcp.json"]
        CANVAS_FILES["workspace/canvas/{id}/\nindex.md 或 index.html"]
        PLUGIN_FILES["workspace/plugins/{id}/\n插件本地资源"]
        CONFIG_FILES["config/\nmodels.json · plugins.json\ngateway.json · update-state.json"]
    end

    subgraph JSTORE["JsonAgentStore（.kode/{agent-id}/）"]
        MSG["messages.json\nAgent 消息历史"]
        EVT["events.json\nSDK 事件流"]
        SNAP["snapshots/\n断点快照"]
    end

    subgraph SQLITE["SQLite（products.db）"]
        T_INBOX["inbox_items"]
        T_APPR["approvals"]
        T_AUTO_DEF["automation_definitions"]
        T_AUTO_RUN["automation_runs"]
        T_CANVAS["canvas_artifacts"]
        T_MODEL["model_endpoints"]
        T_PLUGIN["plugin_records"]
        T_CHAN_ACC["channel_accounts"]
        T_CHAN_TH["channel_thread_bindings"]
    end

    subgraph KEYCHAIN["OS Keychain"]
        K_GW["gateway auth token"]
        K_MODEL["model API keys"]
        K_CHAN["channel bot token"]
        K_PLUG["plugin secrets"]
    end

    WS["KodaClaw.Workspace"] --> FS
    WS --> KEYCHAIN
    SDK["Kode.Agent SDK"] --> JSTORE
    CP["KodaClaw.ControlPlane"] --> T_INBOX
    CP --> T_APPR
    AU["KodaClaw.Automation"] --> T_AUTO_DEF
    AU --> T_AUTO_RUN
    CANVAS_RT["Runtime（canvas_upsert）"] --> CANVAS_FILES
    CANVAS_RT --> T_CANVAS
    MH["KodaClaw.ModelHub"] --> T_MODEL
    PH["KodaClaw.PluginHost"] --> T_PLUGIN
    CH["KodaClaw.ChannelHub"] --> T_CHAN_ACC
    CH --> T_CHAN_TH
```

---

## 10. 安全边界

```mermaid
graph TB
    INTERNET["外部网络"] -->|仅 webhook inbound| GW

    subgraph LOCAL["本机（loopback）"]
        GW["KodaClaw.Gateway\n127.0.0.1:5076\nBearer token 认证"]

        subgraph SANDBOX["沙箱（best-effort）"]
            SDK_SB["LocalSandbox\n边界检查 · 工作目录限制"]
            DOCKER["DockerSandbox\n容器隔离（SDK 支持，非默认）"]
        end

        KEYCHAIN["OS Keychain\n密钥隔离存储"]
    end

    UI["kodaclaw-web / desktop"] -->|loopback HTTP| GW
    GW -->|token 验证| GW
    GW --> SDK_SB
    GW -->|secrets 读写| KEYCHAIN

    GW -->|渠道审批| APPR["Approval / DraftApproval\n外部动作必须经过用户确认"]
    APPR -->|批准后| INTERNET
```
