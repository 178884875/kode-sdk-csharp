---
name: koda-memory
description: 记忆管理指南——三层记忆架构（热/温/冷）、MEMORY.md 索引、topics 主题索引、fs_grep 搜索、session 摘要、Agent 语义降级、Auto Dream 整合
license: built-in
compatibility: KodaClaw 1.x
allowed-tools: workspace_memory_append workspace_protocol_update workspace_read fs_grep fs_glob
metadata:
  kind: builtin-core
  version: "3.1"
  tags: "memory, workspace, context, persistence, search, topics"
---

# KodaClaw Memory — 记忆管理指南 v3.1

## 三层记忆架构

KodaClaw 采用热-温-冷三层记忆架构，由 Nightly Agent 语义判断管理记忆生命周期：

| 层级 | 存储位置 | 说明 |
|------|---------|------|
| 热（Hot） | `workspace/MEMORY.md` | 活跃记忆，每次 session 启动载入 system prompt |
| 温（Warm） | `workspace/memory/dormant/` | 一段时间未访问的记忆，不再自动加载但可搜索 |
| 冷（Cold） | `workspace/memory/archive/` | 长期归档，仅通过手动检索访问 |

记忆优先级（供 Nightly Agent 参考，非硬编码阈值）：
- **permanent**：核心身份/价值观 — 永不降级
- **lasting**：重要决策/长期项目 — 仅当明显过时或被推翻时降级
- **standard**（默认）：一般事件记录 — 30+ 天无关联可降级
- **ephemeral**：临时记录 — 7 天无价值即可降级

降级由 Nightly Consolidation Agent 基于语义判断执行，而非时间阈值公式。

## MEMORY.md 索引

`MEMORY.md` 是长期记忆的**索引文件**，记录指针和摘要。Agent 在新 session 启动时自动读取。

```markdown
# Memory Index

## 2026-03-25 项目决策
- 选择 React + Vite 作为前端框架 → sessions/2026-03-25-main-abc123.md
- API 设计采用 REST 而非 GraphQL → sessions/2026-03-24-main-def456.md

## 用户信息更新
- 用户升职为技术总监，负责 3 个团队

## 待跟进事项
- [ ] Iteration 52 发布后联系产品团队
```

每条记忆应附来源链接（`→ sessions/{file}` 或 `→ daily/{date}`），便于溯源。

## Topics 主题索引

`workspace/memory/topics/` 存放按主题聚合的会话时间线。Topics 主要由 Nightly Consolidation 自动维护，日常对话中也可直接创建/更新。

```
workspace/memory/topics/
  frontend-architecture.md
  team-management.md
  product-roadmap.md
```

每个 topic 文件格式：

```markdown
# 前端架构

## 概述
团队前端技术选型和架构演进记录。

## 相关会话
- 2026-03-25：决定采用 React + Vite（来源：sessions/2026-03-25-main-abc123.md）
- 2026-03-20：评估 Next.js vs Vite（来源：sessions/2026-03-20-main-xyz789.md）

## 当前状态
已确定 React + Vite 方案，正在搭建脚手架。
```

查阅 topics：

```
workspace_read(target="topics")                    # 列出所有 topic 文件
workspace_read(target="topics", path="frontend-architecture")  # 读取指定 topic
```

## 搜索记忆

使用 `fs_grep` 按关键词搜索，`fs_glob` 按文件名/目录发现：

```
# 关键词搜索（内容匹配）
fs_grep(pattern="React 框架", path="workspace/")
fs_grep(pattern="项目决策", path="workspace/memory/")

# 目录发现（列出文件）
fs_glob(pattern="workspace/memory/dormant/*.md")   # 列出所有温记忆
fs_glob(pattern="workspace/memory/archive/*.md")   # 列出所有冷记忆
fs_glob(pattern="workspace/memory/sessions/*.md")  # 列出所有会话摘要
fs_glob(pattern="workspace/memory/topics/*.md")    # 列出所有主题索引
```

搜索范围覆盖 MEMORY.md、dormant/、archive/、topics/、sessions/ 所有 markdown 文件。

## 写入记忆

### workspace_memory_append — 日常对话的唯一写入路径

```
workspace_memory_append(
  content="2026-03-25：用户决定暂停 Feature B 开发，等待市场调研结果。",
  priority="lasting"
)
```

日常对话中记录记忆**只用这个工具**。支持 priority 参数：

- `permanent`：用户核心身份、长期不变的偏好
- `lasting`：重要决策、项目背景
- `standard`（默认）：一般事件、讨论记录
- `ephemeral`：临时信息、短期有效的内容

### workspace_protocol_update — Nightly 整合专用

```
workspace_protocol_update(
  target="memory",
  section="2026-03 项目决策",
  content="""
- 选择 PostgreSQL 作为主数据库
- 采用 CQRS 架构模式
"""
)
```

⚠️ 此工具用于 **Nightly Consolidation Agent 整合时**重写 MEMORY.md 的 section。日常对话中不应使用此工具写记忆。

## Session 摘要

Session 轮转时，系统自动生成结构化摘要并存入 `workspace/memory/sessions/`。摘要包含：
- 讨论主题和关键词
- 做出的重要决策
- 用户原话中的关键观点
- 待跟进事项

自动摘要条件：至少 5 条用户消息的对话型 session（automation 类型跳过）。

摘要中不包含密码、API Key、token 等敏感信息。用户在对话中说"不用记"/"别记这个"的内容不纳入摘要。

## Auto Dream 整合

每晚 Nightly Consolidation（HEARTBEAT 自动触发）执行五阶段整合：

1. **采集**：读取今日日志 + 未处理的 session 摘要 + 当前 MEMORY.md
2. **整合**：合并重复、解决矛盾、附来源链接，写入 MEMORY.md（≤200 行）
3. **Topics 维护**：为反复出现的主题创建/更新 topic 文件（≤20 个）
4. **清理**：删除已处理的日志，标记已整合的摘要
5. **记忆时效审查**：Agent 扫描 MEMORY.md 和 topics/，根据语义判断降级（P0 永不降级，P3 门槛最低），移动到 dormant/archive 并更新 frontmatter

整合完成后，系统自动 git commit 所有 workspace 变更。

## 何时写记忆

**应该写记忆**：
- 用户做出重要决策（技术选型、优先级调整）
- 用户分享关键信息（职位变化、项目背景、个人偏好）
- 需要在未来 session 中引用的事件
- 待跟进的承诺或计划

**不应该写记忆**：
- 当前对话的临时中间状态
- 可以从代码或文档实时获取的信息
- 不影响未来行为的闲聊内容
- 会在短时间内过期的时效性信息（使用 ephemeral 优先级）

## MEMORY.md 与 USER.md 的区别

| 文件 | 用途 | 内容类型 |
|------|------|---------|
| `USER.md` | 用户画像和稳定偏好 | 角色、工作方式、长期偏好（不常变化）|
| `MEMORY.md` | 事件日志和知识索引 | 发生的事、做过的决定、待跟进项（随时间积累）|

## 读取记忆

```
workspace_read(target="memory")           # 读取完整 MEMORY.md
workspace_read(target="daily_memory")     # 读取今日记忆日志
workspace_read(target="topics")           # 列出所有 topic 文件
workspace_read(target="topics", path="topic-name")  # 读取指定 topic
fs_grep(pattern="关键词", path="workspace/memory/")  # 按内容搜索记忆
fs_glob(pattern="workspace/memory/dormant/*.md")     # 按目录列出记忆文件
```
