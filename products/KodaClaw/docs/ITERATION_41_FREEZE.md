# Iteration 41 Freeze — Chat UX as Agent OS

冻结时间：2026-03-23

## 背景

KodaClaw 的 Chat 页面目前是一个标准聊天应用的形态——消息气泡 + 文字流。但 PRODUCT.md 对 KodaClaw 的定位是"Agent OS"，核心差异在于：

1. **工具调用不可见**：Agent 执行了哪些工具、花了多长时间，用户完全看不到，只能等一大段文字突然出现。
2. **流式状态没有语义**：Composer 区域的 hint 显示"正在接收回复…"，和普通 IM 无异；Agent 实际在执行工具时用户不知道在等什么。
3. **时间戳常驻干扰布局**：每条消息顶部固定显示时间，对连续对话来说视觉噪音大，反而让有效信息密度降低。

## 目标

### KC-4101：时间戳 hover 展示
- `message__time` 默认 `opacity: 0`
- 鼠标悬停 `article.message` 时以 transition 淡入显示
- user / assistant / system / error 消息均适用
- 纯 CSS，不改 React 组件

### KC-4102：工具调用 inline 活动块
- 后端：订阅 SDK `"tool:end"` 事件，向 SSE 流追加 `tool_activity` 事件，携带 `toolName`、`durationMs`
- 前端：新的 `"tool_activity"` role message + `ToolActivityBlock` 组件（紧凑行，`⚙ {toolName} · {durationMs}ms`）
- 内嵌在 `MessageTimeline` 中，工具结束时插入，不干扰消息正文

### KC-4103：流式状态语义化
- 后端：订阅 SDK `"tool:start"` 事件，向 SSE 流追加 `agent_working` 事件，携带 `toolName`
- 前端：`useChatConsole` 跟踪 `activeToolName` state，流式结束时清空
- `ChatComposer` hint 在 streaming + activeToolName 时显示 "Koda 正在执行 {toolName}…"（中文）/ "Koda is running {toolName}…"（英文）

## 非目标（本迭代不做）

- 工具调用详情展开（input/output preview）
- 工具调用错误状态差异化渲染（仍走 tool_warning）
- 自动化 session 的工具可见性
- 任何后端 API schema 变更（仅扩展现有 SSE 事件字段）

## 技术约束

- 后端仅改 `KodaClaw.Contracts/ChatStreamEvent.cs` 和 `KodaClaw.Runtime/ChatSessionService.cs`
- 前端仅改 `apps/kodaclaw-web/src/` 内文件
- 不引入新 npm 依赖
- 保持现有 Playwright test selector 稳定

## 验证矩阵

| 层级 | 内容 | 通过标准 |
|-----|------|---------|
| L0 | `dotnet build KodaClaw.sln` | 0 错 0 警告 |
| L0 | `npm run typecheck` | 0 错 0 警告 |
| L0 | `npm run build` | 无构建错误 |
| L1 | `npm run test` | 现有 Vitest 测试全绿，无回归 |
| L5 | Dogfood：发一条消息触发工具调用 → 看到 ⚙ 工具块 + Composer hint 变化 | 人工走通 |
| L5 | Dogfood：悬停消息 → 时间戳淡入显示 | 人工走通 |
