# Iteration 32 FREEZE — Chat 历史会话面板（Session History Panel）

冻结日期：2026-03-21

---

## 目标

用户目前只能看到当前进行中的对话，无法浏览或恢复历史会话。本次迭代在 Chat 视图中增加**历史会话面板**，让用户可以看到过去的对话并一键恢复。

---

## 核心能力（Capability Slice）

### 后端

1. **新增 `POST /api/sessions/{id}/resume` 端点**
   - 清空当前 in-memory session（等同于 rotate 操作）
   - 将 `AppConfig.ActiveMainSessionId` 更新为目标 session ID
   - 返回 `{ ok: true, resumedSessionId: "<id>" }`
   - 下一次 SSE/chat 请求时 `EnsureMainSessionAsync` 将自动从 store 恢复目标 session
   - 需在 `IMainSessionService` 中增加 `ResumeSessionAsync(string sessionId)` 方法

2. **`GET /api/sessions` 已存在**，返回 `SessionSummary[]`，无需变更

### 前端

1. **Chat 头部扩展**：在 `kc-chat-header__actions` 区域增加 `<HistoryButton>` 图标按钮（`History` Lucide 图标）
2. **`SessionHistoryPanel` 组件**（`src/components/chat/SessionHistoryPanel.tsx`）：
   - 下拉抽屉样式，显示最近 20 个 `main` 类型 session
   - 每行显示：日期、状态 badge（active/idle/error）、最后活跃时间
   - 当前 session 高亮为 "当前"
   - "恢复" 按钮 → `POST /api/sessions/{id}/resume` → 成功后调用 `onRotateSession` 刷新 chat
3. **`useSessionHistory` hook**（`src/hooks/useSessionHistory.ts`）：
   - 调用 `fetchSessions()` 并处理加载/错误状态
   - 30s 轮询（与 ChannelsDesk 一致）

---

## API Contract

```
POST /api/sessions/{id}/resume
→ 200 OK: { "ok": true, "resumedSessionId": "<id>" }
→ 404: { "code": "session.not_found", "message": "..." }
→ 401: Unauthorized

GET /api/sessions (已有) → SessionSummary[]
```

---

## 非目标

- 不支持切换到 channel/automation 类型 session（仅限 main）
- 不删除历史 session
- 不修改 session 内容或标题
- 不在 Chat 视图内直接展示历史消息（仅切换，切换后 SSE 重连展示历史）

---

## 影响模块

- `src/KodaClaw.Runtime/IMainSessionService.cs`（新增接口方法）
- `src/KodaClaw.Runtime/MainSessionService.cs`（实现 ResumeSessionAsync）
- `src/KodaClaw.Gateway/Endpoints/GatewayApp.SessionEndpoints.cs`（新增 /resume 端点）
- `apps/kodaclaw-web/src/components/chat/SessionHistoryPanel.tsx`（新增）
- `apps/kodaclaw-web/src/hooks/useSessionHistory.ts`（新增）
- `apps/kodaclaw-web/src/shell/MainContent.tsx`（传递 onRotateSession）
- `apps/kodaclaw-web/src/App.tsx`（向 MainContent 传递 chatHeaderActions）

---

## 验证命令

```bash
# L0
dotnet build
cd apps/kodaclaw-web && npm run typecheck

# L1/L2 (后端)
dotnet test tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj \
  --filter "FullyQualifiedName~SessionResumeTests"

# 前端单元
npm run test

# L5 人工验收
# 1. 完成若干对话 → 新对话 → 打开历史面板 → 点击恢复 → 确认消息历史出现
# 2. 恢复不存在的 session → 显示错误
# 3. 当前 session 显示为"当前"，无"恢复"按钮
```
