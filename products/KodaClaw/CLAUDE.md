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
│ KodaClaw.McpHub     MCP 生态工具接入      │
│ KodaClaw.ControlPlane  审批 / Inbox       │
│ KodaClaw.PluginHost    渠道插件生命周期    │
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
| `KodaClaw.McpHub` | MCP 生态接入：读取 workspace/mcp.json，管理 MCP 连接，向 session 注入工具；不依赖 PluginHost |
| `KodaClaw.PluginHost` | 渠道插件：发现、manifest 校验、HTTP 协议进程托管、生命周期；不依赖 MCP |
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
  mcp.json       # 工具扩展：直连 MCP server（主流扩展路径）
config/
  models.json    # 模型 endpoint 配置
  plugins.json   # 已安装渠道插件（PluginHost 管理）
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

### 新增工具的完整 checklist

新增一个 Agent 工具时，以下三处缺一不可，漏任意一处工具都不会出现在 Agent 可用列表里：

1. **实现 + ToolRegistry 注册**（`KodaClaw.Runtime/ServiceCollectionExtensions.cs`）
   ```csharp
   toolRegistry.Register("my_tool", _ => new MyTool(...));
   ```

2. **加入 `MainSessionOptions.DefaultTools`**（`KodaClaw.Runtime/MainSessionOptions.cs`）
   ```csharp
   // DefaultTools 是传给 AgentConfig.Tools 的白名单
   // 不在这里 → 工具注册了但 Agent 看不到，表现为"工具不存在"
   "my_tool",
   ```

3. **（可选）加入 `BuiltinSkills.SkillGatedTools`**（`KodaClaw.Runtime/BuiltinSkills.cs`）
   ```csharp
   // 仅当该工具需要"激活对应 skill 才解锁"时才加这里
   // 同时必须在步骤 2 里也加，否则 schema 隐藏无意义
   "my_tool",
   ```

4. **更新对应 SKILL.md 的 `allowed-tools`**（如 `skills/koda-workspace/SKILL.md`）
   ```
   allowed-tools: ... my_tool
   ```

> 典型踩坑：只做了步骤 1，跳过步骤 2 → Agent 运行时说"没有这个工具"，但后端日志看不出异常。

### 新增渠道 Connector 的完整 Checklist

新增一个渠道 connector 时，以下 **3 处缺一不可**，漏任何一处 connector 都不会被启动或索引：

1. **加 `ChannelConnectorKind` 枚举值**
   (`src/KodaClaw.Contracts/Channels/ChannelConnectorKind.cs`)
   ```csharp
   Slack = 6,  // 新增
   ```

2. **实现 `IChannelConnector` 接口**
   (`src/KodaClaw.ChannelHub/Connectors/{PlatformName}/{PlatformName}Connector.cs`)
   - 四个成员：`Kind`、`StartAsync`、`StopAsync`、`SendAsync`
   - 参考 `TelegramConnector`（完整参考）或 `GenericWebhookConnector`（最小参考）
   - 入站归一化为 `ChannelEventEnvelope`，出站接收 `ChannelOutboundDraft`

3. **DI 注册（3 行）**
   (`src/KodaClaw.ChannelHub/ServiceCollectionExtensions.cs` 的 `AddKodaClawChannelHub()` 中)
   ```csharp
   services.TryAddSingleton<ISlackApiClient, HttpSlackApiClient>();  // 如有 API client
   services.TryAddSingleton<SlackConnector>();
   services.AddSingleton<IChannelConnector>(sp => sp.GetRequiredService<SlackConnector>());
   ```

> **不需要**修改 `ChannelDeliveryDispatchService`、`ChannelInboundGatewayService`、`ChannelConnectorHostedService` — `ChannelConnectorKindResolver` 自动索引。

**必读文档**：
- `docs/NEW_CONNECTOR_GUIDE.md` — 完整接入指南（接口定义、入站/出站模型、格式处理、已知坑点）
- `docs/CHANNEL_SPEC.md` — 设计规范（领域模型、安全策略、会话隔离）
- `docs/WECHAT_INTEGRATION.md` §11 — 接入血泪教训（注意 §11.4 已过时，见标注）

**测试**：参考 `tests/KodaClaw.IntegrationTests/ChannelHub/TelegramConnector*.cs`

---

## 当前进行中工作（WIP）

