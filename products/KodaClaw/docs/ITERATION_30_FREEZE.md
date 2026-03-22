# Iteration 30 FREEZE：Desk 布局/排版全量统一（KC-W4 专项）

冻结日期：2026-03-21

---

## 背景与动机

Iter 25-29 完成了 SettingsDesk 的新布局体系（`settings-section` / `settings-section-title` / `settings-section-desc`），但其余 7 个 Desk 组件仍在使用旧体系：

- `bootstrap-panel` 容器 + `section-eyebrow`（全大写微标签）+ `section-title`（无 CSS 定义，依赖浏览器默认 h2/h3）+ `section-copy`
- `status-card` 卡片容器（与新 `settings-section` 视觉语言不同）
- 各种 `control-plane-*` 字号使用硬编码 `1.8rem`、`0.9rem` 等裸值
- `canvas-markdown-view` 完全没有 CSS，Markdown 渲染用浏览器默认值（h1 = 2em）

结果：用户感知到"布局和字体大小不协调"，各 Desk 之间视觉语言分裂。

---

## 范围（IN）

### T1：简单 Desk 全量迁移（`settings-desk` + `settings-section` 体系）

适用：布局简单、无复杂两栏 split 的 Desk。

**T1-1：SkillsDesk.tsx**
- 外层 `bootstrap-panel` → 无特殊容器（直接用父层 padding）
- 移除 `section-eyebrow`
- `section-title` (h2) → `settings-section-title`
- `section-copy` → `settings-section-desc`
- 内部卡片保持现有 `.message.message--assistant` 风格不变

**T1-2：InboxApprovalDesk.tsx**
- 同上迁移，保留审批卡片内部结构

**T1-3：SessionsDiagnosticsDesk.tsx**
- 同上迁移，保留诊断卡片内部结构

### T2：中复杂度 Desk 排版规范化（保留 split/two-pane 布局，只修字号体系）

适用：内部有两栏或复杂列表，不适合 720px 单列的 Desk。

**T2-1：AutomationsDesk.tsx**
- 移除 `section-eyebrow`；`section-title` → `.desk-section-title`（见下）；`section-copy` → `.desk-section-desc`
- 内部 `status-card` 保留；`control-plane-*` 字号改用 token

**T2-2：PluginsDesk.tsx**
- 同上

**T2-3：ChannelsDesk.tsx**
- 同上；`threadButtonStyle` 已在 Iter 29 迁移完成

**T2-4：ModelsSettingsDesk.tsx**
- `control-plane-card-title`（已在本迭代即时修复从 1.8rem → var(--font-size-xl)）
- `section-eyebrow` 移除；各 `section-title` 改为 `.desk-section-title`
- 保留两栏 `control-plane-pane-shell` 布局（这是 UX 核心）

### T3：新增 `.desk-section-title` / `.desk-section-desc` CSS 工具类

位于 `app-shell.css`，供 T2 系列组件使用（复杂 Desk 不受 720px 限制但字号体系一致）：

```css
.desk-section-title {
  font-size: var(--font-size-md);  /* 1rem */
  font-weight: 600;
  margin: 0 0 var(--space-2);
  color: var(--text-primary);
}

.desk-section-desc {
  font-size: var(--font-size-sm);  /* 0.82rem */
  color: var(--text-secondary);
  margin: 0 0 var(--space-4);
  line-height: 1.5;
}
```

### T4：canvas-markdown-view 散文排版

（已在本迭代即时修复完成，ControlPlaneDesk.css 末尾追加完整 prose CSS）

---

## 非目标（OUT）

- **ChannelsDesk 状态机重构**：1200+ 行组件整体架构，单独专项
- **ModelsSettingsDesk 两栏→单列**：split 布局是 UX 核心，不动结构
- **PluginsDesk MCP 安装 UI**：功能扩展，单独专项
- **响应式断点优化**：移动端菜单，单独专项

---

## 关键契约变更

| 类型 | 变更 |
|------|------|
| CSS 类移除 | `section-eyebrow`（从各 Desk JSX 中删除，CSS 保留备用） |
| CSS 类替换 | `section-title` → `settings-section-title`（T1）或 `desk-section-title`（T2） |
| CSS 类替换 | `section-copy` → `settings-section-desc`（T1）或 `desk-section-desc`（T2） |
| CSS 类替换 | `bootstrap-panel` → 无容器（T1） |
| 新增 CSS 类 | `.desk-section-title`、`.desk-section-desc`（app-shell.css） |

---

## 验证命令

```bash
cd apps/kodaclaw-web
npm run typecheck && npm run build
npm run test  # 目标 52/52 pass（data-testid 保持不变）
```

---

## 受影响文件

| 文件 | 类型 |
|------|------|
| `src/components/SkillsDesk.tsx` | 修改（T1-1） |
| `src/components/InboxApprovalDesk.tsx` | 修改（T1-2） |
| `src/components/SessionsDiagnosticsDesk.tsx` | 修改（T1-3） |
| `src/components/AutomationsDesk.tsx` | 修改（T2-1） |
| `src/components/PluginsDesk.tsx` | 修改（T2-2） |
| `src/components/ChannelsDesk.tsx` | 修改（T2-3） |
| `src/components/ModelsSettingsDesk.tsx` | 修改（T2-4） |
| `src/shell/app-shell.css` | 修改（新增 T3 CSS 工具类） |
