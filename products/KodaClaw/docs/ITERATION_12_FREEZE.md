# Iteration 12 Freeze — Agent 主动性输出三件套

冻结日期：2026-03-20
前置迭代：Iteration 11（HEARTBEAT.md → SQLite 热更链路）

---

## 1. 一句话目标

补齐 Agent 在对话中主动输出的三条断链：写入自动化规则（HEARTBEAT.md）、发布 Canvas 内容、推送 Inbox 通知，使 PRODUCT.md 中"Agent 产生结果→可见/可追踪"的核心闭环得以完整运行。

---

## 2. 范围冻结

### 2.1 纳入（In Scope）

**KC-1201 — heartbeat target：workspace_protocol_update 支持写入 HEARTBEAT.md**

在 `WorkspaceProtocolUpdateTool.TargetFileMap` 加入 `["heartbeat"] = KodaClawWorkspaceLayout.HeartbeatFile`。

Agent 对话中可调用：
```json
{ "target": "heartbeat", "section": "Daily Morning Digest",
  "content": "- schedule: daily 09:00\n- prompt: ...\n- enabled: true" }
```
→ HEARTBEAT.md 的对应 `##` section 被创建或更新
→ `HeartbeatFileWatcherHostedService` 触发 `HeartbeatSyncService.SyncAsync()`
→ SQLite 自动更新 → `AutomationScheduler` 开始调度

该 KC 依赖 Iteration 11 的热更链路已完成（KC-1101 / KC-1102）。

---

**KC-1202 — canvas_upsert：Agent 可发布内容到 Canvas**

新增 `CanvasUpsertTool`，让 Agent 可以把报告、任务看板、HTML 内容推送到 Canvas。

路径模型（已与 `GatewayApp.CanvasFiles.cs` 的 `TryResolveCanvasFilePath` 对齐）：
- 文件写入：`{workspaceRoot}/workspace/canvas/{id}/index.{ext}`
- `EntryPath`（存 SQLite）：`workspace/canvas/{id}/index.{ext}`
- `AssetDirectory`：`workspace/canvas/{id}/`
- `Source`：`"agent"`

参数设计：
```
id?          — 省略时自动生成 "canvas-{guid8}"
title        — 展示名称
kind         — report | html | board | tasklist | dashboard（大小写不敏感）
content      — markdown 或 html 字符串
content_type — markdown | html
summary?     — 简介
session_id?  — 关联 session
```

注入：`IWorkspaceService` + `ICanvasArtifactRepository`（两者均在 `KodaClaw.Contracts`，Runtime 已引用）。

---

**KC-1203 — inbox_create：Agent 可主动推送 Inbox 通知**

新增 `InboxCreateTool`，让 Agent 在对话中把关键结论或待跟进事项写入 Inbox。

- `Kind = InboxItemKind.Information`，`Source = "agent"`
- `Status = InboxItemStatus.Open`
- `id = "agent-note-{guid12}"`

参数设计：
```
title           — 通知标题
summary         — 内容摘要
requires_action — 是否需要用户操作，默认 false
route?          — 点击跳转路径（如 "/canvas/xxx"）
```

注入：`IInboxRepository`（已在 `KodaClaw.Contracts`，Runtime 已引用）。

---

### 2.2 排除（Out of Scope）

- **Canvas 内容渲染 UI**：Canvas 面板的 Web 展示效果不在本迭代，UI 侧已有 iframe 渲染入口
- **canvas_upsert 的 multi-file artifact**：本迭代只支持单文件内容（`index.md` / `index.html`），复杂多文件 artifact 留后续
- **heartbeat section 删除工具**：`workspace_protocol_update` 目前不支持删除 `##` section，HEARTBEAT 的删除由用户手动编辑文件后 FileWatcher 触发同步
- **inbox_create 的 ApprovalId / PayloadJson**：本迭代 `Information` 类型通知不含审批链接和结构化 payload
- **Desktop 通知推送**：Inbox 更新 → 系统通知的链路属于 Electron 层，不在本迭代

---

## 3. 架构冻结

### 3.1 模块放置

三个新工具全部放在 `KodaClaw.Runtime`（与已有 `WorkspaceMemoryAppendTool` / `WorkspaceProtocolUpdateTool` 同层）：

```
KodaClaw.Runtime/
  WorkspaceProtocolUpdateTool.cs   ← 修改（加 heartbeat target + 更新 Description）
  CanvasUpsertTool.cs              ← 新增（KC-1202）
  InboxCreateTool.cs               ← 新增（KC-1203）
  MainSessionOptions.cs            ← 修改（DefaultTools 加 canvas_upsert + inbox_create）
  ServiceCollectionExtensions.cs   ← 修改（注册两个新工具）
```

### 3.2 服务注册（ServiceCollectionExtensions.cs）

```csharp
var canvasRepo = sp.GetRequiredService<ICanvasArtifactRepository>();
toolRegistry.Register("canvas_upsert", _ => new CanvasUpsertTool(workspaceService, canvasRepo));

var inboxRepo = sp.GetRequiredService<IInboxRepository>();
toolRegistry.Register("inbox_create", _ => new InboxCreateTool(inboxRepo));
```

`ICanvasArtifactRepository` 由 `AddKodaClawStorage()` 注册，`IInboxRepository` 由 `AddKodaClawControlPlane()` 注册，两者均在 `GatewayApp.Composition.cs` 中先于 `AddKodaClawRuntime()` 调用，DI 延迟解析无顺序冲突。

### 3.3 AGENTS.md 模板更新

`DefaultWorkspaceTemplates` 中 AGENTS.md 内容补充三条工具引导：

```markdown
- Use workspace_protocol_update with target=heartbeat to add or modify scheduled automation rules during conversation.
- Use canvas_upsert to publish reports, task boards, or structured results the user can view in Canvas.
- Use inbox_create to proactively notify the user of findings or decisions that require their attention.
```

---

## 4. 非目标确认

- `canvas_upsert` 不校验 content 的格式合法性（markdown/html 均直接写入文件）
- `canvas_upsert` 不做文件去重校验，同 id 重复调用会覆盖文件内容
- `inbox_create` 不提供 update/resolve 工具（已有 Gateway API 供 UI 操作）
- heartbeat target 的 patch 语义与其他 target 完全相同（section-level upsert），不做特殊 YAML 校验

---

## 5. 验收命令（Verification）

```bash
# L0
dotnet build KodaClaw.sln

# L1
dotnet test tests/KodaClaw.UnitTests --filter "CanvasUpsert|InboxCreate|WorkspaceProtocol"

# L2
dotnet test tests/KodaClaw.IntegrationTests --filter "CanvasUpsert"

# L3
dotnet test tests/KodaClaw.ContractTests --filter "WorkspaceTemplate"

# 全量
dotnet test KodaClaw.sln -m:1
```

---

## 6. 分波计划

| Wave | 内容 | KC |
|------|------|----|
| Wave 0 | 本文档 + BACKLOG 条目 | — |
| Wave 1 | KC-1201：heartbeat target + 单元/contract 测试 | KC-1201 |
| Wave 2 | KC-1202：canvas_upsert 工具 + L1/L2 测试 | KC-1202 |
| Wave 3 | KC-1203：inbox_create 工具 + L1 测试 + AGENTS.md 更新 | KC-1203 |
