---
name: koda-memory
description: 记忆管理指南——MEMORY.md 索引格式、workspace_memory_append 用法、结构化写入、记忆归档与 USER.md 的区别
license: built-in
compatibility: KodaClaw 1.x
allowed-tools: workspace_memory_append workspace_protocol_update workspace_read
metadata:
  kind: builtin-core
  version: "1.0"
  tags: "memory, workspace, context, persistence"
---

# KodaClaw Memory — 记忆管理指南

## MEMORY.md 是什么

`MEMORY.md` 是长期记忆的**索引文件**，记录指针和摘要，而非完整内容。它的作用是让 Agent 在新 session 启动时快速了解重要历史，而不是把所有内容都塞入 context。

```markdown
# Memory Index

## 2026-03-25 项目决策
- 选择 React + Vite 作为前端框架（详见对话 #session-abc）
- API 设计采用 REST 而非 GraphQL

## 用户信息更新
- 用户升职为技术总监，负责 3 个团队
- 偏好简洁、无废话的沟通风格

## 待跟进事项
- [ ] Iteration 52 发布后联系产品团队
```

## workspace_memory_append 用法

用于快速追加一条记忆条目，不需要指定 section：

```
workspace_memory_append(
  content="2026-03-25：用户决定暂停 Feature B 开发，等待市场调研结果。"
)
```

适合：
- 对话中的重要决策
- 需要下次 session 记住的事项
- 值得追踪的用户状态变化

## workspace_protocol_update 结构化写入

需要更新特定 section 时，使用 `workspace_protocol_update`（target="memory"）：

```
workspace_protocol_update(
  target="memory",
  section="2026-03 项目决策",
  content="""
- 选择 PostgreSQL 作为主数据库
- 采用 CQRS 架构模式
- 决定在 Q2 重构认证模块
"""
)
```

这种方式会替换对应 section 的内容，适合维护结构化的知识块。

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
- 会在短时间内过期的时效性信息

## MEMORY.md 与 USER.md 的区别

| 文件 | 用途 | 内容类型 |
|------|------|---------|
| `USER.md` | 用户画像和稳定偏好 | 角色、工作方式、长期偏好（不常变化）|
| `MEMORY.md` | 事件日志和知识索引 | 发生的事、做过的决定、待跟进项（随时间积累）|

**示例**：
- "用户偏好简洁回答" → `USER.md`（稳定偏好）
- "2026-03-25 用户批准了 Q2 预算" → `MEMORY.md`（事件记录）

## 旧记忆归档

当 MEMORY.md 积累了大量内容时，可将旧条目整理归档：

```
workspace_protocol_update(
  target="memory",
  section="Archive — 2026 Q1",
  content="""
（将 2026 Q1 的重要事项汇总到此 section）
- 完成了 KodaClaw 迭代 1-30
- 确立了 local-first 架构原则
- 引入了 LibGit2Sharp 做 workspace 版本管理
"""
)
```

归档后删除原有的旧 section，保持 MEMORY.md 精简。

## 读取记忆

```
workspace_read(path="workspace/MEMORY.md")
```

在需要引用历史决策或核实记忆时调用，避免依赖 session context 中可能过期的信息。
