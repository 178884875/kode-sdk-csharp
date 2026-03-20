# Iteration 9 Freeze — Workspace Memory Live

冻结日期：2026-03-20
前置迭代：Iteration 8（Channel Turn 闭环 + Bootstrap 草稿）

---

## 1. 一句话目标

让 Koda 在主对话中真正读到自己的人格与记忆，并能在对话中将值得保留的内容写回 workspace，再由凌晨自动化完成跨日整合。

---

## 2. 范围冻结

### 2.1 纳入（In Scope）

**KC-0901 — 主会话 Workspace 上下文加载**

主会话 system prompt 必须加载以下 workspace 文件（按顺序，受 character budget 截断保护）：

```
workspace/AGENTS.md
workspace/IDENTITY.md
workspace/SOUL.md
workspace/USER.md
workspace/MEMORY.md
workspace/memory/YYYY-MM-DD.md   （当日流水，若存在）
```

与 `AutomationSessionService` 的 baseline 对齐，使用已有的 `PromptBuilder.AddContextDocuments()` 机制。
`MainSessionService.BuildSystemPrompt()` 改为异步，读取 workspace 文件后注入。

**KC-0902 — `workspace_memory_append` 工具**

注入主会话的新工具，允许 Agent 在对话中将记忆条目追加到 workspace：

- 写入目标：`workspace/memory/YYYY-MM-DD.md`（当日流水，append 模式）
- 参数：`content`（必填，记忆条目 markdown 文本）；`date`（可选，默认今天）
- 工具本身不更新 `MEMORY.md`——长期整合交由夜间 automation
- 实现：`KodaClaw.Runtime.WorkspaceMemoryAppendTool`，注入 `IWorkspaceService`
- 不需要 Approval（写入用户自己的 workspace 文件，低风险）
- 工具名注册为 `workspace_memory_append`，加入 `MainSessionOptions.DefaultTools`

**KC-0903 — HEARTBEAT.md 夜间记忆整合模板**

更新 `DefaultWorkspaceTemplates.Heartbeat()` 默认内容，加入夜间整合条目：

```yaml
## Nightly Memory Consolidation
- schedule: daily 23:45
- prompt: >
    Review today's memory captures in the daily file and consolidate them into MEMORY.md.
    Merge new facts with existing ones, remove duplicates, generalize recurring patterns,
    and discard transient details. Use workspace_memory_append to overwrite MEMORY.md
    with the updated consolidated content.
- enabled: false
- inputs:
  - MEMORY.md
  - memory/YYYY-MM-DD.md
```

`enabled: false` 默认关闭，用户在 Automations Desk 手动启用。
Automation 需要 `workspace_memory_append` 工具可用（KC-0902 的同一个工具注入 `AutomationSessionOptions.Tools`）。

### 2.2 排除（Out of Scope）

- **Option B（会话结束后自动摘要）**：延迟，不在本迭代
- **MEMORY.md 的 Web UI 编辑器**：延迟，workspace 文件编辑 UI 是独立功能
- **记忆删除 / 剪枝 API**：延迟
- **channel / automation session 的 memory 写回**：延迟，本迭代只处理主会话
- **memory 按分类存储（facts / conversations 子目录）**：保留目录结构但不在本迭代实现分类写入

---

## 3. 架构冻结

### 3.1 主会话上下文加载路径

```
MainSessionService.BuildSystemPromptAsync(workspaceRoot)
  ↓
LoadWorkspaceContextDocumentsAsync()   （新方法，类似 AutomationSessionService）
  ↓
PromptBuilder.AddContextDocuments(docs)
  ↓
system prompt 含 workspace 文件内容
```

`BuildSystemPrompt()` 改名为 `BuildSystemPromptAsync()`，与 `EnsureMainSessionAsync` 的 await 链对齐。

### 3.2 WorkspaceMemoryAppendTool 位置

```
KodaClaw.Runtime/
  WorkspaceMemoryAppendTool.cs   ← 新文件
  ServiceCollectionExtensions.cs ← 注册工具到 MainSessionOptions 和 AutomationSessionOptions
```

工具实现为 `ITool`（或继承 SDK ToolBase），通过构造函数注入 `IWorkspaceService`。
工具在宿主进程内直接调用 `WorkspaceService`，不经过 sandbox 边界。

### 3.3 每日流水文件路径

```
~/.kodaclaw/workspace/memory/2026-03-20.md
```

文件不存在时自动创建；多次 append 时在文件末尾追加，每条记忆前加时间戳行。

---

## 4. 非目标确认

- Koda 不会自动写入 `MEMORY.md`（只写当日流水），避免长期记忆被未经整合的噪声污染
- `workspace_memory_append` 工具不强制走 Approval，但写入内容会在 diagnostics 中有事件记录
- 夜间整合 automation 默认关闭，避免没有配置好模型的用户意外触发

---

## 5. 验收命令（Verification）

```bash
# L0
dotnet build products/KodaClaw/KodaClaw.sln

# L1
dotnet test products/KodaClaw/tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj \
  --filter "WorkspaceMemoryAppendToolTests"

# L2
dotnet test products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj \
  --filter "MainSessionWorkspaceContextIntegrationTests|WorkspaceMemoryAppendIntegrationTests"

# L3
dotnet test products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj \
  --filter "WorkspaceMemoryContractTests"

# Full solution
dotnet test products/KodaClaw/KodaClaw.sln -m:1
```

---

## 6. 分波计划

| Wave | 内容 | KC |
|------|------|----|
| Wave 0 | 本文档 + BACKLOG 条目 | — |
| Wave 1 | KC-0901：主会话加载 workspace 文件 | KC-0901 |
| Wave 2 | KC-0902：workspace_memory_append 工具 | KC-0902 |
| Wave 3 | KC-0903：HEARTBEAT.md 夜间整合模板 + 集成验收 | KC-0903 |