**近期完成**（ModelCapabilitySet 重设计，2026-04-02）：
- 枚举重设计：`TextChat/ToolCalling/Vision/ImageGeneration/TTS/STT/Embeddings` → `Text/Image/Video/File/Audio`（Text=1, Image=2, Video=4, File=8, Audio=16）
- 移除 `ModelEndpoint.SupportsToolCalling` 计算属性；DTO/ModelPreset 默认值改为 `Text`
- 所有 `ResolveDefaultForAsync(TextChat|ToolCalling)` 调用点统一改为 `ResolveDefaultForAsync(Text)`；`Vision` → `Image`
- `model-presets.json` 全量重新计算 defaultCapabilities（Claude/GPT-4o/Kimi=3，推理/纯文本=1，MiMo-Omni=19）
- 前端 CAP 常量、ModelsSettingsDesk 能力复选框、contracts.ts `supportsToolCalling` 同步清除
- `GenerateImageTool`/`GenerateSpeechTool` 已删除；`koda-canvas` Image kind 移除；`koda-channels` 音频发送段落移除
- `dotnet build` 0 错 0 警告；`npm run typecheck` 通过

**近期完成**（记忆系统优化，2026-03-26）：
- 移除 SQLite 元数据层，纯文件方案（KC-W3-001~007）
  - 删除 `IMemoryMetadataRepository`、`MemoryEntry`、`MemoryEntryQuery`、`SqliteMemoryMetadataRepository`、`MemoryMigrationService`、`memory_entries` DDL
  - 新增 `IMemoryFileService`/`MemoryFileService`（文件扫描替代 SQLite 查询）、`MemoryFrontmatterParser`（frontmatter 解析）、`MemoryMarkdownParser`（共享 helper）
  - 重写 `MemoryConsolidationService`（仅 git commit）；简化 `WorkspaceReadTool`（移除 memory_search）和 `WorkspaceProtocolUpdateTool`（移除 SQLite 追踪）
  - Gateway MemoryEndpoints 改用 `IMemoryFileService`
  - HEARTBEAT 模板新增 Stage 5（Agent 语义降级审查）
  - `koda-memory/SKILL.md` v3.0：`fs_grep` 搜索、Agent 语义降级、五阶段 Auto Dream
  - 前端 MemoryEntryItem 字段更新（lastAccessed→created, topic→tags）

**近期完成**（Iter 59，2026-03-26）：
- 记忆系统 Phase 2（KC-5901~5907）— **注意：SQLite 同步部分已被上述优化移除**
  - HEARTBEAT Nightly Consolidation 重写为五阶段管道（采集 → 整合 MEMORY.md → topics 维护 → 清理 → 记忆时效审查）；`koda-memory/SKILL.md` v3.0（KC-5901）
  - `AutomationScheduler` 后处理钩子：Memory Consolidation 成功后自动触发 `PostConsolidationAsync`（标题匹配，失败不阻塞）；DI 注入 `IMemoryConsolidationService?`（KC-5902）
  - `workspace_read(target=topics)` 列出/读取 topics/（KC-5903）
  - `SessionRetentionService` 扩展：main-*/channel-* 有摘要+>30天可删；活跃 session 永远跳过；`session_retention.cleaned` 诊断事件（KC-5904）
  - `SessionSummaryService` 隐私脱敏：LLM prompt 隐私规则 + `DetectPrivacyLevel` 7 关键词检测 + private 会话最小摘要（KC-5905）
  - Gateway 端点 `/api/memory/{stats,entries,promote}`（改用 IMemoryFileService 文件扫描）；Settings Desk MemorySection 升级（KC-5906）
  - 测试全绿（KC-5907）

**近期完成**（Iter 58，2026-03-26）：
- 记忆系统 Phase 1（KC-5801~5808）— **注意：SQLite 表和迁移服务已被上述优化移除，保留的是会话摘要、隐私、目录常量**
  - `KodaClawWorkspaceLayout` 4 个目录常量（sessions/topics/dormant/archive）（KC-5802 部分保留）
  - `IMemorySessionSummaryService` + `MemorySessionSummaryService`：LLM 生成结构化摘要（topics/keywords/decisions/follow_ups），写入 `workspace/memory/sessions/`；低价值 session 过滤（KC-5803）
  - `MainSessionService` + `ChannelSessionService` 轮转前触发摘要生成（通过 `JsonAgentStore` 从磁盘加载 messages，失败不阻塞），DI 注册完整（KC-5804）
  - `workspace_read(target=memory_search)` 关键词搜索 + `Query` 参数；读写 memory 时自动追踪 `LastAccessed`（引用计数）（KC-5805）
  - `IMemoryConsolidationService.PostConsolidationAsync`：MEMORY.md 同步 → P0-P3 降级 → 文件迁移 dormant/archive → git commit（KC-5806）
  - `koda-memory/SKILL.md` v2.0（三层架构 + memory_search + 降级机制）；HEARTBEAT Nightly Consolidation prompt 更新（KC-5807）
  - 18 个新测试全绿；全量回归 337 单元 + 252 集成 + 144 契约通过（KC-5808）

