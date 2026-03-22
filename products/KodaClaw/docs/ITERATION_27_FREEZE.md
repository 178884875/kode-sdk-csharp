# Iteration 27 FREEZE：Settings Desk

冻结日期：2026-03-21

## 背景与动机

Iteration 26 完成后，以下配置入口在产品中缺少可见的 UI 出口：

1. **workspace 身份文件**（IDENTITY.md / SOUL.md / USER.md）：只能靠 Koda 在对话中引导写入，没有用户可主动编辑的界面
2. **Persona 模板选择**：从 Onboarding 移出后，无处可访问
3. **Telegram 渠道绑定向导**：从 Onboarding 移出后，只有 Channels Desk 的原始账号管理表单（无引导）
4. **语言切换**：当前在 chat header 的 LocaleToggle 组件，位置不合理（设置项不应出现在对话区）
5. **Skills Desk**：当前和会话诊断挤在同一组，定位模糊

目标：新增 Settings Desk（⚙️ 设置），作为 workspace 身份 + 应用偏好 + 连接配置的统一入口；同时调整侧边栏分组，将 Skills 归入能力层分组，结构更清晰。

---

## 设计决策

### 侧边栏布局调整

```
变更前（9 项，4 组）        变更后（10 项，4 组）
────────────────            ────────────────
💬  对话                    💬  对话
📥  收件箱                  📥  收件箱
🎨  画布                    🎨  画布
────────────                ────────────
📡  渠道                    📡  渠道
⚡  自动化                  ⚡  自动化
────────────                ────────────
🧩  模型                    🧩  模型
🔌  插件                    🔌  插件
────────────                💡  技能          ← 从底组移入能力层
🔍  会话诊断                ────────────
⚙️  技能                    🔍  会话诊断
                            ⚙️  设置          ← 新增，压底独立
```

第三组语义升级为"能力层"（模型 / 插件 / 技能），第四组保留诊断和设置。

### Settings Desk 内容结构

```
⚙️  设置
├── 工作区身份
│   ├── IDENTITY.md 编辑器（当前 Persona 名称标签 + 全文编辑）
│   ├── SOUL.md 编辑器（+ "从模板选择" → PersonaSelector 展开）
│   └── USER.md 编辑器
│
├── 连接
│   └── 渠道绑定向导（复用 ChannelSetupWizard，支持 Telegram；
│                     未来插件化渠道在此扩展）
│
├── 偏好
│   ├── 界面语言（zh / en，替代 chat header LocaleToggle）
│   └── [预留：主题 / 通知等]
│
└── 系统
    ├── 重新运行引导程序（POST /api/onboarding/reset → 刷新页面触发 OnboardingShell）
    └── 清除 workspace 身份（仅清除三个 workspace 文件内容，不影响模型 / 插件 / 历史 session）
```

### workspace 身份编辑器行为

- 每个文件独立显示，不折叠，支持多行内联编辑（`<textarea>`，高度自适应内容）
- 编辑后点"保存"调用 `PUT /api/workspace/file`（现有接口，target: identity/soul/user）
- 保存成功后提示"已保存，下次 session 启动时生效"
- SOUL.md 编辑器上方放"从模板选择"按钮，展开 PersonaSelector 卡片网格（复用迁移后的组件）；选中模板内容填入编辑器，用户可再手动修改后保存

### PersonaSelector 复用路径

Iteration 26 中 `PersonaStep.tsx` 迁移到 `src/components/settings/PersonaSelector.tsx`：

```tsx
// 接口简化为纯展示+选择，不含 onboarding 状态推进逻辑
type PersonaSelectorProps = {
  onSelect: (soulMarkdown: string) => void;
  onDismiss: () => void;
};
```

### ChannelSetupWizard 复用路径

Iteration 26 中 `ChannelStep.tsx` 迁移到 `src/components/settings/ChannelSetupWizard.tsx`：

```tsx
// 同样剥离 onboarding advance 逻辑，改为 onComplete / onDismiss 回调
type ChannelSetupWizardProps = {
  onComplete: () => void;
  onDismiss: () => void;
};
```

Settings Desk "连接" section 渲染已绑定的渠道账号列表（复用 ChannelsDesk 的账号列表子组件），点"绑定新渠道"展开 ChannelSetupWizard。

### LocaleToggle 迁移

- `src/components/LocaleToggle.tsx` 保留组件逻辑不变
- 从 `App.tsx` chatHeaderActions 中移除
- 在 Settings Desk "偏好" section 中直接渲染
- chat header 不再有任何工具栏按钮（更干净）

### 清除 workspace 身份的安全设计

- 点击后弹出确认对话框："此操作将清空 IDENTITY.md、SOUL.md、USER.md 的内容，下次对话 Koda 会重新引导你配置身份。此操作不可撤销。确认清除？"
- 确认后调用三次 `PUT /api/workspace/file`（content 置空），再提示 Koda 下次会重新引导

---

## 范围

