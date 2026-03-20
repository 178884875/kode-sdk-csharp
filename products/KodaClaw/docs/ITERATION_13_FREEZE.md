# Iteration 13 Freeze — Inbox 读取、Bootstrap 写回、Inbox 未读徽标

冻结日期：2026-03-20
前置迭代：Iteration 12（canvas_upsert、inbox_create、heartbeat target）

---

## 1. 一句话目标

闭合 Iter 12 打通的三条输出链路的最后缺口：让 Agent 能**读取** Inbox（解锁 Daily Inbox Digest 自动化）；让 Bootstrap 对话在结束后**写回** workspace 身份文件（首次上手体验闭环）；让前端 GlobalRail 展示 Inbox **未读徽标**（主动通知触达用户）。

---

## 2. 范围冻结

### 2.1 纳入（In Scope）

**KC-1301 — `inbox_read` 工具**

新增 `InboxReadTool`，注册为 `inbox_read`，加入 `DefaultTools`。

- 查询参数：`status`（open/resolved/archived，可选）、`limit`（默认 20，最大 50）
- 返回：InboxItem 数组（id、title、summary、kind、status、requiresAction、createdAt）
- 依赖：`IInboxRepository.ListAsync()`（已有）

Agent 在自动化 session（Daily Inbox Digest）和主会话中均可调用，读取当前 Inbox 状态，再通过 `canvas_upsert` 或 `inbox_create` 输出结果。

---

**KC-1302 — Bootstrap 模板补写回指令**

更新 `DefaultWorkspaceTemplates.Bootstrap()` 和 `DefaultWorkspaceTemplates.Agents()`：

1. `Bootstrap()` 末尾补充指令：对话结束时，调用 `workspace_protocol_update` 分别用 `target=identity`、`target=soul`、`target=user` 把收集到的信息写入对应文件。
2. `Agents()` 中为 `workspace_protocol_update` 补充 identity/soul/user 三个 target 的使用时机说明。

效果：首次启动对话后，Koda 的人格文件不再是空模板。

---

**KC-1303 — GlobalRail Inbox 未读徽标**

前端：
- 新增 `useInboxUnreadCount` hook，每 30 秒轮询 `GET /api/inbox?status=open&limit=1&countOnly=true`（或使用现有 `fetchInbox` API，取 `total` 字段）。
- 在 `GlobalRail` 的 Inbox 导航项旁显示数字徽标（> 0 时显示，> 99 显示 "99+"）。
- 数字取自 `InboxPagedResult.total`（后端已返回）。

后端（如缺少 `total` 字段）：确认或补齐 `GET /api/inbox` 响应的 `total` 字段。

---

### 2.2 排除（Out of Scope）

- `canvas_read` 工具（推到 Iter 14）
- Inbox 已读/归档状态更新工具（推到 Iter 14 或控制面板专项）
- Bootstrap 对话 UI 改版（属于 KC-W2 前端专项）
- GlobalRail 其他模块徽标（Automations 运行状态等）

---

## 3. 架构决策

### 3.1 `inbox_read` 工具参数设计
- `status` 默认为 `open`（最常用场景：摘要待处理事项）
- `limit` 上限 50，避免 prompt 过长
- 返回轻量摘要字段，不含 `metadataJson`

### 3.2 Bootstrap 写回时机
- 在 Bootstrap 对话**结束时**（用户说"确认"或 Agent 判断信息收集完毕）
- 分三次调用 `workspace_protocol_update`（identity/soul/user）
- 不覆盖用户手动改过的文件（section-patch 语义不会删除其他 section）

### 3.3 Inbox 未读数轮询
- 轮询间隔 30 秒（不需要 SSE，频率足够低）
- 仅在 Gateway 健康时轮询（复用 healthStatus 信号）
- badge 数值取 `open` 状态的 Inbox 条目总数

---

## 4. 非目标

- 不引入 WebSocket / SSE 推送来替代轮询
- 不重构 Inbox 数据模型
- 不修改 Bootstrap 对话的 UI 流程（只改模板文字）

---

## 5. 验证命令

```bash
# L0 — 编译
dotnet build KodaClaw.sln

# L1 — 单元测试
dotnet test tests/KodaClaw.UnitTests --filter "InboxRead|Bootstrap|WorkspaceTemplate"

# L3 — 契约测试
dotnet test tests/KodaClaw.ContractTests --filter "WorkspaceTemplate|Inbox"

# 全量后端
dotnet test KodaClaw.sln -m:1

# 前端
cd apps/kodaclaw-web && npm run typecheck && npm test
```

---

## 6. Wave 计划

| Wave | 内容 |
|------|------|
| Wave 1 | KC-1301 `inbox_read` 工具（后端） + L1/L3 测试 |
| Wave 2 | KC-1302 Bootstrap/AGENTS.md 模板更新 + L3 contract 测试 |
| Wave 3 | KC-1303 GlobalRail Inbox 徽标（前端） + 前端类型检查 |
