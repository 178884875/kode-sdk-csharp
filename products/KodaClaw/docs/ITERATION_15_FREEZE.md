# Iteration 15 Freeze — AutomationsEnabled UI + V2 Chat Stage 收口

**冻结日期**：2026-03-20
**前置迭代**：Iteration 14（workspace_read / automations enabled 后端 / channel thread summary）
**主题**：纯前端。补齐自动化引擎的用户可见开关、为 AutomationsDesk 补全引擎状态感知，并收口 V2 Chat 主舞台的 page-like 痕迹。

---

## 一句话目标

让用户能在 UI 中开/关自动化引擎全局开关，并让 V2 Shell 的 Chat 主舞台真正做到 conversation-first（移除剩余的 page-style 页面痕迹）。

---

## In-scope

| KC | 描述 | 规模 |
|----|------|------|
| KC-1501 | `automationsEnabled` 全局开关——Settings 面板补 toggle，AutomationsDesk 补引擎状态 Banner | S |
| KC-1502 | V2 Chat 主舞台收口（KC-W2-004 关闭）——移除剩余 page-like 痕迹，主舞台真正成为 conversation canvas | S |
| KC-1503 | ChannelsDesk + PluginsDesk V2 pane 接入验收（KC-W2-007）——确认两个 desk 在 V2 shell 内的视觉与 test-id 稳定性 | XS |

---

## Out-of-scope（本迭代明确不做）

- Desktop Shell（Electron）启动——留专项迭代
- `automationsEnabled` 之外的 Settings 字段 UI 改造
- ChannelsDesk / PluginsDesk 内部功能扩展（只做接入验收，不改功能）
- Automations run 的手动触发按钮（留后续迭代）
- 后端改动（全部 API 已在 Iter 14 就绪）

---

## 架构决策

### KC-1501：automationsEnabled 全局开关

**ModelsSettingsDesk — Settings 区新增 toggle**
- 从 `GET /api/settings` 读取 `automationsEnabled`
- 用 `<input type="checkbox">` 或 toggle button 呈现，label 为"启用自动化引擎"
- 保存时调用已有的 `PUT /api/settings`，payload 合并 `{ automationsEnabled: <bool> }`
- `data-testid="settings-automations-enabled-toggle"`

**AutomationsDesk — 引擎状态 Banner**
- 在 desk 顶部（toolbar 之上）从 `GET /api/settings` 读取 `automationsEnabled`
- `automationsEnabled = false` 时显示 Warning Banner："自动化引擎当前已关闭。在设置页启用后，所有已开启的自动化才会按计划执行。"
- `automationsEnabled = true` 时不显示（或显示绿色 chip）
- `data-testid="automations-engine-banner"`，避免重写 desk 逻辑，只在 desk render 顶部插入

依赖：`fetchSettings` 已有，`updateSettings` 已有。无需后端改动。

---

### KC-1502：V2 Chat 主舞台收口

当前问题（KC-W2-004 遗留）：
- Chat stage header 有旧 page-like metrics 区，在 `is-chat` 模式下冗余
- `v2-stage-surface--chat` 包裹的 Workbench 内 composer/timeline 区域仍有额外 margin/padding

目标：
- Chat 模式下 `v2-main-stage` 全高撑满，不留顶部空白
- Composer 固定在底部，Timeline 占满中部，不出现滚动条嵌套
- 移除 `v2-stage-head__metrics` 在 `is-chat` 模式的冗余显示

不改 test-id（`v2-chat-stage-head`、`v2-chat-stage` 等保持稳定）。

---

### KC-1503：ChannelsDesk + PluginsDesk V2 接入验收

两个 desk 组件（`ChannelsDesk.tsx`、`PluginsDesk.tsx`）已存在且有完整功能，已接入 `App.tsx` workbench 路由。
本迭代目标：
1. 确认 V2 GlobalRail 导航到这两个 desk 的 test-id 稳定（`data-kc-view="channels"` / `data-kc-view="plugins"`）
2. 补 Playwright E2E 测试（`kc0508-channels.spec.ts` / `kc0407-plugins.spec.ts`）中缺失的 desk 导航用例
3. 不重构 desk 内部（它们已有 split layout）

---

## 验证命令

```bash
# L0
cd apps/kodaclaw-web && npm run typecheck

# L1 前端单元
npm test -- src/__tests__/automations-desk.spec.tsx src/__tests__/models-settings-desk.spec.tsx

# L4 E2E（需要 make run-gateway + npm run dev）
npm run test:e2e -- tests/kc0308-automations.spec.ts tests/kc0212-models-settings.spec.ts tests/kc0508-channels.spec.ts tests/kc0407-plugins.spec.ts

# 全量前端
npm run typecheck && npm test && npm run build
```

---

## Wave 计划

| Wave | 内容 |
|------|------|
| Wave 1 | KC-1501：Settings toggle + AutomationsDesk banner + L1 测试更新 |
| Wave 2 | KC-1502：V2 Chat 主舞台 CSS 收口 + E2E 回归 |
| Wave 3 | KC-1503：Channels / Plugins desk E2E 接入验收 + BACKLOG 更新 + commit |
