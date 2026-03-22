# Iteration 28 FREEZE：前端 UI/UX 统一优化（KC-W3 专项）

冻结日期：2026-03-21

---

## 背景与动机

Iteration 25 完成了布局重构（三栏→二栏），Iter 26-27 完成了 Settings Desk。但前端积累了多个系统性问题，影响产品质感与可维护性：

1. **无统一 Design Token**：间距、圆角、字号全部硬编码，重复定义散落各 CSS 文件，3 套 button 体系（`.settings-btn`、`.secondary-button`、`.bootstrap-form__submit`）并存
2. **Emoji 导航图标**：`💬 📥 🎨` 等 emoji 在 macOS 渲染为彩色、Windows 为黑白，跨平台不一致
3. **暗色主题未实现**：`ThemeMode` 类型与 UI 选项已存在，但 CSS 变量和 `App.tsx` 均未响应，ModelSettings 里的 Theme 选项是空操作
4. **消息气泡无方向性**：user 和 assistant 消息对称靠左，与现代 chat UI 习惯相悖
5. **Composer 固定高度**：`min-height: 100px` 固定高度，短消息浪费大量垂直空间
6. **Settings 内容左偏**：`max-width: 720px` 但没有 `margin: 0 auto`，宽屏下右侧大量空白
7. **Loading 状态单薄**：所有异步加载用纯文字"加载中…"，无 skeleton
8. **空状态设计缺失**：空列表只有纯文字，缺少 icon + 引导 CTA 结构
9. **响应式约束缺失**：Sidebar 无 min/max-width，主内容区无 max-width，宽屏/窄屏体验差
10. **`isMainDesk` 遗漏 'settings'**：localStorage 恢复时 settings desk 无法正确还原

---

## 范围（IN）

### W1：Design Token 系统
- `src/index.css` 新增 `--space-*`（1~8）、`--radius-*`（sm/md/lg/xl/pill）、`--font-size-*`（xs/sm/base/md/lg/xl）、`--duration-*`（fast/base/slow）tokens
- 字体栈切换为 Geist（Google Fonts variable font，向下 fallback system-ui）
- 统一 `.btn` 体系：base + 5 变体（primary/secondary/ghost/danger/link）+ 3 尺寸（sm/md/lg）；旧类（`.secondary-button`、`.bootstrap-form__submit`、`.settings-btn`）保留为属性别名，零破坏兼容
- 全局 focus-visible amber ring（`outline: 2px solid var(--accent); outline-offset: 2px`）
- Skeleton shimmer CSS（`@keyframes shimmer`）和 EmptyState CSS

### W2：暗色主题 CSS
- `[data-theme="dark"]` 显式覆盖（用于 Light→Dark 切换）
- `@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) }` 系统跟随
- 暗色 token：`--bg-primary: #0F0F10`、Sidebar `bg-secondary: #1A1A1C`、amber 稍亮 `#F59E0B`

### W3：Lucide React 图标系统
- 安装 `lucide-react` 包
- 新建 `src/components/ui/Icon.tsx` wrapper（size/strokeWidth/className 传递）
- `Sidebar.tsx`：`DESK_ICONS` 从 emoji string 改为 Lucide `ReactNode`；new-chat 按钮用 `<PenSquare>`；status footer 用 `<Activity>`
- `MainContent.tsx`：`DeskConfig.icon` 类型 `string → ReactNode`，值改为 Lucide 组件
- `DeskPageHeader.tsx`：`icon: string → ReactNode`

### W4：消息气泡方向 + auto-scroll
- `MessageTimeline.tsx` 删除顶部 eyebrow/title/streaming header（冗余区域）
- `timeline__body` 改为 `display: flex; flex-direction: column`
- `.message--user` 右对齐（`margin-left: auto; max-width: 75%`），圆角：右上角 sm
- `.message--assistant` 左对齐（`margin-right: auto; max-width: 100%`），圆角：左上角 sm
- `useRef + useEffect` auto-scroll-to-bottom（new messages / streaming）
- 空消息时用 `EmptyState` 组件

### W5：ChatComposer auto-grow
- `ChatComposer.tsx` 添加 `useRef + useEffect`，每次 value 变化后 `el.style.height = Math.min(el.scrollHeight, 160)px`
- CSS `.composer__input`：`min-height: 44px; max-height: 160px; resize: none; rows={1}`
- streaming 状态徽章移入 `composer__actions-right`（不再显示在 textarea 元信息区）

