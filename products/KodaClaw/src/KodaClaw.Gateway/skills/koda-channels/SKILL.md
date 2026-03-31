---
name: koda-channels
description: 渠道消息发送指南——channel_send 工具、Telegram/飞书/微信格式差异、媒体附件、BindingId 获取
license: built-in
compatibility: KodaClaw 1.x
allowed-tools: channel_send channel_list
metadata:
  kind: builtin-core
  version: "1.1"
  tags: "channels, telegram, feishu, wechat, messaging"
---

# KodaClaw Channels — 渠道消息发送指南

## channel_send 工具

```
channel_send(
  bindingId="telegram-personal",
  content="你好，这是一条消息",
  mediaId="media-abc123"   // 可选，发送图片或音频附件
)
```

参数说明：
- `bindingId`（必填）：渠道账号标识符，从 ChannelsDesk 获取
- `content`（必填）：消息文本内容
- `mediaId`（可选）：媒体文件 ID，用于发送图片或音频附件

## BindingId 获取方式

1. 打开 ChannelsDesk（Settings → 渠道）
2. 找到目标渠道账号
3. 点击"复制 BindingId"按钮
4. 在工具调用中使用该 ID

BindingId 格式示例：`telegram-personal`、`feishu-work`、`wechat-account`

## 各平台格式差异

### Telegram

- 支持 Markdown（`**粗体**`、`_斜体_`、`\`代码\``、代码块）
- 消息长度上限：4096 字符（超出自动截断或分段发送）
- 支持发送图片（`mediaId` 参数）
- 支持发送音频文件（mp3，`sendAudio` 模式）
- 换行用 `\n`

```
channel_send(
  bindingId="telegram-personal",
  content="**今日报告**\n\n- 任务 A 完成\n- 任务 B 进行中"
)
```

### 飞书（Lark）

- 支持 Markdown，但语法略有不同（加粗用 `**text**`）
- 不支持斜体 markdown，建议使用纯文本
- 消息长度上限：约 4000 字符
- 支持图文混排（文本 + 图片分别发送）
- 支持发送音频文件（需 App 具有文件上传权限，失败时降级为文本提示）
- `@` 提及：不支持通过 channel_send 直接 @ 用户

```
channel_send(
  bindingId="feishu-work",
  content="**项目进展**\n完成了本周所有需求，代码审查通过。"
)
```

### 微信（个人号）

- **不支持 Markdown**，发送纯文本，所有格式标记会原样显示
- 消息长度上限：约 2000 字符
- 发送前自动将 Markdown 转换为纯文本
- 不支持发送图片附件（个人号 API 限制）
- **不支持发送音频文件**（需 AMR 格式，当前不支持）

```
channel_send(
  bindingId="wechat-account",
  content="今日工作总结：完成了三个功能模块，修复了两个 bug。明天继续推进剩余需求。"
)
```

## delivery-mode 对渠道推送的影响

自动化任务（`HEARTBEAT.md`）中，渠道推送受 `delivery-mode` 控制：

| delivery-mode | 行为 |
|--------------|------|
| `none` | 不推送任何渠道 |
| `auto` | 执行完成后自动推送到 `channels` 列表 |
| `approval` | Inbox 审批通过后手动触发推送 |

在对话中（非自动化），直接调用 `channel_send` 不受此设置影响，立即发送。

## 媒体附件发送

发送图片需要先有 `mediaId`（用户上传图片后系统返回），然后通过 `mediaId` 参数传入 `channel_send`。

**注意**：微信个人号不支持图片发送，`mediaId` 参数在微信渠道会被忽略。

## 最佳实践

- **针对平台调整格式**：给 Telegram 用 Markdown，给微信发纯文本
- **控制消息长度**：超长内容优先使用 Canvas 保存，再推送摘要 + 链接说明
- **批量发送**：多个渠道需分别调用 `channel_send`，每个 bindingId 一次调用