**近期完成**（Iter 56，2026-03-25）**[已移除]**：
- TTS 语音合成（KC-5601~5605）——`GenerateSpeechTool`（`generate_speech`）、`ISpeechService`、`OpenAICompatibleTtsService`、Connector 音频发送路径（TelegramConnector/FeishuConnector/WeChatConnector）已整体移除。TTS 能力改由外部 Skill 扩展提供，不内置于 Runtime。

**近期完成**（Iter 55，2026-03-25）：
- HEARTBEAT.md Cron 调度重构（KC-5501~5504）
  - `HeartbeatAutomationCompiler`：新增 `- cron: "..."` bullet 解析（Cronos 验证）；`LegacyScheduleToCron` 将旧 5 种 `- schedule:` 表达式转换为等价 cron string；`TryReadFieldValue` 自动去除引号（KC-5501）
  - 删除 `AutomationSchedule` / `AutomationScheduleKind` / `AutomationScheduleDay` 三个 Contracts 类型；`AutomationDefinition.Schedule` → `CronExpression: string`；`AutomationScheduler.ComputeNextRunAt` 改用 Cronos + `TimeZoneInfo.Local`（本机本地时区，用户直写本地时间无需换算 UTC）；新增 NuGet `Cronos`（KC-5502）
  - SQLite 迁移：4 列（`schedule_kind/interval/local_time/days_of_week`）→ 1 列 `cron`；旧行 `cron=NULL` fallback `"0 * * * *"`；`schedule_kind` 改为 `NULL`（KC-5503）
  - `contracts.ts` 删 schedule 嵌套类型加 `cronExpression: string`；`AutomationsDesk.tsx` `formatSchedule` 直接展示 cron 字符串；`koda-automation/SKILL.md` 完整改写 v2.0；`DefaultWorkspaceTemplates.Heartbeat()` 改为 `- cron:` 格式；`WorkspaceProtocolUpdateTool` 描述更新（KC-5504）
  - 全量回归：`dotnet build` 0 错 0 警告；`dotnet test KodaClaw.sln -m:1` 通过（1 个预存在 flaky 不计）；`npm run typecheck` 通过

**近期完成**（Iter 54，2026-03-25）：
- agentskills.io 标准对齐 + `allowed-tools` 功能化（KC-5401~5404）
  - SDK `SkillsLoader`：新增 `case "allowed-tools":` 连字符分支，改为空格分隔；新增 `metadata:` 嵌套块状态机，`SkillMetadata.Metadata` 不再为 null；暴露 `public static SkillMetadata ParseFrontmatter(string content)`（KC-5401）
  - SDK `PermissionManager`：新增 `GrantTools(IEnumerable<string>)`，`_allowTools==null` 时 no-op；`Agent.cs` 两条 AutoActivate 路径激活后各调 `_permissionManager.GrantTools()`，skill `allowed-tools` 中的工具自动加入 session 白名单（KC-5402）
  - 5 个内置 SKILL.md 迁移至标准格式（`allowed-tools` 空格分隔，`metadata:` 块存 `kind/version/tags`，新增 `compatibility: KodaClaw 1.x`）；`SkillFrontmatterParser` 精简为 SDK 委托；`SkillDescriptor` `Requires`→`AllowedTools` + 新增 `Compatibility`（KC-5403）
  - `contracts.ts` `requires`→`allowedTools` + 加 `compatibility?`；`SkillsDesk.tsx` i18n key 重命名 + compatibility 展示行；`SkillFrontmatterParserTests` 全量重写（12 个）；`SkillDescriptorContractTests`（4 个）+ `SkillsEndpointIntegrationTests` 字段更新（KC-5404）
  - 全量回归：`dotnet build` 0 错 0 警告；`dotnet test KodaClaw.sln -m:1` 691 个测试全绿；`npm run typecheck` 通过

