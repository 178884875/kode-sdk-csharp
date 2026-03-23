# KodaClaw 插件规范

## 1. 设计目标

KodaClaw 的扩展体系分为两个独立模块：

| 模块 | 职责 | 协议 |
|---|---|---|
| `KodaClaw.PluginHost` | 受管插件生命周期（多类型） | 自定义 HTTP 协议 |
| `KodaClaw.McpHub` | MCP 生态工具直连 | MCP 协议 |

两者**完全独立**，没有依赖关系。PluginHost 不依赖任何 MCP 库；McpHub 不涉及插件安装和生命周期管理。

---

## Part 1：PluginHost — 受管插件系统

### 2. 设计原则

PluginHost 是 KodaClaw 的**受管扩展容器**，负责插件的发现、信任审批、进程托管、健康监控和生命周期管理。它不限于某一类型——插件可以同时声明多种能力。

设计决策：

- **类型可扩展**：`channel` 是当前主力类型，`tool` / `memory` / `ui` 按需迭代实现，框架设计不预设上限
- **不基于 MCP**：PluginHost 插件的接口是结构化已知的，不需要动态发现。自定义 HTTP 协议更简单、语言无关、双向通信天然支持
- **可参考 MCP 理念**：进程隔离、权限声明、manifest 格式、健康检查——思路借鉴，但不走 MCP 协议栈
- **治理是核心价值**：信任门控 + Keychain secrets + 日志 + 重启，这是 PluginHost 区别于 workspace/mcp.json 的根本价值

### 3. 插件类型

一个插件可以同时声明多个类型（如一个 Notion 插件同时提供工具和记忆能力）：

| 类型 | 说明 | 实现状态 |
|---|---|---|
| `channel` | 接入外部消息平台（入站 + 出站） | 主力，当前迭代重点 |
| `tool` | 向 Agent session 注入工具（需治理/Keychain 的工具） | 待实现 |
| `memory` | 记忆或知识接入（向量库、外部知识库） | 🔮 未来 |
| `ui` | Canvas 面板或设置页注入 | 🔮 未来 |

> **`tool` 类型的两条路径**：
> - 社区/第三方 MCP server → **McpHub**（workspace/mcp.json，无安装流程）
> - 需要信任审批 / Keychain secrets 的自研工具 → **PluginHost tool 插件**

### 4. Plugin HTTP 协议

#### 4.1 通信方式

PluginHost 通过环境变量告知插件进程监听端口，插件暴露 HTTP server，PluginHost 用 HttpClient 调用：

```
KODACLAW_PLUGIN_PORT   插件应监听的端口
KODACLAW_PLUGIN_TOKEN  双向认证 token
KODACLAW_HOST_URL      KodaClaw Gateway base URL（用于插件主动回调）
KODACLAW_PLUGIN_ID     插件 ID
+ 用户 secrets（从 Keychain 解析后注入）
```

所有请求携带 `Authorization: Bearer {KODACLAW_PLUGIN_TOKEN}`。

#### 4.2 共用端点（所有类型必须）

| 端点 | 说明 |
|---|---|
| `GET /health` | 健康检查 |
| `GET /capabilities` | 返回插件实际支持的 capability 列表（运行时确认） |

**`GET /health`**
```json
// 200
{ "status": "ok" }
// 500
{ "status": "error", "message": "数据库连接失败" }
```

**`GET /capabilities`**
```json
// 200
{
  "types": ["channel", "tool"],
  "channel": { "kind": "Discord", "inboundMode": "push" },
  "tool": { "tools": ["search_messages", "list_members"] }
}
```

#### 4.3 Channel 能力端点（`types` 含 `channel` 时实现）

| 端点 | 必须 | 说明 |
|---|---|---|
| `POST /channel/start` | ✅ | 启动指定账号的连接 |
| `POST /channel/stop` | ✅ | 停止指定账号的连接 |
| `POST /channel/send` | ✅ | 出站消息发送 |
| `POST /channel/test-creds` | ✅ | 凭证连通性测试 |
| `POST /channel/register-webhook` | Webhook 模式 | 向平台注册 Webhook URL |

**`POST /channel/start`**
```json
// Request
{
  "accountId": "acc-xxx",
  "config": { "botToken": "xxx", "appId": "yyy" },
  "hostIngestUrl": "http://localhost:5076/api/channels/plugin/ingest",
  "webhookUrl": "http://localhost:5076/api/channels/plugin/discord/webhook"
}
// Response
{ "ok": true }
{ "ok": false, "error": "Invalid token." }
```

**`POST /channel/send`**
```json
// Request
{
  "accountId": "acc-xxx",
  "externalThreadId": "channel-123",
  "text": "Hello!",
  "mediaUrl": "https://...",      // 可选
  "replyToMessageId": "msg-id"   // 可选
}
// Response
{ "ok": true, "messageId": "ext-msg-id", "sentAt": "2026-03-22T10:00:00Z" }
{ "ok": false, "error": "..." }
```

