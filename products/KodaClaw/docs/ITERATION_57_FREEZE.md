# Iteration 57 FREEZE — CLI+Skills 生态基础层

**日期**：2026-03-25
**类型**：新功能（新增用户可感知能力）
**范围**：`Kode.Agent.Sdk` / `KodaClaw.Cli`（新项目）/ `KodaClaw.Gateway/skills`

---

## 背景与动机

KodaClaw 当前的能力扩展路径是 MCP（`mcp.json` + McpHub），适合需要常驻进程的重量级集成。
但大量外部系统已经提供了 CLI 工具（如 `gh`、`kubectl`、`aws`），或者可以被轻量封装成 CLI。

**CLI + Skills 路线**提供了一种更轻量的集成模式：
- CLI 负责封装外部系统（认证、HTTP API 调用、输出格式化）
- SKILL.md 告诉 agent 如何使用这个 CLI（前置条件、命令参考、错误处理）
- Agent 通过 `bash_run` 执行 CLI，技术门槛极低

这条路线与 MCP 互补，而非替代。MCP 适合高频双向通信，CLI+Skills 适合面向结果的单次操作。

本迭代建立基础层：
1. SDK 层支持 `Bash(kc:*)` 级别的命令白名单（免审批、安全限界）
2. KodaClaw 自身的管理 CLI `kc`（作为第一个 pilot，同时是第三方 CLI skill 的参考实现）
3. 符合 agentskills.io 标准的 `koda-cli` SKILL.md

---

## 关键设计决策

### 1. `Bash` 别名兼容 agentskills.io 标准

agentskills.io 规范的 `allowed-tools` 使用 `Bash(git:*)` 语法（大写 `Bash`）。
KodaClaw SDK 的工具名是 `bash_run`。

**方案**：在 `SkillsLoader` 解析时做映射 `Bash` → `bash_run`，对外标准名用 `Bash`，内部实现名用 `bash_run`。SKILL.md 作者应使用 `Bash(...)` 写法。

### 2. 命令级白名单在 SDK `PermissionManager` 执行

`Bash(kc:*)` 解析为：工具 `bash_run` + 命令前缀约束 `["kc"]`。
- 执行 `kc workspace status` → 命令首 token `kc` 在白名单 → 免审批
- 执行 `rm -rf ~` → 首 token `rm` 不在白名单 → 走正常审批流
- 执行 `kc status; rm -rf ~` → 检测到 shell 元字符 `;` → 强制审批（不可绕过）

