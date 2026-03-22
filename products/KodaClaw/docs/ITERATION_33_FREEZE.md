# Iteration 33 FREEZE — Automations 启用/禁用开关 + Inbox 自动化结果优化

冻结日期：2026-03-21

---

## 目标

填补两个已知产品缺口：
1. AutomationsDesk 缺少 `automationsEnabled` 开关的前端控制（CLAUDE.md WIP 记录）
2. InboxApprovalDesk 已展示 AutomationResult 条目，但没有专属的 UI 渲染（显示为通用卡片）

---

## 核心能力

### KC-3301：Automations 启用/禁用 Toggle

**前端**（仅需前端）：
- `AutomationsDesk` 在工具栏右侧增加 `<ToggleSwitch>` 组件（或 checkbox + label）
- 读取 `GET /api/settings` 中的 `automationsEnabled` 字段
- 点击 → `PATCH /api/settings` 发送 `{ "automationsEnabled": <bool> }`
- 状态切换立即反映在 Desk 状态描述文字（已启用/已禁用）

**后端**（已有 `PATCH /api/settings` 支持 `automationsEnabled` 字段，无需新增端点）

验证：L0 + 前端手动验收（需 Gateway）

---

### KC-3302：Inbox AutomationResult 专属渲染

**前端**：
- `InboxApprovalDesk` 的 inbox 列表中，对 `kind === 'AutomationResult'` 的条目显示专属卡片样式：
  - 图标：`Zap`（自动化图标）而非通用消息图标
  - 标题：显示 automation 名称或 session 摘要
  - 状态 badge：success/failed/running
  - 无"审批"操作按钮（AutomationResult 只可 "标为已读"）
- 在 Inbox 列表区域的 tab 分组中增加 "全部 / 审批 / 自动化结果" 过滤切换

**后端**（`GET /api/inbox?kind=AutomationResult` 已支持，无需新增）

验证：L0 + `npm run test`（补充渲染测试）+ L5 人工验收

---

## 非目标

- 不修改 automation 调度逻辑
- 不增加新的 automation 类型
- 不修改 inbox 数据模型

---

## 影响模块

- `apps/kodaclaw-web/src/components/AutomationsDesk.tsx`（增加 toggle）
- `apps/kodaclaw-web/src/components/InboxApprovalDesk.tsx`（AutomationResult 渲染 + tab 分组）
- `apps/kodaclaw-web/src/index.css` 或 `ControlPlaneDesk.css`（新增 automation-result-card 样式，如需）

---

## 验证命令

```bash
# L0
cd apps/kodaclaw-web && npm run typecheck && npm run test

# L5 人工验收（需 Gateway 运行中）
# KC-3301: 打开 AutomationsDesk → 点击 toggle → 刷新 → 确认状态持久化
# KC-3302: 触发一次 automation → 打开 Inbox → 确认自动化结果条目可见并样式正确
```
