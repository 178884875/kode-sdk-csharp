# Iteration 25 FREEZE：前端全面重构（Claude Desktop 风格）

冻结日期：2026-03-21

## 背景与动机

当前 Web 前端经过 22-24 次迭代的快速叠加，存在三个核心问题：

1. **布局过复杂**：三栏布局（84px GlobalRail + 292px ContextRail + MainStage）中，ContextRail 承载了 9 个 Desk 描述卡、Bootstrap Panel、System Status Card 等大量信息，但几乎没有可操作内容，占据了大量视觉空间
2. **App.tsx 是 981 行 God Component**：~350 行内联 i18n、~8 个 Bootstrap 状态管理 useState、9 个 Desk if/else 路由全挤在一个文件，维护成本极高
3. **视觉风格与产品定位不符**：grain 纹理、glass-morphism、serif chrome 字体、暖棕色系与现代 Agent OS 产品形象不符

## 目标

以 Claude Desktop 的视觉风格为参考，重构为"对话优先"的二栏布局：
- 左侧固定侧边栏（240px）：导航 + 状态
- 右侧主内容区（全宽）：Chat 为主角，其他 Desk 全宽展开

## 范围

### 包含（In Scope）

- **KC-W2-010**：新 CSS Design System（theme 变量 + index.css 重写）
- **KC-W2-011**：新 AppShell + Sidebar + MainContent（替代 V2Shell 三栏）
- **KC-W2-012**：App.tsx 瘦身（抽取 i18n/app-strings.ts + useBootstrap hook）
- **KC-W2-013**：Onboarding CSS 对齐新设计语言
- **KC-W2-014**：删除旧壳（shell-v1, shell-v2）+ 更新受影响测试

### 不包含（Out of Scope）

- 后端代码任何改动
- Desk 组件内部逻辑修改（InboxApprovalDesk, ChannelsDesk 等保持不变）
- API 层修改（lib/api.ts 保持不变）
- 新功能开发

## 设计语言

### 色彩系统

```
背景：#FFFFFF (主内容) / #F7F7F8 (侧边栏)
文字：#1A1A1A (主) / #6B6B6B (次) / #9B9B9B (弱)
边框：#E5E5E5 (细) / #D1D1D1 (中)
强调：#D97706 (amber/orange，KodaClaw 品牌色保留)
状态：成功 #16A34A / 警告 #CA8A04 / 错误 #DC2626
```

### 字体与风格

- `font-family: -apple-system, BlinkMacSystemFont, "Inter", "Segoe UI", sans-serif`
- 无 serif 字体用于 UI chrome
- 无 grain 纹理、无 glass-morphism、无 paper-haze
- Card radius: 12px，Button/Input radius: 8px

## 布局设计

```
┌─────────────────────────────────────────────────────────┐
│ Sidebar (240px, fixed) │ MainContent (remaining width)  │
│                        │                                 │
│ [KC] KodaClaw          │ Chat 模式：                     │
│                        │   MessageTimeline（全宽滚动）   │
│  + 新对话              │   ChatComposer（底部固定）      │
│ ──────────────────     │                                 │
│ 💬  对话               │ 其他 Desk：                     │
│ 📥  收件箱 [2]         │   DeskPageHeader（标题栏）      │
│ 🎨  画布               │   <DeskComponent />（全宽）     │
│ ──────────────────     │                                 │
│ ⚡  自动化             │                                 │
│ 📡  渠道               │                                 │
│ ──────────────────     │                                 │
│ 🧩  模型               │                                 │
│ 🔌  插件               │                                 │
│ ──────────────────     │                                 │
│ 🔍  会话诊断           │                                 │
│ ⚙️  技能               │                                 │
│                        │                                 │
│ ● Healthy              │                                 │
└─────────────────────────────────────────────────────────┘
```

## data-testid 迁移

| 旧 | 新 |
|----|----|
| `v2-shell` | `kc-shell` |
| `v2-global-rail` | `kc-sidebar` |
| `v2-context-rail` | 删除 |
| `v2-main-stage` | `kc-main-content` |
| `v2-chat-stage` | `kc-chat-view` |
| `v2-chat-stage-head` | `kc-chat-header` |
| `desk-tab-{id}` | **保持不变** |
| `bootstrap-pill` | `kc-bootstrap-banner` |

## 文件变更汇总

### 新增
- `src/shell/AppShell.tsx`
- `src/shell/Sidebar.tsx`
- `src/shell/MainContent.tsx`
- `src/shell/DeskPageHeader.tsx`
- `src/shell/app-shell.css`
- `src/i18n/app-strings.ts`
- `src/hooks/useBootstrap.ts`

### 修改
- `src/App.tsx`（981 行 → ~180 行）
- `src/index.css`（色彩/字体变量替换）
- `src/onboarding/onboarding.css`（对齐新设计语言）
- `src/shell-shared/types.ts`（简化 ShellLayoutProps）
- `src/__tests__/app-shell.spec.tsx`（更新 data-testid）
- `tests/kc0108-smoke.spec.ts`（更新 shell root testid）

### 删除
- `src/shell-v1/`（整目录）
- `src/shell-v2/`（整目录）
- `src/components/DeskHeader.tsx`
- `src/components/SystemStatusCard.tsx`
- `src/shell-shared/shell-variant.ts`

### 不变
- 所有 Desk 组件（`components/` 下 ~12 个）
- `hooks/useChatConsole.ts`, `useGatewaySnapshot.ts`, `useInboxUnreadCount.ts`
- `lib/api.ts`, `lib/config.ts`
- `types/contracts.ts`, `types/chat.ts`
- `onboarding/` 步骤组件（仅 CSS 变动）
- `components/MessageTimeline.tsx`, `ChatComposer.tsx`, `BootstrapPanel.tsx`

## 验证

```bash
# L0
cd apps/kodaclaw-web && npm run typecheck && npm run build

# 单元测试
npm run test

# L4（需 Gateway 运行）
npm run test:e2e
```
