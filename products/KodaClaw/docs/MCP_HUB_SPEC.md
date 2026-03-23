# KodaClaw McpHub 规范

## 1. 模块定位

`KodaClaw.McpHub` 是 KodaClaw 的 **MCP 生态接入层**，唯一职责是将外部 MCP server 的工具能力注入到 KodaClaw 的 Agent session 中。

它与 PluginHost 完全独立：

| | KodaClaw.McpHub | KodaClaw.PluginHost |
|---|---|---|
| 协议 | MCP | 自定义 HTTP |
| 用途 | 工具扩展 | 渠道扩展 |
| 配置方式 | 编辑 workspace/mcp.json | 安装 plugin.json 插件 |
| 进程管理 | 不管（按需连接） | 常驻进程生命周期 |
| 信任门控 | 无（用户自负） | 安装 → 信任 → 启用 |

## 2. 配置来源

McpHub 读取 `~/.kodaclaw/workspace/mcp.json`，格式与 Claude Desktop 完全兼容：

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "~/Documents"]
    },
    "postgres": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-postgres", "postgresql://localhost/mydb"]
    },
    "web-search": {
      "transport": "streamableHttp",
      "url": "https://open.bigmodel.cn/api/mcp/web_search_prime/mcp",
      "headers": { "Authorization": "Bearer YOUR_TOKEN" }
    },
    "my-tool": {
      "command": "python3",
      "args": ["~/tools/my_server.py"],
      "enabled": false
    }
  }
}
```

### 字段说明

| 字段 | 必须 | 说明 |
|---|---|---|
| `transport` | 可选 | `stdio`（默认）/ `http` / `streamableHttp` / `sse` |
| `command` | stdio 必须 | 可执行文件 |
| `args` | 可选 | 命令行参数 |
| `env` | 可选 | 环境变量（明文，用户自管） |
| `url` | HTTP/SSE 必须 | 端点 URL |
| `headers` | 可选 | HTTP headers（含 Authorization） |
| `enabled` | 可选 | 默认 `true`；`false` 时跳过，不影响其他 server |

## 3. 核心行为

### 3.1 Session 启动时注入工具

每次主 session 启动时，McpHub 依次处理 mcp.json 中的每个 server entry：

```
读取 mcp.json
  ↓
过滤 enabled=false 的 entry
  ↓
为每个 entry 建立 MCP 连接（按 transport 类型）
  ↓
调用 ListTools，获取工具列表
  ↓
注册到 session ToolRegistry（命名空间：mcp__{serverName}__{toolName}）
  ↓
单 server 失败 → 记录诊断事件，继续处理其他 server（错误隔离）
```

### 3.2 工具命名空间

所有 MCP 工具以 `mcp__{serverName}__{toolName}` 形式注册，与内置工具完全隔离：

```
filesystem 的 read_file  → mcp__filesystem__read_file
postgres 的 query        → mcp__postgres__query
web-search 的 search     → mcp__web-search__search
```

### 3.3 错误隔离

单个 server 连接失败或 ListTools 超时时：
- 该 server 的工具不可用
- 其他 server 不受影响
- 记录诊断事件 `mcp_hub.server.fetch_failed`（含 serverName 和错误信息）
- session 正常启动，不因 MCP 失败而中断

### 3.4 连接生命周期

- stdio transport：session 启动时 spawn 进程，session 结束时终止
- http/streamableHttp/sse transport：session 启动时建立连接，session 结束时断开
- 连接由 `McpClientManager`（SDK）管理，McpHub 不自己维护连接状态

## 4. 模块接口

```csharp
// 主要服务接口
public interface IMcpHubService
{
    // session 启动时调用，返回注入的工具数量
    Task<McpHubInjectionResult> InjectToolsAsync(
        IToolRegistry toolRegistry,
        CancellationToken cancellationToken = default);
}

public sealed record McpHubInjectionResult(
    int ServerCount,
    int ToolCount,
    int FailedServerCount,
    IReadOnlyList<string> FailedServers);
```

## 5. 诊断事件

| 事件类型 | 级别 | 说明 |
|---|---|---|
| `mcp_hub.server.injected` | info | server 成功注入，附 toolCount |
| `mcp_hub.server.fetch_failed` | warning | server 连接或 ListTools 失败，附 error |
| `mcp_hub.server.skipped` | info | enabled=false，已跳过 |

## 6. 与 Runtime 的关系

McpHub 由 `MainSessionService` 在 session 构建阶段调用：

```
MainSessionService.BuildSessionToolsAsync()
  ├── 内置工具注册
  ├── IMcpHubService.InjectToolsAsync()   ← McpHub 负责
  └── （渠道插件工具由 PluginHost 另外注入，未来）
```

## 7. 未来扩展

当前 McpHub 只处理 workspace/mcp.json。未来可扩展：

- **Project-level mcp.json**：项目目录下的 `.kodaclaw/mcp.json`（优先级高于 workspace）
- **热重载**：文件变更时自动重新加载，无需重启 session
- **McpServersDesk**：前端 UI 管理界面（增删改 entry，enable/disable toggle）
- **连接池**：跨 session 复用已连接的 server（避免重复 spawn）

## 8. 当前实现状态

Iter 37 已实现的功能（位于 `KodaClaw.Runtime/MainSessionService.cs`）：

- ✅ 读取 workspace/mcp.json（`IWorkspaceService.ReadMcpConfigAsync`）
- ✅ 四种 transport 支持（stdio/http/streamableHttp/sse）
- ✅ 工具注册 + 去重
- ✅ 单 server 错误隔离
- ✅ 诊断事件（`main_session.workspace_mcp.fetch_failed` / `.injected`）
- ✅ `enabled` 字段过滤（Iter 38 计划）

待完成：
- ⏳ 提取为独立 `KodaClaw.McpHub` 模块（当前逻辑在 Runtime 层）
- ⏳ `IMcpHubService` 接口抽象
- ⏳ McpServersDesk 前端 UI
