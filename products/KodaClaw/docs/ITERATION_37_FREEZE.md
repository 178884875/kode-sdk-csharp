# Iteration 37 FREEZE — workspace/mcp.json 接入

冻结日期：2026-03-22

## 背景

`workspace/mcp.json` 在 WORKSPACE_SPEC.md 中已定义为「启动时读取」的配置文件，WorkspaceService 在初始化时会创建它（默认 `{}`），但整个 Runtime 从未读取它。用户无法通过标准 MCP 配置方式（类 Claude Desktop `mcpServers` 格式）添加工具，必须走完整的 PluginHost 安装流程（6 步）。

## 目标

让用户能直接编辑 `~/.kodaclaw/workspace/mcp.json`，在下一次 session 启动时自动将声明的 MCP server 的工具注入 Agent，无需安装、信任等管理流程。

## 用户 Outcome

用户编辑 `~/.kodaclaw/workspace/mcp.json` 添加一个 MCP server 后，重新开始对话，Agent 立刻拥有该 server 提供的工具。

```json
{
  "mcpServers": {
    "my-tool": {
      "command": "npx",
      "args": ["-y", "my-mcp-server@latest"]
    }
  }
}
```

## 范围（在 scope）

- `WorkspaceMcpConfig` / `WorkspaceMcpServerEntry` DTO（KodaClaw.Contracts）
- `IWorkspaceService.ReadMcpConfigAsync()` + WorkspaceService 实现
- `MainSessionService` 在 `BuildSessionToolsAsync` 末尾追加 workspace mcp.json 工具加载
- 支持 stdio / http / streamableHttp / sse 所有 transport（workspace 级别不受 Iteration 4 Stdio-only 限制）
- 工具命名空间：`mcp__{serverName}__{toolName}`（与 PluginHost 保持一致）
- 单 server 错误隔离：一个 server 连接失败不影响其他
- 合约测试：WorkspaceMcpConfigContractTests

## 非目标

- 热重载（mcp.json 变更不自动重载 session，下次 session 生效即可）
- Gateway API 端点管理 workspace mcp.json（直接文件编辑）
- 前端 UI 编辑 mcp.json（文本编辑器访问即可）
- 信任/权限声明（workspace 级别，用户自己负责）

## 模块

- `src/KodaClaw.Contracts/WorkspaceMcpConfig.cs`（新增）
- `src/KodaClaw.Contracts/IWorkspaceService.cs`（新增方法）
- `src/KodaClaw.Workspace/WorkspaceService.cs`（实现）
- `src/KodaClaw.Runtime/MainSessionService.cs`（注入 McpClientManager + 工具加载）
- `tests/KodaClaw.ContractTests/Workspace/WorkspaceMcpConfigContractTests.cs`（新增）

## 验收命令

```bash
dotnet build KodaClaw.sln                                                          # L0
dotnet test tests/KodaClaw.ContractTests --filter "WorkspaceMcpConfig"            # L3
dotnet test KodaClaw.sln -m:1                                                      # 全量回归
```
