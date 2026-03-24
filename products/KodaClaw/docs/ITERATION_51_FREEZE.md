# Iteration 51 FREEZE — Workspace Git 版本管理

**日期**：2026-03-24
**类型**：新功能
**范围**：KodaClaw.Workspace / KodaClaw.Runtime / KodaClaw.Gateway / kodaclaw-web

---

## 背景与动机

当前 workspace 协议文件（`IDENTITY.md`、`SOUL.md`、`MEMORY.md` 等）由 Agent 通过 `workspace_protocol_update` / `workspace_memory_append` 直接覆盖写入，没有任何历史记录：

- Agent 在某次对话中悄悄修改了用户人格文件，用户无从察觉
- 写错后只能靠 TimeMachine 或手动备份恢复，且恢复路径对普通用户不可用
- 无法区分"Agent 写的"还是"用户在 Settings 手动改的"

这是 KodaClaw 核心设计原则 **"inspectable autonomy"** 的最大缺口。

**解决方案**：将 `~/.kodaclaw/` 纳入 git 版本管理，协议文件每次写入后自动 commit，并在 Settings Desk 提供 Workspace 历史面板，支持查看变更 diff 和按文件回滚。

**技术选型**：`LibGit2Sharp`（libgit2 的 .NET binding），内嵌到发布包，用户无需安装系统 git，跨平台（macOS / Windows / Linux）零外部依赖。

---

## 范围（IN SCOPE）

### 轨道一：WorkspaceGitService 基础设施

新建 `WorkspaceGitService`（在 `KodaClaw.Workspace` 模块），提供：

**`EnsureGitRepoAsync()`**（幂等，`EnsureInitializedAsync` 时调用）：
- 检测 `Repository.IsValid(rootPath)`，已是 git repo 则跳过，零开销返回
- **损坏的 `.git` 处理**：若 `.git` 目录存在但 `IsValid` 为 false（损坏），先删除 `.git` 再重新 init，避免 `Repository.Init` 因目录已存在抛异常
- 新建（首次用户）：`Repository.Init(rootPath)` → 写入 `.gitignore` + `.gitattributes` → 初始 commit `workspace(init)[system/init]: initialize workspace repository`
- 存量迁移（升级用户）：workspace 已有文件但无 `.git` 时，init 后把现有协议文件全量 commit `workspace(migrate)[system/migrate]: import existing workspace into git`；历史从此刻起记录，升级前的变更不可追溯（UI 里说明）

**DI 注册**：`IWorkspaceGitService` 必须注册为 **Singleton**。`SemaphoreSlim` 是实例级别的，Transient/Scoped 注册会导致并发保护失效。

**`TryCommitAsync(string message, string source)`**（非阻塞容错）：
- `SemaphoreSlim(1,1)` 序列化所有 git 写操作，防止并发 index.lock 冲突
- 只 stage 协议文件（`workspace/`、`config/models.json`、`config/plugins.json`、`config/app.json`）
- `repo.RetrieveStatus().IsDirty` 为 false 时跳过，不产生空 commit
- 任何 LibGit2Sharp 异常静默吞掉并返回 `false`，不影响主业务流程

**`.gitignore` 内容**（EnsureGitRepo 时写入）：
```gitignore
sessions/
logs/
cache/
media/
identity/device.json
config/gateway.json
workspace/canvas/artifacts/
workspace/knowledge/*
!workspace/knowledge/**/*.md
```

**`.gitattributes` 内容**（跨平台换行归一化）：
```
* text=auto eol=lf
*.md text eol=lf
*.json text eol=lf
```

**Commit signature**：`KodaClaw <agent@kodaclaw.local>`，timestamp = `DateTimeOffset.UtcNow`

**Commit message 规范**：
```
workspace({target})[{source}]: {description}

source 取值：
  agent              ← WorkspaceProtocolUpdateTool / workspace_memory_append 调用（不区分 session 类型，避免 ToolContext 耦合）
  user/settings-desk ← 用户在 Settings Desk 手动编辑
  system/init        ← EnsureInitializedAsync
  system/migrate     ← 存量 workspace 迁移
```

### 轨道二：4 个集成点 auto-commit

以下位置在文件写入后调用 `TryCommitAsync`：

| 调用位置 | commit message 示例 |
|---------|-------------------|
| `WorkspaceProtocolUpdateTool.ExecuteAsync` | `workspace(memory)[agent/main-session]: update section 'Daily 2026-03-24'` |
| `WorkspaceMemoryAppendTool.ExecuteAsync` | `workspace(memory)[agent/main-session]: append memory entry` |
| `PUT /api/workspace/file` Gateway 端点 | `workspace(identity)[user/settings-desk]: edit via Settings Desk` |
| `WorkspaceService.EnsureInitializedAsync` | `workspace(init)[system/init]: initialize workspace repository` |

`source` 参数由调用方传入，Gateway 端点固定为 `user/settings-desk`，工具调用由 session context 中的 SessionKind 推断。

### 轨道三：Gateway History API

**`GET /api/workspace/git/log?limit=50`**

返回最近 N 次 commit 列表：
```json
{
  "commits": [
    {
      "hash": "a1b2c3d",
      "shortHash": "a1b2c3d",
      "message": "workspace(memory)[agent/main-session]: update section 'Daily 2026-03-24'",
      "author": "KodaClaw",
      "committedAt": "2026-03-24T09:15:32Z",
      "changedFiles": ["workspace/MEMORY.md"]
    }
  ]
}
```

**`GET /api/workspace/git/diff/{hash}`**

返回指定 commit 相对于父 commit 的文件级 diff（unified diff 格式，文本）。

**`POST /api/workspace/git/revert-file`**

```json
{ "hash": "a1b2c3d", "filePath": "workspace/MEMORY.md" }
```