**`POST /channel/test-creds`**
```json
// Request
{ "config": { "botToken": "xxx", "appId": "yyy" } }
// Response
{ "ok": true }
{ "ok": false, "error": "Invalid bot token." }
```

**入站事件推送（插件 → KodaClaw）**

插件进程将入站消息 POST 到 KodaClaw：

```
POST {hostIngestUrl}
Authorization: Bearer {KODACLAW_PLUGIN_TOKEN}

{
  "connectorKind": "Discord",
  "accountId": "acc-xxx",
  "eventId": "evt-unique-id",
  "eventType": "MessageReceived",
  "externalThreadId": "channel-id",
  "externalMessageId": "msg-id",
  "threadType": "DirectMessage",
  "text": "用户发来的消息",
  "sender": { "id": "user-123", "displayName": "Alice" },
  "occurredAt": "2026-03-22T10:00:00Z"
}
```

三种入站模式均归结到此推送端点：
- **Push 模式**：插件自维护长连接，收到消息后立即 POST
- **Webhook 模式**：平台推 Webhook 到 KodaClaw → KodaClaw 转给插件解析后 POST
- **Poll 模式**：插件自行定时轮询平台后 POST

#### 4.4 Tool 能力端点（`types` 含 `tool` 时实现）

| 端点 | 必须 | 说明 |
|---|---|---|
| `GET /tools` | ✅ | 返回工具列表及 JSON Schema |
| `POST /tools/{name}` | ✅ | 调用指定工具 |

**`GET /tools`**
```json
// Response
{
  "tools": [
    {
      "name": "search_messages",
      "description": "搜索 Discord 消息",
      "inputSchema": {
        "type": "object",
        "properties": {
          "query": { "type": "string" },
          "limit": { "type": "integer", "default": 20 }
        },
        "required": ["query"]
      }
    }
  ]
}
```

**`POST /tools/{name}`**
```json
// Request
{ "arguments": { "query": "部署记录", "limit": 10 } }
// Response
{ "ok": true, "content": "找到 3 条消息：..." }
{ "ok": false, "error": "权限不足" }
```

工具注册到 ToolRegistry 时使用命名空间 `plugin__{pluginId}__{toolName}`，区别于 McpHub 的 `mcp__{serverName}__{toolName}`。

### 5. Manifest 格式（plugin.json）

#### 多类型插件示例（Discord，channel + tool）

```json
{
  "id": "discord-connector",
  "name": "Discord",
  "version": "1.0.0",
  "types": ["channel", "tool"],
  "runtime": {
    "command": "python3",
    "args": ["server.py"],
    "environmentReferences": {
      "DISCORD_BOT_TOKEN": "keychain:plugins:discord-bot-token"
    }
  },
  "permissions": {
    "network": true,
    "background": true,
    "secrets": ["discord.botToken"]
  },
  "capabilities": {
    "channelConnector": {
      "kind": "Discord",
      "inboundMode": "push",
      "configSchema": {
        "type": "object",
        "properties": {
          "botToken": { "type": "string", "secret": true, "label": "Bot Token" }
        },
        "required": ["botToken"]
      }
    },
    "tools": {
      "expose": ["search_messages", "list_members", "get_channel_info"]
    }
  },
  "display": {
    "description": "接入 Discord，同时提供消息搜索和成员查询工具",
    "icon": "discord.png"
  },
  "healthcheck": {
    "intervalSeconds": 30,
    "timeoutSeconds": 5
  }
}
```

#### 纯工具插件示例（需要 Keychain 治理的工具）

```json
{
  "id": "github-tools",
  "name": "GitHub 工具集",
  "version": "1.0.0",
  "types": ["tool"],
  "runtime": {
    "command": "node",
    "args": ["dist/server.js"],
    "environmentReferences": {
      "GITHUB_TOKEN": "keychain:plugins:github-pat"
    }
  },
  "permissions": {
    "network": true,
    "background": false,
    "secrets": ["github.pat"]
  },
  "capabilities": {
    "tools": {
      "expose": ["list_prs", "create_issue", "get_repo_info"]
    }
  }
}
```

> **何时选 PluginHost tool 插件，何时选 workspace/mcp.json？**
>
> - 工具需要 Keychain 保护的 secrets → PluginHost
> - 工具需要信任审批（企业内部工具） → PluginHost
> - 工具与 channel 能力打包分发 → PluginHost
> - 社区 MCP server（直接用 npx/python 启动）→ workspace/mcp.json

#### 完整 manifest 字段

| 字段 | 必须 | 说明 |
|---|---|---|
| `id` | ✅ | 全局唯一，kebab-case |
| `name` | ✅ | 显示名称 |
| `version` | ✅ | semver |
| `types` | ✅ | `["channel"]` / `["tool"]` / `["channel","tool"]` 等 |
| `runtime.command` | ✅ | 可执行文件 |
| `runtime.args` | 可选 | 命令行参数 |
| `runtime.environment` | 可选 | 明文环境变量（兼容旧格式） |
| `runtime.environmentReferences` | 可选 | Keychain 引用（推荐） |
| `capabilities.channelConnector` | channel 类型必须 | 渠道连接器配置 |
| `capabilities.tools` | tool 类型必须 | 声明暴露的工具名称列表 |
| `permissions` | ✅ | 权限声明 |
| `display` | 可选 | 描述、图标 |
| `healthcheck` | 建议 | 健康检查间隔与超时 |

