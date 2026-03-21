# Iteration 18 FREEZE：Channel Full Agent Mode + 主动推送

## 冻结日期
2026-03-21

## 动机

当前所有 channel session（DM + Group）使用 JSON-only 协议，Agent 被强制只输出
`{ action, replyText, reason, confidence }` 结构，导致：
- 工具虽已注册却永远无法被调用（JSON 约束阻断 tool_use 生成）
- 模型只能"描述"要做什么，无法真正执行（如 "让我查看一下目录" 但不调用 fs_list）
- 渠道体验远弱于主会话，与 "渠道是独立会话入口" 的产品定位不符
- 无主动推送能力（Automation 结果只进 Inbox，无法直接发到用户的 Telegram）

## 用户可感知的变化

- Telegram DM 发消息后，Koda 可真正使用工具（读文件、执行命令、查 Inbox 等）并分步回复
- Telegram Group @Koda 后，Koda 也以 Full Agent 模式响应
- Automation 任务完成后，可通过 `channel_send` 主动向已绑定渠道发送结果
- Main session 也可通过 `channel_send` 向已绑定渠道发消息

## 范围

### 在范围内

| KC | 描述 |
|----|------|
| KC-1801 | 新增 `channel_send` 工具 |
| KC-1802 | 新增 `channel_list` 工具 |
| KC-1803 | Channel session 切换到 Full Agent 模式（DM + Group） |

### 不在范围内

- Web UI 变更（渠道 thread 视图仍保持现有 UI）
- channel_send 的 DraftApproval 治理（始终 AutoSend）
- GenericWebhook 出站支持（保持 NotSupportedException）
- 渠道消息的 streaming SSE 透出（仍然是 best-effort 异步发送）
- 多 connector 并发场景的 binding 冲突解析

## 关键设计决定

| 问题 | 决定 | 理由 |
|------|------|------|
| channel_send delivery 治理 | 始终 AutoSend | DM 信任级高，简化初期实现 |
| Group 是否也 Full Agent | 是，保留 RequireExplicitMention 约束 | 一致的运行时模型 |
| Automation 定位 binding | 显式 bindingId 参数 | 避免模糊匹配；channel_list 工具提供发现能力 |
| bindingId 如何传给 channel_send | 系统提示中已有 BindingId 字段，Agent 从 context 读取后传参 | 无需 per-session 工具注册 |

## 契约变更

### 新增工具（无破坏性变更）

`channel_send`：所有 session 可用
```
bindingId: string (required)
text:      string (required)
→ { ok: bool, bindingId: string, sentAt: string }
```

`channel_list`：automation + main session 可用
```
connectorKind?: string (optional filter)
→ [{ bindingId, displayTitle, connectorKind, threadType, lastInboundAt }]
```

### ChannelTurnOrchestrator 行为变更

旧流程：`RunInboundTurnAsync → ParseProposal(JSON) → Dispatch/DraftApproval`
新流程：`RunInboundTurnAsync → agent 内部调用 channel_send → 记录 Delivered outcome`

`ChannelTurnOutcomeKind.DraftCreated` / `ApprovalRequested` 不再产生（保留枚举值向后兼容）

## 验收标准

1. 从 Telegram 发 "帮我看看工作目录" → Koda 实际调用 fs_list 并回复文件列表
2. Automation 执行完成后，agent 调用 channel_send 将结果发到 Telegram
3. 已有审批流（现有 pending approvals）不受影响
4. Group @Koda 后能以 Full Agent 模式响应
5. `dotnet test KodaClaw.sln -m:1` 全量通过