### W6：布局响应式约束 + Settings 居中
- `.kc-sidebar`：新增 `min-width: 220px; max-width: 280px`
- `.kc-desk-content`：新增 `max-width: 1200px`
- `.settings-desk`：新增 `margin: 0 auto; width: 100%`

### W7：Skeleton / EmptyState 组件
- 新建 `src/components/ui/Skeleton.tsx`：`.skeleton` + `.skeleton-stack` wrapper
- 新建 `src/components/ui/EmptyState.tsx`：icon + title + desc + children CTA
- `ConnectionsSection.tsx` 加载时显示 Skeleton，无账号时显示 EmptyState

### W8：暗色主题集成（App.tsx + useTheme）
- 新建 `src/hooks/useTheme.ts`：`fetchSettings()` → 读取 `theme: ThemeMode` → 调用 `applyTheme()`（Light → `data-theme="light"`，Dark → `data-theme="dark"`，System → 无属性）
- `App.tsx` 引入 `useTheme()`

### W9：Bug Fix
- `App.tsx` `isMainDesk` 函数加入 `value === 'settings'`

---

## 非目标（OUT）

- **Tool call 可视化**：当前 SSE 协议仅有 `text_chunk`/`done`/`error`，需后端协议扩展，留后续迭代
- **消息 Markdown 渲染**：`message.text` 目前用 `<p pre-wrap>` 渲染，带 `react-markdown` 的版本留后续
- **Sidebar 折叠/展开**：需要 ResizeObserver + 持久化，单独专项
- **手机端汉堡菜单**：需要 Drawer/Sheet 组件，单独专项
- **自动化 cron 编辑 UI**：不在本次范围

---

## 关键契约

| 契约 | 值 |
|------|-----|
| `data-theme` 属性值 | `"light"` \| `"dark"` \| 无属性（System） |
| Lucide strokeWidth | `1.75`（Sidebar 16px）/ `1.75`（DeskHeader 20px） |
| Composer min/max 高度 | min 44px / max 160px |
| Settings Desk 最大宽度 | 720px，居中 |
| 主内容区最大宽度 | 1200px |
| Sidebar 宽度范围 | 220px ~ 280px，默认 240px |
| user 消息最大宽度 | 75% |

---

## API 依赖

- `GET /api/settings`：读取 `{ theme: "System" | "Light" | "Dark" }` 用于 `useTheme`（已有端点）
- 无新增后端端点

---

## 验证命令

```bash
# L0：编译 + 类型检查
cd apps/kodaclaw-web
npm run typecheck && npm run build

# L1：单元测试（目标 52/52 pass）
npm run test

# L5 手动验收（需要 Gateway 运行）
make run-gateway
npm run dev
# 验收点：
# 1. 系统暗色模式下界面自动变暗；Light/Dark/System 切换后即时生效
# 2. Sidebar 图标为 Lucide SVG，macOS/Windows 渲染一致
# 3. user 消息右对齐气泡，assistant 消息左对齐
# 4. Composer 随输入内容自动伸缩（1行→多行→最大 160px）
# 5. Settings Desk 在 1440px 宽屏下居中显示
# 6. 刷新后 Settings Desk 能正确恢复（isMainDesk bug 修复）
# 7. ConnectionsSection 加载时显示 skeleton，无账号时显示空状态
```

---

## 受影响模块

| 模块 | 文件 |
|------|------|
| CSS Design System | `src/index.css`、`src/shell/app-shell.css` |
| Shell | `src/shell/Sidebar.tsx`、`src/shell/MainContent.tsx`、`src/shell/DeskPageHeader.tsx` |
| Chat | `src/components/MessageTimeline.tsx`、`src/components/ChatComposer.tsx` |
| Settings | `src/components/settings/ConnectionsSection.tsx` |
| App root | `src/App.tsx` |
| 新增 UI 组件 | `src/components/ui/Icon.tsx`、`src/components/ui/Skeleton.tsx`、`src/components/ui/EmptyState.tsx` |
| 新增 Hook | `src/hooks/useTheme.ts` |

---

## 依赖前置条件

- Iter 26-27 全部 Completed（KC-2601~2707）✅
- `lucide-react` 包安装（`npm install lucide-react`）
