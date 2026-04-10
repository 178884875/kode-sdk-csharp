---
name: koda-workspace
description: KodaClaw workspace 协议指南——workspace 文件布局、工具使用模式、各类 session 上下文差异
license: built-in
compatibility: KodaClaw 1.x
allowed-tools: workspace_protocol_update workspace_memory_append workspace_read diagnostics_query schedule_reminder config_update
metadata:
  kind: builtin-core
  version: "1.1"
  tags: "workspace, protocol, memory, identity"
---

# KodaClaw Workspace 协议

## Workspace 文件布局

KodaClaw 将所有状态以普通文件形式存储在 `workspace/`（相对于 workspace 根目录）：

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

### schedule_reminder
安排一次性定时提醒——在未来某个特定时刻触发一次 Agent 会话执行给定 prompt。

**何时使用（vs heartbeat）：**
- 用 `schedule_reminder`：明确的单次时间点，如"明天下午三点提醒我"、"2026-04-01 检查服务器"
- 用 `heartbeat`（写入 workspace）：周期性重复任务，如"每天 9 点"、"每周五下午"

**参数：**
- `prompt`（必填）：到时触发的 Agent 指令，应当自洽、不依赖当前对话上下文
- `fireAt`（必填）：ISO 8601 格式，必须包含时区偏移，如 `2026-04-01T15:00:00+08:00`
- `title`（可选）：简短标题，显示在 Inbox 条目中
- `channels`（可选）：结果推送的渠道 BindingId 列表，如 `["tg-12345"]`

```
schedule_reminder(
  prompt="检查 CI 流水线是否全绿，如有失败写入 Inbox",
  fireAt="2026-04-01T10:00:00+08:00",
  title="CI 健康检查"
)
```

**注意：**
- `fireAt` 必须是未来时间，不接受过去时间
- `prompt` 要写得完整——执行时没有当前对话上下文
- 工具返回 `{ timerId, fireAt }`，可告知用户已安排

### diagnostics_query
查询系统近期诊断事件，用于 Agent **主动自诊断**。返回字段：`ts`、`level`、`source`、`eventType`、`message`、`correlationId`、`sessionId`（不含 attributes，避免敏感数据进入上下文）。

**何时主动调用：**
- 用户反映"刚才好像出错了"或"自动化没有按时执行"时
- 自动化任务完成后发现结果异常，需要排查原因
- 用户询问"最近有什么错误/警告"时

**参数使用策略：**
- 默认不传参数，查询最近 60 分钟内最多 20 条事件
- 已知出错大致时间段时，用 `sinceMinutes` 缩小窗口（最大 1440 分钟）
- 只关注严重问题时，传 `level="error"` 过滤
- 追查某次自动化运行或特定请求时，传 `correlationId` 精确定位

**与 correlationId 联动（全链路追踪）：**

先从宽泛查询中找到可疑事件的 correlationId，再用它过滤出完整链路：

```
# Step 1：查询近期错误
diagnostics_query(level="error", sinceMinutes=30)

# Step 2：锁定某次运行的完整链路
diagnostics_query(correlationId="abc123...", sinceMinutes=60, limit=50)
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
- 用户要求**周期性**定时任务（"每天早上 9 点..."）时，写入 `heartbeat`。
- 用户要求**一次性**提醒（"明天下午三点..."、"X 月 X 日..."）时，调用 `schedule_reminder`，不写 heartbeat。
- 避免写入临时对话细节——只写在下次 session 中有实际价值的内容。

## 技能自安装

为未来 session 添加领域知识：

1. 写入 SKILL.md：调用 `fs_write` 工具，路径填 `workspace/skills/<name>/SKILL.md`
2. 验证发现：调用 `skill_list` 工具确认新技能已出现
3. 在当前 session 激活：调用 `skill_activate` 工具，传入技能名称
