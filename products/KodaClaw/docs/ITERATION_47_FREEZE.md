# Iteration 47 FREEZE — Chat 会话三项 Bug 修复

**日期**：2026-03-24
**类型**：Bug Fix
**范围**：仅前端 `apps/kodaclaw-web`，无后端变更

---

## 问题清单

### KC-BUG-301：历史会话恢复后消息不加载

**症状**：用户在 SessionHistoryPanel 点击"恢复"，清空消息后，历史消息永远不会加载进来，只剩"已恢复会话"系统提示。

**根因**：
1. `useGatewaySnapshot` 只在 mount 时拉取一次，无轮询，`snapshot.activeMainSessionId` 永远不更新。
2. `App.tsx` 的 `loadHistory` 依赖 `activeMainSessionId` 变化触发，因 snapshot 不更新，条件永远不满足。
3. `onResumed` 回调无参数，丢失了 sessionId，无法直接调用 `loadHistory(sessionId)`。

**修复方案**：
- `onResumed` 回调链改为带 `sessionId: string` 参数，贯穿 `useSessionHistory` → `SessionHistoryPanel` → `App.tsx`
- `handleResumeSession(sessionId)` 中：`clearMessages()` 后直接 `loadHistory(sessionId)`，不依赖 snapshot
- resume 完成后主动调一次 `refresh()`（snapshot），确保 `activeMainSessionId` 同步更新

---

### KC-BUG-302：SSE 流进行中恢复/轮转会话导致幽灵消息

**症状**：Koda 正在流式输出时，用户切换到其他会话（resume 或 rotate），消息列表清空后，旧 SSE 流的 `tool_activity` / `approval_required` 事件继续追加消息，造成"幽灵消息"出现在新上下文里。

**根因**：`sendMessage` 中 `for await (const event of streamChatEvents(...))` 未传 `AbortSignal`，无法从外部中断；`clearMessages` 不通知正在进行的流。

**修复方案**：
- `useChatConsole` 添加 `streamAbortRef`（`useRef<AbortController | null>`）
- `sendMessage` 每次创建新 `AbortController`，作为 signal 传入 `streamChatEvents`
- `clearMessages` 中先 abort 当前流（`streamAbortRef.current?.abort()`），再重置 messages
- `sendMessage` 的 catch 块过滤掉 `AbortError`（正常中断，不写错误消息）
- **恢复时若流正在进行**：SessionHistoryPanel "恢复" 按钮先弹 confirm `"Koda 正在回复中，确认中断并恢复此会话？"` + `[确认] [取消]`，确认后再调 `resume()`
- confirm 弹窗使用内联 confirm（避免 `window.confirm`），在 SessionHistoryPanel 内维护 `pendingResumeSessionId` 状态

---

### KC-BUG-303：切换模型对当前 session 无效（Option A2）

**症状**：用户在 composer model pill 切换模型，前端 pill 更新，但 Gateway 内 `_agents` 缓存命中，后端继续用旧模型回复，前后端显示不一致。

**根因**：`MainSessionService.LoadOrCreateMainSessionAsync` 在 `_agents` 命中时直接返回缓存 agent，不重新解析模型；`setDefaultModelEndpoint` 只改 DB，不重置 session。

**修复方案（前端，Option A2）**：
- `handleModelChange` 切换成功后弹 confirm 对话框：`"切换模型将开始新会话，当前上下文会保存在历史记录中。继续？"` + `[确认] [取消]`
- 用户确认后：`rotateSession()` + `clearMessages("已切换为 [model]，已开始新会话")`
- 用户取消后：回滚前端 pill 到原来的 model（`setModelName` / `setSelectedModelId` 恢复旧值）
- confirm 弹窗复用 KC-BUG-302 的内联 confirm 模式（`.chat-confirm-banner`）

---

## 非目标

- 不修改后端 `MainSessionService`（不做 live model swap）
- 不新增 Toast 组件（用 system note / inline confirm banner）
- 不修改 `useGatewaySnapshot` 为轮询

---

## 验证矩阵

| 层级 | 验证内容 |
|------|---------|
| L0 | `npm run typecheck` 通过，`dotnet build` 0 错 0 警告 |
| L1 | Vitest：onResumed 带 sessionId 调用；AbortController 在 clearMessages 时被调用 |
| L5 Dogfood | 手动验证三个修复路径均符合预期 |