将指定文件恢复到 `hash` 对应版本，并产生新的 revert commit：
`workspace(memory)[user/settings-desk]: revert to a1b2c3d`

不使用 `git revert`（会产生反向 patch），而是直接 `git checkout <hash> -- <file>` 语义（LibGit2Sharp 读取 blob → 写文件 → commit）。

**filePath 白名单**：只允许 `workspace/` 前缀的路径，`config/`、`identity/` 一律拒绝并返回 400。同时做 path traversal 防护（禁止 `..`）。

### 轨道四：Settings Desk Workspace 历史面板

在 Settings Desk 新增"变更历史"分区（`HistorySection.tsx`）：

- 加载 `GET /api/workspace/git/log`（30 条）
- 每条 commit 展示：时间、source badge（`[agent]` / `[user]` / `[system]`）、变更文件列表
- 点击 commit 行展开 diff（调 `GET /api/workspace/git/diff/{hash}`），以 `<pre>` 渲染 unified diff
- 每条 commit 提供"回滚此文件"按钮（调 `POST /api/workspace/git/revert-file`），回滚后刷新列表

UI 说明文案需包含隐私悖论告知：
> "Workspace 历史记录了 Agent 和你对工作区文件的所有修改。编辑文件后旧内容仍存在于 git 历史中——如需完全清除历史，请使用'重置变更历史'功能。"

---

## 非目标（OUT OF SCOPE）

- 不做多机同步（不配置 remote，不 push）
- 不做 knowledge/ 二进制文件的 Git LFS 支持（当前 knowledge/*.md 纳入，二进制排除）
- 不做 git gc 自动化（libgit2 内部会处理 loose objects，显式 gc 留后续）
- 不做"清除敏感 git 历史"的高级工具（`git filter-branch` / `git rebase` 留 Phase 2）
- 不做 git blame 展示（超出当前 UI 需求）
- 不改变 workspace 文件的读取逻辑（只影响写入后的 commit 副作用）
- 不对 sessions/、logs/、cache/ 做任何 git 操作

---

## 关键契约

### WorkspaceGitCommit DTO

```csharp
public sealed record WorkspaceGitCommit(
    string Hash,
    string ShortHash,
    string Message,
    string Author,
    DateTimeOffset CommittedAt,
    IReadOnlyList<string> ChangedFiles);
```

### IWorkspaceGitService 接口

```csharp
public interface IWorkspaceGitService
{
    /// <summary>幂等初始化 git repo；存量 workspace 自动迁移。</summary>
    Task EnsureGitRepoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 暂存协议文件并 commit。没有变更时返回 false（不产生空 commit）。
    /// git 不可用或 commit 失败时返回 false，不抛异常。
    /// </summary>
    Task<bool> TryCommitAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>读取最近 N 条 commit 记录。git repo 不存在时返回空列表。</summary>
    Task<IReadOnlyList<WorkspaceGitCommit>> GetRecentCommitsAsync(
        int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>返回指定 commit 相对于其父的 unified diff 文本。</summary>
    Task<string> GetCommitDiffAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将 filePath 恢复到 hash 时的版本，并产生新 revert commit。
    /// 返回新 commit 的 hash。
    /// </summary>
    Task<string> RevertFileToCommitAsync(
        string hash, string filePath, CancellationToken cancellationToken = default);
}
```

### Gateway 端点

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/api/workspace/git/log` | 返回 `WorkspaceGitLogResponse`（commit 列表）|
| `GET` | `/api/workspace/git/diff/{hash}` | 返回 `text/plain` unified diff |
| `POST` | `/api/workspace/git/revert-file` | body: `{ hash, filePath }`；返回 `{ newHash }` |

---

## 受影响模块

| 模块 | 变更类型 |
|------|---------|
| `KodaClaw.Workspace` | 新增 `WorkspaceGitService`、`IWorkspaceGitService`；`WorkspaceService.EnsureInitializedAsync` 调 git init；新增 NuGet `LibGit2Sharp` |
| `KodaClaw.Runtime` | `WorkspaceProtocolUpdateTool`、`WorkspaceMemoryAppendTool` 写入后调 `TryCommitAsync` |
| `KodaClaw.Gateway` | 新增 `GatewayApp.WorkspaceGitEndpoints.cs`；`WorkspaceGitService` 注册到 DI |
| `KodaClaw.Contracts` | 新增 `WorkspaceGitCommit`、`WorkspaceGitLogResponse`、`WorkspaceGitRevertFileRequest` |
| `kodaclaw-web` | `api.ts` 新增 3 个 git API 调用；Settings Desk 新增 `HistorySection.tsx` |

---

## 验证矩阵

| 层级 | 验证内容 | 工具 |
|------|---------|------|
| L0 | `dotnet build` 0 错 0 警告；`npm run typecheck` 通过 | dotnet / tsc |
| L1 | `WorkspaceGitServiceTests`：新建 repo 幂等；存量迁移（已有文件 → init → 迁移 commit）；损坏 `.git` 自动修复；TryCommit 无变更 skip；并发 10 个 TryCommit 无死锁无 index.lock 异常；.gitignore 内容校验 | xUnit + temp dir |
| L2 | `WorkspaceGitIntegrationTests`：WorkspaceProtocolUpdateTool 写入后 git log 有新 commit；commit message 含正确 source tag；回滚文件后内容还原 | WebApplicationFactory |
| L3 | `WorkspaceGitContractTests`：`WorkspaceGitCommit` / `WorkspaceGitLogResponse` JSON 序列化 round-trip | FluentAssertions |
| L5 | Dogfood：在主会话让 Agent 更新 MEMORY.md → Settings Desk 历史面板出现该 commit → 展开 diff 可读 → 点回滚文件内容还原 | 真实 Gateway + Web |
