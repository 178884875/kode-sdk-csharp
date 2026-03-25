---
name: koda-automation
description: HEARTBEAT.md 自动化规则编写指南——cron 调度语法、Markdown 结构、渠道推送、delivery-mode 配置
license: built-in
compatibility: KodaClaw 1.x
allowed-tools: workspace_protocol_update workspace_read
metadata:
  kind: builtin-core
  version: "2.0"
  tags: "automation, heartbeat, scheduling, cron"
---

# KodaClaw Automation — HEARTBEAT.md Guide

## HEARTBEAT.md 格式

`HEARTBEAT.md` 是 KodaClaw 的自动化规则文件，每条规则是一个 Markdown `##` 章节，字段为顶级项目符号：

```markdown
## 每日晨报
- cron: "0 9 * * 1-5"
- prompt: 基于 MEMORY.md 和 USER.md，生成今天的工作重点和提醒事项。
- enabled: true
- channels:
  - telegram-personal
- delivery-mode: auto
```

**规则**：
- 每个 `## 标题` 定义一条自动化规则，ID 由标题自动生成（slug）
- `- cron:` 和 `- prompt:` 是必填字段
- 其余字段可选

## Cron 表达式格式

`cron:` 字段使用标准 5 段表达式（UTC 时间）：

```
分钟(0-59)  小时(0-23)  日期(1-31)  月份(1-12)  星期(0-7, 0/7=周日)
```

常用模板：

| 表达式 | 含义 |
|--------|------|
| `0 9 * * *` | 每天 09:00 UTC |
| `0 9 * * 1-5` | 工作日 09:00 UTC |
| `30 18 * * 5` | 每周五 18:30 UTC |
| `0 1 * * *` | 每天 01:00 UTC（北京时间 09:00）|
| `*/15 * * * *` | 每 15 分钟 |
| `0 */2 * * *` | 每 2 小时整点 |

> **注意**：cron 表达式使用 UTC 时间。北京（UTC+8）09:00 对应 UTC 01:00。

## 兼容旧格式

`- schedule:` 字段（旧版语法）仍支持，自动转换为 cron：

| 旧语法 | 等效 cron |
|--------|-----------|
| `schedule: every 15m` | `*/15 * * * *` |
| `schedule: hourly 2h` | `0 */2 * * *` |
| `schedule: daily 09:00` | `0 9 * * *` |
| `schedule: weekdays 09:00` | `0 9 * * 1-5` |
| `schedule: weekly mon,wed,fri 18:30` | `30 18 * * 1,3,5` |

建议新规则使用 `- cron:` 格式，`- schedule:` 已废弃。

## 多行 Prompt（块标量）

使用 `>` 写多行 prompt：

```markdown
## 每周总结
- cron: "0 18 * * 5"
- prompt: >
    回顾本周对话和记忆，生成工作总结。
    包含：完成事项、未完成事项、下周计划。
- enabled: true
```

## delivery-mode 说明

| 模式 | 行为 |
|------|------|
| `none` | 仅执行，不推送（默认） |
| `auto` | 执行完成后自动推送到 `channels` 列表 |
| `approval` | 执行完成后进 Inbox 等待人工审批，审批后推送 |

**重要**：`delivery-mode: auto` 仅适合"生成型" prompt。如果 prompt 已指示 Agent 调用 `channel_send` 发送消息，请使用 `delivery-mode: none`，否则会重复发送。

## channels 推送配置

```markdown
- channels:
  - telegram-personal
  - feishu-work
```

BindingId 在 ChannelsDesk → 渠道账号 → 复制 BindingId 获取。

## 写入规则

通过 `workspace_protocol_update` 写入 HEARTBEAT.md，立即热更新：

```
workspace_protocol_update(
  target="heartbeat",
  section="每日晨报",
  content="- cron: \"0 9 * * 1-5\"\n- prompt: 生成今天的工作计划。\n- enabled: true"
)
```

## 完整示例

```markdown
## 每日晨报
- cron: "0 1 * * 1-5"
- prompt: 基于 MEMORY.md 和 USER.md，生成今天的工作重点和提醒事项。
- enabled: true
- channels:
  - telegram-personal
- delivery-mode: auto

## 每周总结
- cron: "0 10 * * 5"
- prompt: >
    回顾本周对话和记忆，生成工作总结。
    包含：完成事项、未完成事项、下周计划。
- enabled: true
- channels:
  - feishu-work
- delivery-mode: approval

## 定时提醒
- cron: "0 2,6,8 * * 1-5"
- prompt: 提醒用户喝水，保持健康。
- enabled: true
- channels:
  - telegram-personal
- delivery-mode: auto
```
