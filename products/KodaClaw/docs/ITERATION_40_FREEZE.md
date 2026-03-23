# Iteration 40 Freeze — 前端 Modal 表单体验系统化

冻结时间：2026-03-23

## 背景

当前前端多个 Desk 的表单交互存在以下三类问题：

1. **删除/破坏性操作使用 `window.confirm()`**：原生系统弹窗在 Electron 中表现不稳定，且无法定制视觉风格（无危险红色确认、无操作说明）。受影响：`McpServersDesk`、`ModelsSettingsDesk`、`ChannelsDesk`。
2. **`ModelsSettingsDesk` 编辑 Composer 占位浪费**：右侧 Composer 面板在"仅查看"状态时是空置的，始终占用 50% 屏幕空间；字段多达 11 个，Modal 聚焦后更利于用户填写。
3. **`ConnectionsSection` / `ChannelSetupWizard` 缺乏上下文隔离**：向导在 Settings 页面内联展开，多步流程与设置项并列，视觉层次混乱；`SystemSection` 的破坏性操作确认也是 inline 展开，视觉分量不足。

## 目标

### KC-4001：`ConfirmModal` 通用确认弹窗
- 新建 `ConfirmModal` 组件（复用 `Modal.tsx`）
- 支持 `variant: "danger" | "warning" | "default"` 三档视觉强度
- 支持自定义标题、描述文本、确认按钮 label
- danger 变体：确认按钮为红色背景，与取消按钮明确区分
- 全量替换现有 `window.confirm()` 调用（McpServersDesk、ModelsSettingsDesk、ChannelsDesk）

### KC-4002：`SystemSection` 破坏性操作 → Modal
- "重新引导" 和 "清除身份" 两个操作改为点击按钮弹出 `ConfirmModal`
- Modal 包含操作说明文案 + 不可撤销警告 + 红色确认按钮
- 移除当前 `settings-confirm-row` inline 确认逻辑（`useConfirmAction` hook 内部的 inline confirming 状态）
- 简化为：idle → working → done/error 三态，确认步骤由 Modal 承担

### KC-4003：`ModelsSettingsDesk` 编辑 → Modal
- 将右侧 Composer 面板改为 Modal（复用 `Modal.tsx`）
- Modal 标题：创建时"新建模型端点"，编辑时"编辑 {端点名称}"
- Modal 内包含所有现有字段（名称、Provider、Model ID、Base URL、API Key、7 个 Capability 勾选项）和预设选择器
- "测试连接" 按钮保留在 Modal 内
- 左侧模型列表改为全宽（无右侧占位面板），编辑按钮触发 Modal
- "创建端点" 按钮移至工具栏

### KC-4004：`ConnectionsSection` / `ChannelSetupWizard` → Modal
- `ConnectionsSection` 中 "+ 绑定新渠道" 按钮点击后弹出 Modal
- Modal 内嵌现有 `ChannelSetupWizard` 多步向导，封装步骤导航和关闭行为
- 完成或取消后关闭 Modal，列表自动刷新
- `ChannelSetupWizard` 组件本身不改 API，只是由 Modal 容器包裹

## 非目标（本迭代不做）

- `InboxApprovalDesk` 批准/拒绝 → Modal（操作频率高，inline 体验更流畅，延后评估）
- `PluginsDesk` 安装/信任 → Modal（交互相对低频，延后）
- `AutomationsDesk` 启禁确认 → Modal（toggle 语义清晰，风险可接受）
- `WorkspaceIdentityEditor` Persona 选择 → Modal（搜索体验改善优先级低）
- `McpServersDesk` 的 ServerModal（已是 Modal，不改）
- 任何后端 API 变更

## 技术约束

- 所有改动限于 `apps/kodaclaw-web/src/` 内
- 不改变任何 API contract / Gateway 端点
- 不引入新的 npm 依赖
- 保持现有 Playwright test selector 稳定（`model-default`、`model-delete-*`、`settings-form` 等）
- 对 `ChannelSetupWizard` 只做容器包裹，不修改其内部逻辑和 props API

## 验证矩阵

| 层级 | 内容 | 通过标准 |
|-----|------|---------|
| L0 | `npm run typecheck` | 0 错 0 警告 |
| L0 | `npm run build` | 无构建错误 |
| L1 | `npm run test` | 现有 Vitest 测试全绿，无回归 |
| L5 | Dogfood：删除 MCP Server → 弹 ConfirmModal → 点确认 → 删除成功 | 人工走通 |
| L5 | Dogfood：新建模型端点 → Modal 弹出 → 填写 → 保存 → 列表更新 | 人工走通 |
| L5 | Dogfood：Settings → 连接 → 绑定新渠道 → Modal 内走向导全流程 | 人工走通 |
| L5 | Dogfood：Settings → 系统 → 清除身份 → 弹 Modal → 红色确认 → 执行 | 人工走通 |
