# Iteration 26 FREEZE：自然语言 Workspace 引导（对话即配置）

冻结日期：2026-03-21

## 背景与动机

Iteration 24 完成的 Onboarding 引导程序包含：语言选择 → 模型配置 → Persona 模板 → Telegram 渠道 → 完成页。其中：

- **模型配置（KC-2403）**：不可去掉——Koda 需要 API Key 才能开口，属于硬依赖
- **Workspace 身份配置（Bootstrap Panel）**：目前通过侧边面板的三个文本框完成，属于"表单填写"思维，与 Agent OS 产品定位不符

当前 Bootstrap Panel 的问题：
1. 用户面对空白文本框，不知道该写什么
2. 侧边面板占据视觉空间但缺乏引导
3. BootstrapPanel 组件 + `useBootstrap` hook + App.tsx bootstrap 态管理 = 约 400 行专用代码
4. 与 PRODUCT.md 核心原则"inspectable autonomy"和"conversational-first"相悖

目标：用 Koda 主动开口的对话替代 Bootstrap Panel，让 workspace 身份配置成为自然的第一次对话，而非表单填写。

---

## 核心设计决策

### 谁来引导？

**Koda 主动引导**，而非产品表单。

当 Koda 在 system prompt 中检测到 IDENTITY.md / SOUL.md / USER.md 为空（或仅有默认模板内容）时，在第一条消息前主动开口：询问用户希望如何称呼 Koda、主要工作类型、沟通偏好。

### 怎么写入？

Koda 在完成信息收集后，调用 `workspace_protocol_update` 工具写回对应文件，并在聊天中显示确认。

### 写入后何时生效？

写入后自动调用 `rotate_session`（后端新开 session），让新 system prompt 即时加载更新后的 workspace。Koda 在新 session 开头确认："已记住，这些设定从现在开始生效。"

### Onboarding 流程怎么变？

| 步骤 | 当前 | 变更后 |
|------|------|--------|
| 语言选择 | OnboardingShell 第 1 步 | **改为跟随 OS/浏览器 locale 自动检测**，无需用户选择 |
| 模型配置 | OnboardingShell 第 2 步 | **保留**（必须，Koda 需要模型才能开口） |
| Persona 选择 | OnboardingShell 第 3 步 | **移除**（由 Koda 在对话中引导或用户后续在主界面设置） |
| Telegram 渠道 | OnboardingShell 第 4 步 | **移除**（后续可从 Channels Desk 自行绑定） |
| 完成页 | OnboardingShell 第 5 步 | **简化**：仅显示"模型已配置，开始和 Koda 对话吧" |

精简后 Onboarding：语言自动检测 → 模型配置 → 直接进入主界面

### 如何处理"不想现在配置"？

Koda 在第一次询问时，如果用户明确表示不想配置，写入 session 级别 flag（内存，不持久化），本 session 内不再主动询问。下次新 session 若 workspace 仍为空，重新判断。

### 已有内容如何处理？

Koda 不会重复询问已有内容。对每个文件独立检查：
- IDENTITY.md 有实质内容 → 不询问 Koda 名字/角色
- SOUL.md 有实质内容 → 不询问行为准则
- USER.md 为默认模板（空占位） → 主动询问用户画像

### 透明度

Koda 在每次写入前说明将要写入哪个文件，写入成功后确认文件路径，符合 PRODUCT.md "inspectable autonomy" 原则。

---

## 范围

### 包含（In Scope）

- **KC-2601**：精简 Onboarding（去掉语言/Persona/Channel 步骤，加 locale 自动检测）
- **KC-2602**：删除 Bootstrap Panel + `useBootstrap` hook + App.tsx bootstrap mode 相关代码
- **KC-2603**：Gateway 新增 workspace 空缺检测接口（`GET /api/workspace/readiness`）
- **KC-2604**：Koda system prompt 注入引导提示（workspace 为空时附加引导 section）
- **KC-2605**：写入后自动 rotate session，Koda 在新 session 中确认生效
- **KC-2606**：session 级别 suppress flag（用户拒绝后本 session 不再询问）

### 不包含（Out of Scope）

- 重新设计 Onboarding 的模型配置步骤（保持不变）
- workspace 文件的可视化编辑器（保持现有 BootstrapPanel 之外的访问方式不变）
- 多语言 locale 的详细选项页（仅 zh/en 自动检测，无手动切换入口改动）
- Persona 预设库的前端展示（移除出 Onboarding，Settings 页后续可接入）
- 后端 `workspace_protocol_update` 工具的修改（已有，直接复用）

---

## 技术细节

### KC-2603：workspace readiness 接口

```
GET /api/workspace/readiness

Response:
{
  "identityMissing": bool,   // IDENTITY.md 为空或仅默认模板
  "soulMissing": bool,       // SOUL.md 为空或仅默认模板
  "userMissing": bool,       // USER.md 为空或仅默认模板
  "hasAnyGap": bool          // = identityMissing || soulMissing || userMissing
}
```

判断"仅默认模板"的标准：文件内容与 `WorkspaceService.DefaultXxx` 完全一致，或字符数 < 50（仅有占位符行）。

### KC-2604：system prompt 引导 section

当 `hasAnyGap=true` 时，Gateway 在组装 session system prompt 时附加：