### 6. PluginHost 内部架构

```
PluginLifecycleHost
  ├── 进程管理           System.Diagnostics.Process
  ├── HTTP 通信          PluginHttpAdapter（HttpClient）
  │   ├── ChannelAdapter   /channel/* 端点
  │   └── ToolAdapter      /tools/* 端点
  ├── 注册表             SqlitePluginRegistryRepository
  ├── 日志               SqlitePluginLogRepository
  ├── 向 ChannelHub 注册  PluginChannelConnector（IChannelConnector）
  └── 向 ToolRegistry 注入  plugin__{id}__{name} 命名空间工具
```

### 7. 插件生命周期

```
Discovered → Installed → Trusted → Enabled → Running ⇌ Degraded
                                            ↓
                                          Stopped
```

| 状态 | 说明 |
|---|---|
| `Discovered` | 目录扫描发现 plugin.json |
| `Installed` | manifest 通过校验，写入注册表 |
| `Trusted` | 用户在 UI 确认权限声明 |
| `Enabled` | 用户启用 |
| `Running` | 进程启动，`GET /health` 返回 ok |
| `Degraded` | 健康检查失败，自动重试中 |
| `Stopped` | 进程退出或用户手动停止 |

### 8. 权限模型

| 权限 | 说明 |
|---|---|
| `network` | 出站 HTTP/WebSocket |
| `background` | 常驻后台进程 |
| `filesystem` | 访问指定 workspace 目录 |
| `notifications` | 写入 Inbox |
| `secrets` | 声明需要哪些 Keychain secret |

Secrets 仅在进程启动时通过环境变量注入内存，不落文件。

### 9. ConnectorKind 动态注册

- 内置渠道（Telegram/飞书/Webhook）保留 enum 值，走原有 `IChannelConnector` 实现
- 插件渠道在 PluginHost 启动时向 ChannelHub 注册 `PluginChannelConnector`
- `connectorKind` 在 SQLite 存为字符串，向后兼容
- ChannelsDesk 统一展示内置和插件渠道

### 10. 失败与恢复

- 独立日志（SQLite，PluginsDesk 可查）
- 健康检查连续失败 → 自动重启，超限 → `Degraded` + Inbox 告警
- Process.Exited 事件监听，进程意外退出自动触发重启
- 单插件崩溃不影响其他插件和主 session

---

## Part 2：McpHub — MCP 生态接入

> 详见 `docs/MCP_HUB_SPEC.md`

McpHub（`KodaClaw.McpHub`）是独立模块，专门负责将 MCP 生态的工具接入 KodaClaw session：

- 读取 `~/.kodaclaw/workspace/mcp.json`（Claude Desktop 兼容格式）
- 工具命名空间：`mcp__{serverName}__{toolName}`
- 支持 stdio / http / streamableHttp / sse
- 单 server 错误隔离，不依赖 PluginHost

---

## 11. 两个来源的工具在 session 中共存

session 启动后，ToolRegistry 中可能同时存在：

```
内置工具         fs_read, fs_write, bash_run, ...
plugin__ 工具    plugin__discord-connector__search_messages
                 plugin__github-tools__list_prs
mcp__ 工具       mcp__filesystem__read_file
                 mcp__postgres__query
```

Agent 可以调用任意工具，来源对 Agent 透明，在 diagnostics 中可追溯。

---

## 12. 当前实现状态

| 功能 | 状态 |
|---|---|
| tool 插件（旧 MCP 路径）安装/信任/启停/日志 | ✅ 已实现（Iter 4，保留兼容） |
| workspace/mcp.json 工具接入（McpHub 前身） | ✅ 已实现（Iter 37，在 Runtime 层） |
| PluginHost HTTP 协议：channel 适配层 | ⏳ 待实现 |
| PluginHost HTTP 协议：tool 适配层 | ⏳ 待实现 |
| channel 插件 manifest 解析与注册 | ⏳ 待实现 |
| `PluginChannelConnector` 适配器 | ⏳ 待实现 |
| ConnectorKind 动态注册 | ⏳ 待实现 |
| McpHub 独立模块提取 | ⏳ 待实现 |
| McpServersDesk 前端 UI | ⏳ 待实现 |
| `enabled` 字段支持 | ⏳ 待实现 |
| memory / ui 插件类型 | 🔮 未来规划 |

---

## 13. 结论

KodaClaw 的扩展体系由两个职责互补的独立模块构成：

- **PluginHost**：受管插件容器，自有 HTTP 协议，支持 channel / tool / memory / ui 多类型，核心价值是治理（信任、Keychain、日志、重启）
- **McpHub**：MCP 生态直连，拥抱社区资源，零治理开销，适合快速接入现有 server

选择原则：需要治理 → PluginHost；快速接入社区资源 → McpHub。
