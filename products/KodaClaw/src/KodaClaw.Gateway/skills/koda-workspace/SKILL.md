---
name: koda-workspace
description: KodaClaw workspace 协议指南——workspace 文件布局、工具使用模式、各类 session 上下文差异
license: built-in
compatibility: KodaClaw 1.x
allowed-tools: workspace_protocol_update workspace_memory_append workspace_read
metadata:
  kind: builtin-core
  version: "1.1"
  tags: "workspace, protocol, memory, identity"
---

# KodaClaw Workspace 协议

## Workspace 文件布局

KodaClaw 将所有状态以普通文件形式存储在 `~/.kodaclaw/workspace/`：

| 文件 | 用途 |
|------|------|
| `IDENTITY.md` | Agent 名称、角色与人格定义 |
| `SOUL.md` | 核心行为原则与边界 |
| `USER.md` | 用户画像、偏好和工作方式 |
| `MEMORY.md` | 长期记忆索引（指针，非完整内容）|
| `HEARTBEAT.md` | 定时自动化规则（Markdown bullet-list 格式，`## 标题` + `- cron:` 字段）|
| `AGENTS.md` | Session 规则、工具指导、workspace 惯例 |

## 工具使用模式

### workspace_protocol_update
用于更新 workspace 文件中的结构化 section。可用 target：
- `identity` — 更新名称、角色、人格特征
- `soul` — 更新行为原则和边界
- `user` — 更新对话中学到的用户画像信息
- `memory` — 添加新的记忆条目
- `agents` — 添加或更新 AGENTS.md 的 section
- `heartbeat` — 添加或修改定时自动化规则

```
workspace_protocol_update(target="user", section="偏好", content="- 偏好简洁回答\n- 在 CST 时区工作")
```

### workspace_memory_append
向 MEMORY.md 快速追加一条事实或备注，无需指定 section 标题。

### workspace_read
在 session 中读取任意 workspace 文件内容。

```
workspace_read(path="workspace/MEMORY.md")
```

## 各类 Session 上下文差异

| Session 类型 | 加载的上下文 | 技能可用 |
|-------------|------------|---------|
| 主会话（对话）| IDENTITY, SOUL, USER, MEMORY, HEARTBEAT, AGENTS | 是 |
| 渠道私聊（DM）| AGENTS, IDENTITY, SOUL, USER, MEMORY, 会话摘要 | 是 |
| 渠道群聊 | AGENTS, IDENTITY, SOUL（默认不加载 USER）| 是 |
| 自动化 | AGENTS, IDENTITY, SOUL, USER, HEARTBEAT | 是 |

## 记忆写入策略

- 学到用户稳定偏好或背景信息后，写入 `user`。
- 重要事件、决策或需要未来引用的内容，写入 `memory`。
- 用户要求定时任务（"每天早上 9 点..."）时，写入 `heartbeat`。
- 避免写入临时对话细节——只写在下次 session 中有实际价值的内容。

## 技能自安装

为未来 session 添加领域知识：

1. 写入 SKILL.md：调用 `fs_write` 工具，路径填 `~/.kodaclaw/workspace/skills/<name>/SKILL.md`
2. 验证发现：调用 `skill_list` 工具确认新技能已出现
3. 在当前 session 激活：调用 `skill_activate` 工具，传入技能名称