**近期完成**（Iter 53，2026-03-25）：
- Skills 自动激活三会话接入（KC-5301~5302）
  - SDK `SkillsConfig` record 新增 `AutoActivate: IReadOnlyList<string>?` 字段；`Agent.cs` session 启动时在 Template 激活路径之前执行 `SkillsManager.AutoActivateAsync`，将激活结果注入 system prompt 并发出 `SkillActivatedEvent`（KC-5301）
  - `KodaClaw.Runtime/BuiltinSkills.cs`：Chat→`[koda-workspace, koda-memory]`、Channel→`[koda-workspace, koda-channels]`、Automation→`[koda-workspace, koda-automation]`；`koda-canvas` 不自动激活；3 个 SDK L1 + 5 个 Runtime L1 测试（KC-5302）
  - `MainSessionService`（2 处）、`ChannelSessionService`（2 处）、`AutomationSessionService`（1 处）`SkillsConfig` 均加 `AutoActivate`
  - 全量回归：`dotnet test KodaClaw.sln -m:1` 693 个测试全绿

**近期完成**（Iter 52，2026-03-25）：
- Skills frontmatter 规范 + 内置技能库 + SkillsDesk 升级（KC-5201~5203）
  - `SkillFrontmatterParser`（`KodaClaw.Gateway/Infrastructure/`）：解析 `kind/version/tags/requires` 扩展字段，`ParseInlineList` 支持 `[a, b, c]` 内联格式；`SkillDescriptor` 晋升至 `KodaClaw.Contracts/SkillContracts.cs`（KC-5201）
  - 4 个内置技能 SKILL.md：`koda-workspace`（更新）、`koda-automation`（heartbeat YAML / cron）、`koda-canvas`（artifact 类型 / image 生成）、`koda-channels`（channel_send / BindingId / 平台差异）、`koda-memory`（MEMORY.md 索引 / 写入策略）（KC-5202）
  - SkillsDesk UI：kind badge（builtin-core amber / optional neutral）、tags chips、requires 行；`sortSkills()` builtin-core 优先；`contracts.ts` + `api.ts` + CSS token 全量更新；15 L1 + 3 L3 + 3 L2 测试（KC-5203）

**近期完成**（Iter 46，2026-03-24）：
- 微信个人号渠道接入（KC-4601~4606）
  - `ChannelConnectorKind.WeChat = 3`；`WeChatQrCodeResult` / `WeChatQrCodeStatus` contracts（KC-4601）
  - `WeChatApiContracts.cs` iLink DTO；`IWeChatApiClient` + `HttpWeChatApiClient`（长轮询、发消息、二维码、登录验证）（KC-4602）
  - `WeChatConnector`（长轮询主循环、`get_updates_buf` 游标持久化、500 LRU 去重、Markdown→纯文本剥离）；`WeChatConnectorConfiguration`（FromAccount 工厂）；`WeChatAuthManager`（syncBuf 读写）；`WeChatConnectorOptions`（KC-4603）
  - `ServiceCollectionExtensions` 注册；`ChannelConnectorHostedService` + `ChannelInboundGatewayService` 加 WeChat arm；`POST /api/channels/wechat/get-qrcode`、`GET /api/channels/wechat/qrcode-status`、`POST /api/channels/wechat/test-credentials` 端点；扫码确认自动 upsert ChannelAccount + 启动 Connector（KC-4604）
  - 前端：`WeChatQrLoginPanel.tsx`（扫码状态机，3 分钟超时，2s 轮询）；`ChannelSetupWizard.tsx` 加微信分支（pick→intro→qrlogin→delivery→success）；`ChannelsDesk.tsx` Degraded 重新扫码入口（KC-4605）
  - 15 个新测试全绿：`WeChatConnectorConfigurationTests`（8 L1）+ `WeChatApiContractTests`（4 L3）+ `WeChatAuthApiIntegrationTests`（3 L2）；`dotnet build` 0 错 0 警告；`npm run typecheck` 通过（KC-4606）

**近期完成**（Iter 45，2026-03-23）：
- 自动化渠道推送闭环（KC-4501~4507）
  - `AutomationNotifyMode` 枚举（None/Auto/Approval）；`IAutomationNotificationService` 接口 + `ChannelPushResult` record 加入 Contracts 层（KC-4501）
  - `HeartbeatAutomationCompiler` 解析 `channels:` 嵌套列表 + `delivery-mode:` 字段；8 个新契约测试（KC-4502）
  - SQLite 幂等迁移加 `notification_channels` + `notify_mode` 列；Repository CRUD 读写补齐（KC-4503）
  - `AutomationScheduler` Auto 模式成功后调 `PushToChannelsIfAutoAsync`；Inbox PayloadJson 扩展 `channelPushResults`（KC-4504）
  - `AutomationNotificationService`（ChannelHub 实现，单渠道隔离错误）；`POST /api/inbox/{id}/push-to-channel` 端点（KC-4505）
  - 前端：AutomationsDesk 渠道 tag；InboxApprovalDesk 推送状态列表 + Approval 模式"推送"按钮；ChannelsDesk 复制 BindingId 按钮（KC-4506）
  - 全量 test 修复（12 个文件补 `NotificationChannels: null, NotifyMode: None`）；新增 3 个调度器集成测试（Auto/None/失败隔离）（KC-4507）
  - `dotnet test KodaClaw.sln -m:1` 全绿（预存在失败不计入）

