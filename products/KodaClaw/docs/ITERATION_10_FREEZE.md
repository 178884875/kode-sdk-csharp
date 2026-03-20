# Iteration 10 Freeze — Workspace Protocol Write

冻结日期：2026-03-20
前置迭代：Iteration 9（Workspace Memory Live）

---

## 1. 一句话目标

让 Koda 在主对话中能够语义化地更新 workspace 协议文件（IDENTITY / SOUL / USER / MEMORY / AGENTS），用户只需说自然语言，Agent 自主决定写哪个文件、写哪个章节、写什么内容；同时修正夜间整合模板中引用了错误工具名的问题。

---

## 2. 范围冻结

### 2.1 纳入（In Scope）

**KC-1001 — `workspace_protocol_update` 工具**

注入主会话的新工具，允许 Agent 对协议文件做章节级 patch：

- 支持 target：`identity` | `soul` | `user` | `memory` | `agents`
- 文件映射：
  ```
  identity → workspace/IDENTITY.md
  soul     → workspace/SOUL.md
  user     → workspace/USER.md
  memory   → workspace/MEMORY.md
  agents   → workspace/AGENTS.md
  ```
- 参数：
  - `target`（必填）：目标协议文件枚举值
  - `section`（可选）：`##` 小节标题；不提供则替换 `#` 标题之后的全部正文
  - `content`（必填）：该小节的新内容（Agent 决定，不做格式校验）
- Patch 语义：**不是 append，不是整体覆写**
  - 找到 `## {section}` heading → 替换该节内容 → 保留其他章节
  - section 不存在时：在文件末尾追加新章节
  - section 为空时：替换 `#` 标题行之后的所有内容
- 不需要 Approval（写入用户自己的 workspace 协议文件，低风险）
- 触发诊断事件：`workspace_protocol_updated`（含 target、section、字节数）
- 注册到 `MainSessionOptions.DefaultTools`

**KC-1002 — 修正 HEARTBEAT.md 夜间整合模板**

`DefaultWorkspaceTemplates.Heartbeat()` 的 `Nightly Memory Consolidation` 段落当前错误地引用了 `workspace_memory_append` 来覆写 `MEMORY.md`，而 `workspace_memory_append` 只能 append 到日记文件。

- 将 prompt 中的引导语改为使用 `workspace_protocol_update` with `target=memory`
- 补充 contract 测试 `WorkspaceTemplateContractTests`，验证：
  - Heartbeat 模板包含 `workspace_protocol_update`
  - Heartbeat 模板包含 `target=memory`
  - Heartbeat 模板不再包含对 `MEMORY.md` 的 `workspace_memory_append` 引导

### 2.2 排除（Out of Scope）

- **HEARTBEAT.md 的语义编辑**：YAML-like automation 语法复杂，另起 KC 处理
- **字段级 key-value 追加**：本迭代只做 section-level patch，不做 `- Key: Value` 行级增量
- **文件内容格式校验**：Agent 决定内容，工具不做结构校验
- **编辑历史 / 版本回滚**：不在本迭代，用户可通过 Agent + fs_read 手动恢复
- **channel / automation session 写回**：本迭代只注入主会话
- **BOOTSTRAP.md 的语义编辑**：bootstrap 流程由 BootstrapDraftService 专门处理

---

## 3. 架构冻结

### 3.1 WorkspaceProtocolUpdateTool 位置

```
KodaClaw.Runtime/
  WorkspaceProtocolUpdateTool.cs   ← 新文件
  ServiceCollectionExtensions.cs   ← 注册工具到 MainSessionOptions
```

工具实现为 `ToolBase<WorkspaceProtocolUpdateArgs>`，通过构造函数注入 `IWorkspaceService`，在宿主进程内直接调用文件系统，不经过 sandbox 边界。

### 3.2 Section Patch 算法

```
1. 读取目标文件全部内容（文件不存在则从对应 DefaultWorkspaceTemplates 初始化）
2. 按行分割
3. if section == null:
     保留第一行（# 标题），替换其余所有行为 content
   else:
     找到 "## {section}" 行的位置
     if 找到:
       收集从该行到下一个 ## 或 EOF 之前的行（即该节的旧内容）
       用 content 替换这段旧内容，保留其他章节
     else:
       在文件末尾追加 "## {section}\n{content}"
4. 写回文件
5. 触发 workspace_protocol_updated 事件
```

### 3.3 目标文件路径解析

```
{workspaceService.RootPath}/workspace/{TargetToFileName(target)}
```

`TargetToFileName`：
```
identity → IDENTITY.md
soul     → SOUL.md
user     → USER.md
memory   → MEMORY.md
agents   → AGENTS.md
```

---

## 4. 非目标确认

- `workspace_protocol_update` 不写入 HEARTBEAT.md（YAML 语法不同，另起 KC）
- 工具不验证 content 格式，Agent 负责生成合理的 markdown 内容
- 修改不会使当前会话的 system prompt 热更新，下次会话开始时才生效
- 工具不产生审批请求，但 diagnostics 中有完整事件记录

---

## 5. 验收命令（Verification）

```bash
# L0
dotnet build products/KodaClaw/KodaClaw.sln

# L1
dotnet test products/KodaClaw/tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj \
  --filter "WorkspaceProtocolUpdateToolTests"

# L2
dotnet test products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj \
  --filter "WorkspaceProtocolUpdateIntegrationTests"

# L3
dotnet test products/KodaClaw/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj \
  --filter "WorkspaceTemplateContractTests"

# Full solution
dotnet test products/KodaClaw/KodaClaw.sln -m:1
```

---

## 6. 分波计划

| Wave | 内容 | KC |
|------|------|----|
| Wave 0 | 本文档 + BACKLOG 条目 | — |
| Wave 1 | KC-1001：WorkspaceProtocolUpdateTool 实现 + L1/L2 测试 | KC-1001 |
| Wave 2 | KC-1002：Heartbeat 模板修正 + L3 契约测试 | KC-1002 |
