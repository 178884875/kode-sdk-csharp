# Iteration 39 Freeze — Chat UX：会话清空 + 内联审批

冻结时间：2026-03-23

## 背景

两个独立问题，本迭代一起解决：

1. **会话切换无反馈**：点击"新对话"或从历史面板恢复会话后，Chat 消息列表不清空，用户无法感知切换已发生。
2. **审批流割裂**：Agent 执行需要授权的工具时，只能通过"收件箱"轮询感知——UI 割裂、不实时、步骤繁琐。用户必须离开对话界面、导航到 Inbox Desk 才能完成一次审批。

## 目标

### 会话切换
- 点击 Sidebar "新对话" 按钮后，消息列表清空，显示系统提示"已开始新对话"
- 从 SessionHistoryPanel 点击"恢复"后，消息列表清空，显示系统提示"已切换到历史会话，Agent 记得之前的上下文"

### 内联审批
- Agent 触发权限审批时，Chat 消息流中**立即出现**审批卡片（无需跳转 Inbox）
- 审批卡片展示：工具名称、输入参数预览、允许/拒绝按钮
- 用户在聊天界面完成审批后，Agent 继续执行，卡片更新为已批准/已拒绝状态
- 收件箱作为备份入口保留，不删除

### 全局工具自动授权
- `KodaClawSettings` 新增 `AutoApproveToolCalls: bool`（默认 false）
- SettingsDesk → BehaviorSection 新增对应 toggle，label："工具调用自动授权"
- `MainSessionService` 创建/恢复 session 时读取该设置，映射到 Agent `PermissionConfig.Mode`：
  - `true` → `PermissionMode = "auto"`（跳过审批，无内联卡片）
  - `false` → `PermissionMode = "approval"`（走内联审批卡片）
- 注意：与现有 `RequireApprovalForExternalActions`（控制渠道消息投递审批）语义不同，不合并

## 非目标（本迭代不做）

- Session 级"信任模式"开关（需要 session rotation，打断对话，延后到 ApprovalCard "本次不再询问"复选框实现）
- 历史消息列表持久化恢复（恢复后仍从空白开始）
- 工具粒度的"记住我的选择"（浏览器权限模型）
- 对 ChannelSession 审批流的改动

## 技术契约

### 新增 SSE 事件类型

```
event: approval_required
data: {
  type: "approval_required",
  approvalId: string,
  callId: string,
  toolName: string,
  inputPreview: string,   // 参数 JSON 截断预览，max 400 chars
  sessionId: string,
  timestamp: number
}

event: approval_decided
data: {
  type: "approval_decided",
  approvalId: string,
  callId: string,
  decision: "approved" | "rejected",
  sessionId: string,
  timestamp: number
}
```

### IMainSessionService 新增方法

```csharp
// 供 ChatSessionService 将 callId 解析为 approvalId
string? TryGetApprovalIdForCall(string callId);
```

### 前端新增 ChatMessage role

```typescript
// messages 新增 role
role: "approval"           // 审批卡片，status: "pending" | "approved" | "rejected"
```

## 验证矩阵

| 层级 | 内容 | 通过标准 |
|-----|------|---------|
| L0 | `dotnet build KodaClaw.sln` + `npm run typecheck` | 0 错 0 警告 |
| L1 | `dotnet test --filter "ApprovalSse"` | 新增单元测试全绿 |
| L2 | `dotnet test tests/KodaClaw.IntegrationTests --filter "InlineApproval"` | 集成测试覆盖 SSE 序列 |
| L3 | 契约测试：approval_required / approval_decided 事件 JSON schema | snapshot 通过 |
| L5 | Dogfood：触发需审批工具 → 卡片出现在对话流 → 点允许 → Agent 继续 | 人工走通全流程 |
| L5 | Dogfood：Settings → 开启"工具调用自动授权" → 新建对话 → Agent 执行工具无卡片弹出 | 人工验证 |