**近期完成**（Iter 38，2026-03-22）：
- KodaClaw.McpHub 独立模块 + McpServersDesk（KC-3801~3807）
  - `WorkspaceMcpServerEntry` 加 `enabled` nullable bool 字段，缺省等价 true，向后兼容 Claude Desktop 格式（KC-3801）
  - 新建 `KodaClaw.McpHub` 独立项目：`IMcpHubService`（`InjectToolsAsync` / `TestConnectionAsync`）、`McpHubService`（迁移自 Runtime，错误隔离，诊断事件）、`McpHubInjectionResult` / `McpConnectionTestResult` records、`ServiceCollectionExtensions`（KC-3802）
  - `IWorkspaceService.SaveMcpConfigAsync()`；Gateway `GET /api/mcp-servers` + `PUT /api/mcp-servers` + `POST /api/mcp-servers/{name}/test-connection`；集成测试 5 个（KC-3803 + KC-3806）
  - 前端：`contracts.ts` 加 `WorkspaceMcpConfig` / `WorkspaceMcpServerEntry` / `McpConnectionTestResult`；`api.ts` 加 `fetchMcpServers` / `saveMcpServers` / `testMcpServerConnection`；`McpServersDesk.tsx` 完整管理界面（列表、enable/disable、添加表单、删除、测试连接状态徽章）；Sidebar 能力层加 "MCP 工具" 入口（KC-3804 + KC-3807）
  - 契约测试：`McpHubInjectionContractTests`（6 个）+ `WorkspaceMcpConfigContractTests`（新增 enabled + SaveMcpConfigAsync 测试，共 13 个新测试）（KC-3805）
  - 全量验证：`dotnet build` 0 错 0 警告；`npm run typecheck` 通过；L1/L2/L3 测试全绿

**近期完成**（Iter 37，2026-03-22）：
- workspace/mcp.json 接入（KC-3701~3704）
  - `WorkspaceMcpConfig` / `WorkspaceMcpServerEntry` DTO，Claude Desktop 兼容格式（mcpServers 字典，transport/command/args/env/url/headers）（KC-3701）
  - `IWorkspaceService.ReadMcpConfigAsync()`，`WorkspaceService` 实现：路径 `workspace/mcp.json`，文件不存在或 JSON 损坏时安全返回空配置（KC-3702）
  - `MainSessionService.BuildWorkspaceMcpToolsAsync()`：session 启动时读 mcp.json → 为每 entry 构建 McpConfig → `McpToolProvider.GetToolsAsync` → 注册 ToolRegistry + 去重；支持 stdio/http/streamableHttp/sse；单 server 错误隔离；诊断事件 `main_session.workspace_mcp.fetch_failed` / `.injected`（KC-3703）
  - 7 个契约测试：stdio/http entry 序列化、transport 缺省推断、空文件/文件缺失/JSON 损坏 → 安全返回（KC-3704）
  - 全量回归：`dotnet test KodaClaw.sln -m:1` 失败数 0（1 个预存在失败不计入）

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
  - ModelCapabilitySet flags enum（→ 已重设计为 Text/Image/Video/File/Audio，见当前进行中），替换 `SupportsToolCalling: bool`（KC-3401）
  - SQLite 迁移追加 `capabilities` 列，`ResolveDefaultForAsync(ModelCapabilitySet)` 接口及实现（KC-3402）
  - Frontend Models Desk 能力勾选 UI + 预设携带推荐 capabilities（KC-3403）
  - SDK `ImageContent : ContentBlock`（base64/URL），AnthropicProvider / OpenAIProvider 映射，MessageQueue 多模态重载（KC-3404）
  - `IMediaStore` / `LocalMediaStore`，`MediaMeta` / `MediaReference` contracts，`GET /api/media/{id}` 端点（KC-3405）
  - `IGenerationService` / `OpenAIImageGenerationService`（DALL-E 3），`GenerateImageTool`，`CanvasArtifactKind.Image`（KC-3406）**[已移除]** — 图像生成改由外部 Skill 扩展，`CanvasArtifactKind.Image` 随之移除
  - Frontend CanvasDesk Image kind 渲染（KC-3407）**[已移除]**
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