Shell 元字符检测范围：`;` `&&` `||` `` ` `` `$(` `>` `>>` `<` `|`（管道）。

### 3. `kc` CLI 用 .NET CLI 实现

- 项目路径：`tools/KodaClaw.Cli/`
- 通过 Gateway HTTP API 操作（Base URL 从 `KODACLAW_GATEWAY_URL` 或默认 `http://127.0.0.1:5076`）
- 所有子命令支持 `--json` 输出（agent 消费）和默认文本输出（人类可读）
- Exit code 语义：0 成功，1 一般错误，2 参数错误，401 认证失败，404 资源不存在

### 4. `koda-cli` SKILL.md 完全遵循 agentskills.io 标准

frontmatter 字段：`name` / `description` / `compatibility` / `allowed-tools` / `metadata`。
SKILL.md body 覆盖：认证状态检测流程、子命令参考、`--json` 输出解析、错误处理策略。

---

## 范围（IN SCOPE）

### KC-5701：SDK — `Bash` 别名 + 命令级白名单

**`SkillsLoader` 扩展**（`Kode.Agent.Sdk/Skills/SkillsLoader.cs`）：

解析 `allowed-tools` 时，识别 `Bash(kc:*)` 格式：
- 提取工具名 `Bash` → 映射为 `bash_run`
- 提取约束前缀 `kc`（去掉 `:*` 后缀）
- 存入 `SkillMetadata.AllowedTools`，格式：`bash_run[kc]`（内部表示）

**`PermissionManager` 扩展**（`Kode.Agent.Sdk/Core/PermissionManager.cs`）：

```csharp
// 内部存储：工具名 → 允许的命令前缀列表（null = 无约束）
private Dictionary<string, List<string>?>? _toolCommandConstraints;

// GrantTools 扩展：解析 "bash_run[kc]" 格式，拆分工具名和约束
public void GrantTools(IEnumerable<string> toolSpecs);

// 检查接口扩展
public PermissionResult CheckCommandPermission(string toolName, string command);
```

`CheckCommandPermission` 逻辑：
1. 检测 shell 元字符 → 返回 `RequireApproval`（不可被白名单覆盖）
2. 工具不在约束字典 → 走原有 `CheckPermission` 逻辑
3. 约束为 null（无限制）→ 允许
4. 提取命令首 token（按空格分割，取 basename）→ 是否在约束前缀列表中 → 允许 / 需审批

**`BashRunTool` 扩展**（`Kode.Agent.Sdk/Tools/Builtin/BashRunTool.cs` 或等价路径）：

执行前调用 `_permissionManager.CheckCommandPermission("bash_run", command)`，根据结果决定是否需要审批。

---

### KC-5702：新项目 — `kc` CLI

**项目结构**：
```
tools/KodaClaw.Cli/
  KodaClaw.Cli.csproj
  Program.cs                    # 入口，System.CommandLine 根命令
  Commands/
    AuthCommands.cs             # kc auth status / login
    WorkspaceCommands.cs        # kc workspace status
    AutomationCommands.cs       # kc automation list / run <id>
  HttpGatewayClient.cs          # Gateway HTTP client（读 KODACLAW_GATEWAY_URL）
  OutputFormatter.cs            # 文本 / JSON 双模式输出
```

**依赖**：
- `System.CommandLine`（.NET 官方 CLI 框架）
- `System.Text.Json`（已有）

**子命令规格**：

```
kc auth status [--json]
  → GET /api/system/status（或等价接口）
  → 输出：{ "authenticated": true, "gatewayUrl": "..." }
  → 文本：✓ Connected to http://127.0.0.1:5076

kc workspace status [--json]
  → GET /api/workspace/readiness
  → 输出：{ "ready": true, "gaps": [] }
  → 文本：Identity ✓  Soul ✓  User ✓

kc automation list [--json]
  → GET /api/automations
  → 文本：表格形式（id / name / cron / enabled / lastRunAt）

kc automation run <id> [--json]
  → POST /api/automations/{id}/trigger
  → 文本：✓ Triggered automation <name>
```

**Exit code 语义**：
| Code | 含义 |
|------|------|
| 0 | 成功 |
| 1 | 一般错误（含 Gateway 连接失败） |
| 2 | 参数错误 |
| 3 | 资源不存在（404） |
| 4 | 认证失败（401） |

---

### KC-5703：`koda-cli` SKILL.md（agentskills.io 标准）

**文件路径**：`src/KodaClaw.Gateway/skills/koda-cli/SKILL.md`

**Frontmatter**：
```yaml
---
name: koda-cli
description: KodaClaw management CLI (kc). Use when user wants to check system status,
  manage automations, inspect workspace, or perform admin operations on their KodaClaw
  instance. Activate when user mentions automation, workspace status, or system management.
compatibility: Requires kc CLI. Install: included with KodaClaw desktop app or build
  from source at tools/KodaClaw.Cli/.
allowed-tools: Bash(kc:*)
metadata:
  version: "1.0"
  kind: builtin-core
---
```

**Body 覆盖内容**：
1. 前置条件检测（`kc auth status --json` → 解析 `authenticated` 字段）
2. Gateway 连接失败处理（提示用户确保 KodaClaw 正在运行）
3. 各子命令使用示例（含 `--json` 输出样例）
4. `--json` 输出解析指引（agent 应解析 JSON 而非文本）
5. 错误处理策略（exit code 映射表）

---

### KC-5704：测试

| 层级 | 内容 | 测试文件 |
|------|------|---------|
| L1 | `PermissionManager` 命令白名单：首 token 匹配、basename 提取、约束为 null 时放行 | `PermissionManagerCommandConstraintTests` (~8 个) |
| L1 | Shell 元字符检测：`;` `&&` `\|\|` `\`...\`` `$(` `>` `\|` 各自触发强制审批 | `ShellMetacharDetectionTests` (~10 个) |
| L1 | `SkillsLoader` 解析 `Bash(kc:*)` → `bash_run[kc]`；`Bash` 别名映射 | `SkillsLoaderBashAliasTests` (~6 个) |
| L2 | `kc automation list` + `kc automation run` 集成测试（WebApplicationFactory + 真实 HTTP） | `KcCliIntegrationTests` (~5 个) |
| L3 | `koda-cli/SKILL.md` frontmatter 契约（name/description/allowed-tools/compatibility 字段合规） | `KodaCliSkillContractTests` (~4 个) |
| L0 | `dotnet build` 0 错 0 警告；`npm run typecheck` 通过 | — |

---

## 非目标（OUT OF SCOPE）

- **第三方 CLI skill 分发市场**：koda-cli 作为参考实现，生态建设留 future
- **`kc install` / skill 包管理**：手动放 `~/.kodaclaw/skills/` 即可，自动安装留 future
- **长命令流式输出**：`kc automation run` 等待完成后一次性返回，流式留 future
- **CLI 执行审计 UI**：诊断事件已够，Inbox 展示留 future
- **`kc model` / `kc channel` 子命令**：第一批只做 auth/workspace/automation
- **Windows PATH 适配**：`kc.exe` 支持，但 shell 元字符检测优先覆盖 Unix 场景
- **`kc` 独立发布（brew tap 等）**：本迭代只做本地构建，打包发布留 future

---

## 关键契约

### `allowed-tools` 解析（SKILL.md → SDK 内部）

```
输入（SKILL.md frontmatter）:  Bash(kc:*) Bash(git:*) fs_read
解析结果（内部表示）:
  bash_run → 约束前缀 ["kc", "git"]
  fs_read  → 无约束（原有逻辑）
```

### Shell 元字符检测

```csharp
// 命中任意一项 → 返回 RequireApproval，不可被白名单覆盖
private static readonly string[] DangerousPatterns =
[
    ";", "&&", "||", "`", "$(", ">", ">>", "<", " | "
];
```

### `kc` CLI JSON 输出样例

```jsonc
// kc auth status --json
{ "authenticated": true, "gatewayUrl": "http://127.0.0.1:5076", "version": "0.1.0" }

// kc workspace status --json
{ "ready": true, "gaps": [] }

// kc automation list --json
[
  { "id": "heartbeat", "name": "Daily Summary", "cronExpression": "0 8 * * *",
    "enabled": true, "lastRunAt": "2026-03-25T08:00:00Z" }
]

// kc automation run <id> --json
{ "ok": true, "automationId": "heartbeat", "sessionId": "auto-xxx" }
```

### 受影响模块

| 模块 | 变更类型 |
|------|---------|
| `Kode.Agent.Sdk` | `SkillsLoader` + `PermissionManager` + `BashRunTool` 扩展 |
| `tools/KodaClaw.Cli` | **新建项目**：`kc` CLI |
| `KodaClaw.Gateway/skills/koda-cli` | **新建 SKILL.md**：agentskills.io 标准 |
| `KodaClaw.sln` | 引入 `KodaClaw.Cli` 项目 |

**零改动模块**：`KodaClaw.Workspace`、`KodaClaw.Runtime`、`KodaClaw.ChannelHub`、`KodaClaw.Automation`、`KodaClaw.ControlPlane`、`KodaClaw.Storage`、`kodaclaw-web`

---

## 验证矩阵

| 层级 | 验证内容 |
|------|---------|
| L0 | `dotnet build` 0 错 0 警告 |
| L1 | `PermissionManager` 命令白名单 + 元字符检测 |
| L1 | `SkillsLoader` `Bash(kc:*)` 解析 + `Bash` 别名映射 |
| L2 | `kc automation list/run` 真实 HTTP 调用（WebApplicationFactory） |
| L3 | `koda-cli/SKILL.md` frontmatter 契约合规 |
| L5 | Dogfood：在主会话激活 koda-cli skill，让 agent 运行 `kc automation list --json`，解析结果回复用户 |

---

## 依赖与风险

**新增 NuGet**：`System.CommandLine`（稳定版，Microsoft 官方）

**风险**：`bash_run` 工具当前实现位置需要确认（SDK 内置还是 KodaClaw.Runtime 注册）。
若在 Runtime 注册，`CheckCommandPermission` 的调用点需在 Runtime 层而非 SDK 层——需在 KC-5701 实现时先确认调用链。