### 包含（In Scope）

- **KC-2701**：侧边栏分组调整（技能移入能力层，设置新增压底）
- **KC-2702**：Settings Desk 框架（4-section 布局 + 路由接入）
- **KC-2703**：工作区身份编辑器（IDENTITY / SOUL / USER 内联编辑 + 保存）
- **KC-2704**：PersonaSelector 集成（SOUL.md 编辑器内"从模板选择"）
- **KC-2705**：连接 section（ChannelSetupWizard 集成 + 已绑账号展示）
- **KC-2706**：偏好 section（LocaleToggle 迁移，删除 chat header 工具栏）
- **KC-2707**：系统 section（重新引导 + 清除 workspace 身份确认弹框）

### 不包含（Out of Scope）

- workspace 文件的 diff / 历史版本查看
- MEMORY.md 编辑器（读写策略不同，后续单独规划）
- HEARTBEAT.md 编辑器（属于 Automation 领域）
- 主题切换（预留 UI 占位，不实现）
- 渠道账号的编辑 / 删除（保持在 ChannelsDesk，Settings 只做绑定入口）
- 模型、插件、技能的设置（各自 Desk 已有，不合并）

---

## 文件变更汇总

### 新增

| 文件 | 说明 |
|------|------|
| `src/components/SettingsDesk.tsx` | Settings Desk 根组件，4 section 布局 |
| `src/components/settings/WorkspaceIdentityEditor.tsx` | IDENTITY / SOUL / USER 三文件编辑器 |
| `src/components/settings/PersonaSelector.tsx` | 从 PersonaStep.tsx 迁移重构 |
| `src/components/settings/ChannelSetupWizard.tsx` | 从 ChannelStep.tsx 迁移重构 |
| `src/components/settings/SystemSection.tsx` | 重新引导 + 清除 workspace 身份 |

### 修改

| 文件 | 变更要点 |
|------|---------|
| `src/shell/Sidebar.tsx` | 调整 NAV_GROUPS：技能入能力层，设置新增底部独立组 |
| `src/shell/MainContent.tsx` | 新增 `mainDesk === 'settings'` 路由 → `<SettingsDesk />` |
| `src/shell-shared/types.ts` | `MainDesk` 类型新增 `'settings'`，移除 `'skills'`（若并入）或保留 |
| `src/i18n/app-strings.ts` | desks 数组新增 settings 条目，skills 条目图标调整 |
| `src/App.tsx` | 移除 chatHeaderActions 中的 LocaleToggle |
| `src/onboarding/steps/PersonaStep.tsx` | 删除（已迁移） |
| `src/onboarding/steps/ChannelStep.tsx` | 删除（已迁移） |

### 保持不变

- `src/components/LocaleToggle.tsx`（组件逻辑不变，只是挂载位置变）
- `src/components/SkillsDesk.tsx`（内容不变，Desk ID 保留 `skills`）
- `src/components/ChannelsDesk.tsx`（账号管理主页面不变）
- 所有后端接口（`PUT /api/workspace/file` 等已有接口直接复用）

---

## data-testid 新增

| testid | 位置 |
|--------|------|
| `settings-desk` | SettingsDesk 根容器 |
| `settings-identity-section` | 工作区身份 section |
| `settings-identity-editor` | IDENTITY.md textarea |
| `settings-soul-editor` | SOUL.md textarea |
| `settings-user-editor` | USER.md textarea |
| `settings-persona-trigger` | "从模板选择"按钮 |
| `settings-connections-section` | 连接 section |
| `settings-preferences-section` | 偏好 section |
| `settings-system-section` | 系统 section |
| `settings-reset-onboarding` | 重新引导按钮 |
| `settings-clear-identity` | 清除 workspace 身份按钮 |

---

## 后端依赖

本迭代全部复用现有 Gateway 接口，无需新增端点：

| 接口 | 用途 |
|------|------|
| `GET /api/workspace/file?target=identity` | 读取 IDENTITY.md 内容 |
| `PUT /api/workspace/file` `{ target, content }` | 保存编辑后的文件 |
| `GET /api/workspace/persona-presets` | PersonaSelector 模板列表 |
| `POST /api/onboarding/reset` | 重新引导触发 |
| `GET /api/channels/accounts` | 已绑定渠道账号列表 |
| `POST /api/channels/accounts` | 新绑定渠道账号 |

---

## 验证

```bash
# L0
cd apps/kodaclaw-web && npm run typecheck && npm run build

# 单元测试
npm run test    # 新增 settings-desk.spec.tsx

# L4（需 Gateway 运行）
npm run test:e2e
```

---

## 前置依赖

- Iter 26 KC-2601：PersonaStep / ChannelStep 已迁移到 `src/components/settings/`
- Iter 26 KC-2602：BootstrapPanel 已删除，App.tsx 无 bootstrap mode
- Iter 26 KC-2603：`GET /api/workspace/readiness` 已存在（Settings Desk 可选消费）
