# Iteration 38 冻结文档

**日期**：2026-03-22
**状态**：已冻结

## 目标

将 workspace/mcp.json 接入能力从 Runtime 层提取为独立模块 `KodaClaw.McpHub`，补齐 `enabled` 字段支持，实现前后端完整管理界面（McpServersDesk），并支持连通性测试与运行状态展示。

## 范围

### 纳入

- `WorkspaceMcpServerEntry` 新增 `enabled` 字段（nullable bool，缺省 true，向后兼容）
- `KodaClaw.McpHub` 独立项目：`IMcpHubService` 接口 + `McpHubService` 实现
- 将 `MainSessionService.BuildWorkspaceMcpToolsAsync()` 逻辑迁移到 McpHub 模块
- `IWorkspaceService` 新增 `SaveMcpConfigAsync()`，`WorkspaceService` 实现
- Gateway 端点：`GET /api/mcp-servers`、`PUT /api/mcp-servers`
- Gateway 端点：`POST /api/mcp-servers/{name}/test-connection`（连通性探测：尝试连接并调 ListTools，返回成功/失败及工具数量）
- 前端 `McpServersDesk`：列表 + enable/disable toggle + 添加表单 + 删除
- 前端 McpServersDesk：每个 server 行显示连通状态徽章 +「测试连接」按钮（点击调 test-connection 端点）
- Sidebar 能力层新增"MCP 直连"入口
- 契约测试：`enabled` 字段序列化，GET/PUT API schema

### 不纳入

- McpHub 热重载（文件变更自动重新加载，留到后续迭代）
- 跨 session 连接复用（连接池）
- Project-level mcp.json（项目目录优先级）
- PluginHost HTTP 协议（独立迭代）
- McpHub → PluginHost tool 类型集成
- 连通状态自动轮询/实时推送（本迭代仅支持手动点击测试，不做后台自动刷新）

## 模块边界

```
KodaClaw.McpHub
  依赖：KodaClaw.Contracts, Kode.Agent.Mcp, Kode.Agent.Sdk
  不依赖：KodaClaw.PluginHost, KodaClaw.ChannelHub, KodaClaw.Runtime

KodaClaw.Runtime
  依赖变化：新增 KodaClaw.McpHub 引用
  变化：MainSessionService 用 IMcpHubService 替换内联逻辑
```

## 验证命令

```bash
# L0
dotnet build KodaClaw.sln

# L1
dotnet test tests/KodaClaw.UnitTests

# L3
dotnet test tests/KodaClaw.ContractTests --filter "McpHub|WorkspaceMcp"

# L2
dotnet test tests/KodaClaw.IntegrationTests --filter "McpServers"

# 全量回归
dotnet test KodaClaw.sln -m:1

# 前端
cd apps/kodaclaw-web && npm run typecheck && npm run test
```

## 非目标

- 不改变 workspace/mcp.json 的存储格式（Claude Desktop 兼容格式保持不变）
- 不做连通状态自动后台轮询（手动触发即可）