```markdown
## 首次引导提示（仅在 workspace 不完整时附加）

以下 workspace 文件尚未配置：{missing list}

请在本次对话开始时**主动询问**用户以下信息（每次只问一个问题，不要一次抛出全部）：
- IDENTITY.md 缺失时：你希望我叫什么名字？我们主要会一起做哪类工作？
- SOUL.md 缺失时：你希望我的工作风格是怎样的？有什么行为原则需要特别注意？
- USER.md 缺失时：能简单介绍一下你自己的工作习惯和沟通偏好吗？

信息收集完成后，**先总结让用户确认**，确认后调用 `workspace_protocol_update` 写入对应文件，并告知用户文件路径。

如果用户明确表示"不想现在配置"或"跳过"，请尊重用户意愿，不再在本次会话中重复询问。
```

### KC-2605：写入后 rotate session

`workspace_protocol_update` 工具执行成功后，Koda 调用现有的 `rotate_session` 工具（或 Gateway 自动触发）新开 session。新 session 加载更新后的 workspace，Koda 第一条消息确认："workspace 已更新，新设定从现在开始生效。"

---

## 文件变更汇总

### 删除

| 文件 | 理由 |
|------|------|
| `apps/kodaclaw-web/src/components/BootstrapPanel.tsx` | Bootstrap Panel 整体去除 |
| `apps/kodaclaw-web/src/hooks/useBootstrap.ts` | 不再需要 bootstrap state 管理 |
| `apps/kodaclaw-web/src/onboarding/steps/LanguageStep.tsx` | 改为自动检测 |

### 组件保留（从 Onboarding 移出，供 Settings Desk 复用）

| 文件 | 后续去向 |
|------|---------|
| `apps/kodaclaw-web/src/onboarding/steps/PersonaStep.tsx` | 迁移到 `src/components/settings/PersonaSelector.tsx`，在 Settings Desk"行为准则"区域复用 |
| `apps/kodaclaw-web/src/onboarding/steps/ChannelStep.tsx` | 迁移到 `src/components/settings/ChannelSetupWizard.tsx`，在 Settings Desk"连接"区域复用 |

### 修改

| 文件 | 变更要点 |
|------|---------|
| `apps/kodaclaw-web/src/App.tsx` | 移除 bootstrap mode 相关 state 和渲染分支；移除 bootstrapBanner 和 contextPanel |
| `apps/kodaclaw-web/src/shell/MainContent.tsx` | 移除 bootstrap mode 分支（只保留 chat / desk 两种路由） |
| `apps/kodaclaw-web/src/shell/AppShell.tsx` | 移除 bootstrap mode props |
| `apps/kodaclaw-web/src/shell/Sidebar.tsx` | 移除 bootstrap mode 渲染分支 |
| `apps/kodaclaw-web/src/shell/app-shell.css` | 移除 `.kc-bootstrap-banner` 样式 |
| `apps/kodaclaw-web/src/i18n/app-strings.ts` | 移除 bootstrap 相关文案；移除 bootstrapNavTitle 等 |
| `apps/kodaclaw-web/src/onboarding/OnboardingShell.tsx` | 精简为 2 步（locale 自动检测 + 模型配置 + 完成页） |
| `src/KodaClaw.Runtime/` | session 构建时注入引导 section（消费 KC-2603/2604） |
| `src/KodaClaw.Gateway/` | 新增 `GET /api/workspace/readiness` 端点 |

### 新增

| 文件 | 说明 |
|------|------|
| `src/KodaClaw.Runtime/WorkspaceReadinessService.cs` | 检测 workspace 文件空缺状态 |

### 不变

- 所有 Desk 组件（ChannelsDesk、AutomationsDesk 等）
- `workspace_protocol_update` 工具实现
- `rotate_session` / `WorkspaceBackupService`
- 模型配置相关（ModelHub、ModelsSettingsDesk）
- `lib/api.ts`、`lib/config.ts`

---

## data-testid 变更

| 旧 | 新 |
|----|----|
| `kc-bootstrap-banner` | 删除 |
| `bootstrap-identity-input` | 删除 |
| `bootstrap-soul-input` | 删除 |
| `bootstrap-user-input` | 删除 |
| `bootstrap-submit` | 删除 |
| `bootstrap-generate-draft` | 删除 |
| `bootstrap-draft-summary` | 删除 |
| `onboarding-step-language` | 删除（自动检测） |
| `onboarding-step-persona` | 删除（移出 Onboarding） |

---

## 验证

```bash
# L0
cd apps/kodaclaw-web && npm run typecheck && npm run build
dotnet build KodaClaw.sln

# 单元测试
npm run test        # bootstrap 相关 spec 需删除或重写

# L4（需 Gateway 运行）
npm run test:e2e    # kc0108-smoke、kc0109-bootstrap 相关 spec 需更新
```

---

## 非目标与风险

### 非目标

- 不支持对话中途更改 Onboarding 选项（Onboarding 完成后不可重触发，设置在 desk 中处理）
- 不重新设计 workspace 文件的图形化编辑界面

### 已知风险

| 风险 | 缓解 |
|------|------|
| Koda 询问质量依赖 prompt 设计，模型不同效果有差异 | system prompt 中给出明确的问法模板；L5 dogfood 验证 |
| 用户可能在引导对话中输入无效内容，导致 workspace 文件质量差 | Koda 总结后先给用户确认再写入 |
| rotate session 后用户对话历史断裂 | 保持 MessageTimeline 滚动位置；新 session 第一条消息说明原因 |
| 部分测试依赖 `kc-bootstrap-banner` 等旧 testid | 统一在本迭代内清理，不留兼容 shim |
