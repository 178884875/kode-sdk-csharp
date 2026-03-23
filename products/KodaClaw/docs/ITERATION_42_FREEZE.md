# Iteration 42 Freeze — Session History Restoration

**冻结日期**：2026-03-23
**范围负责人**：codex/kodaclaw-20260320

---

## 背景

用户通过历史面板恢复一个旧 session 后，Chat 区域仅显示"会话已恢复"提示，历史消息不可见。对于长期使用的用户，这让 resume 功能几乎没有实用价值——无法回顾之前的对话内容。

---

## 范围

### KC-4201 — 后端：`GET /api/sessions/{id}/messages` 端点

- **契约**：新增 `SessionMessageItem` record（id、role、text、timestamp）和 `SessionMessagesResponse`（items、totalCount、hasMore）
- **端点**：`GET /api/sessions/{id}/messages?limit=20&skip=0`
- 从 `JsonAgentStore.LoadMessagesAsync` 读取消息，仅提取 User/Assistant 角色、TextContent 拼接文本
- 按时间倒序（消息数组末尾 = 最新），返回 `skip` 到 `skip+limit` 的切片
- session 不存在时返回 404

### KC-4202 — 前端：resume 后加载历史 + "加载更多"

- `api.ts`：`fetchSessionMessages(sessionId, limit, skip)`
- `chat.ts`：`ChatMessage.isHistory?: boolean`；新增 role `"history_separator"`
- `useChatConsole.ts`：新增 `prependHistory(items)`、`loadMoreHistory()`、`isLoadingHistory`、`hasMoreHistory`
- `App.tsx`：`resumeSession` 调用后，触发 `loadHistory(sessionId, limit=20, skip=0)`，prepend 到消息列表头部，插入 history_separator
- `MessageTimeline.tsx`：顶部渲染"加载更多"按钮（`hasMoreHistory`）；history_separator 渲染分隔线；历史消息视觉减淡（`opacity: 0.7`）
- `index.css`：`.history-separator`、`.message--history`、`.load-more-history` 样式

---

## 非目标

- 不支持 Channel / Automation session 的历史加载（只加载当前 active 的 main session 历史）
- 不做 AI 摘要压缩
- 不做无限滚动自动触发（点击式加载更多）

---

## 验证命令

```bash
# L0
dotnet build products/KodaClaw/KodaClaw.sln
cd apps/kodaclaw-web && npm run typecheck

# L2 — 集成测试（新增测试文件）
dotnet test products/KodaClaw/KodaClaw.sln -m:1 --filter "SessionMessages"

# L4 — 手动验证步骤
# 1. 发送几条消息
# 2. 通过历史面板点击 Resume
# 3. 确认历史消息出现在 Chat 顶部，有分隔线
# 4. 点击"加载更多"，更早的消息追加到顶部
```
