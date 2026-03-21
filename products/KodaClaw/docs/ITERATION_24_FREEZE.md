# Iteration 24 FREEZE：首次使用引导程序

## 冻结日期
2026-03-21

## 前置依赖
Iteration 23（模型预设库 / Persona 模板库 / Onboarding 状态持久化）必须完成。

## 动机

KodaClaw 目前没有任何首次启动引导：新用户打开后直接面对空白主界面，不知道先做什么。三个最常见的流失点：

1. **模型没配** → 发消息报错，用户不知道要先去 Models Settings 页添加 API Key
2. **Koda 没有人格** → Bootstrap 对话让用户"描述你想要什么风格的助手"，大多数用户不知道怎么回答
3. **不知道核心能力** → 用了几天才发现 Channels、Automations、Canvas 的存在

引导程序的目标不是"教会用户所有功能"，而是：**用最少的摩擦完成能让 KodaClaw 跑起来的最小配置**，同时给用户一个清晰的起点感。

---

## 用户可感知的变化

- 首次打开 KodaClaw，进入引导程序而不是空白主界面
- 4 个步骤完成核心配置：语言 → 模型 + API Key → Koda 人格 → （可选）渠道绑定
- 每一步都可以跳过，引导程序中途关闭后下次从断点续接
- 引导完成后看到清晰的"接下来可以做什么"建议
- 之后可以从设置入口重新触发引导程序（换 workspace、重置人格等场景）
- Models Settings Desk 新增"从预设选择"入口（同步消费 Iter 23 数据）

---

## 范围

### 在范围内

| KC | 描述 | 规模 |
|----|------|------|
| KC-2401 | 引导程序入口与路由：首次启动检测、进入/跳过/续接逻辑 | S |
| KC-2402 | Step 0 — 语言选择 | XS |
| KC-2403 | Step 1 — 模型选择 + API Key + 连通性测试 | M |
| KC-2404 | Step 2 — Persona 选择 | S |
| KC-2405 | Step 3 — Telegram 绑定向导（可跳过） | M |
| KC-2406 | Step 4 — 完成页 + 行动建议 | S |
| KC-2407 | Models Settings Desk "从预设选择"集成 | S |

### 不在范围内

- 引导程序内的完整 Bootstrap 对话（已有独立 Bootstrap 流程，引导完成后自然进入）
- 多 workspace 切换时自动触发引导 → 后续专项
- 引导程序的 A/B 测试或分析埋点 → 后续专项
- 其他渠道（Webhook、QQ 等）的绑定向导 → 后续专项（本期只做 Telegram）
- Persona 模板的自定义编辑（选完后的深度调整通过 Bootstrap 对话完成）

---

## 架构决策

### KC-2401：引导程序入口与路由

**触发条件**：Gateway 启动时在 `X-KodaClaw-Onboarding-Pending: true` 响应头中标记（Iter 23 已实现）。前端 `App.tsx` 在初始化时调用 `GET /api/onboarding/state`：
- `isCompleted = false` → 渲染 `<OnboardingShell>` 替代普通壳层
- `isCompleted = true` → 进入正常主界面

**OnboardingShell**：独立全屏组件，不复用 GlobalRail / MainStage / ContextRail，避免引导中出现尚未配置的功能入口造成混乱。

**续接逻辑**：进入 `OnboardingShell` 时读取 `state.currentStepId`，直接跳到对应步骤，不重播已完成步骤。

**跳过整体引导**：引导程序右上角提供"跳过，我自己配置"链接，点击后调用 `POST /api/onboarding/complete`（标记完成），直接进入主界面。跳过后可从 Settings 页重新触发（`POST /api/onboarding/reset` + 刷新）。

**步骤顺序**：
```
Step 0: language    → Step 1: model    → Step 2: persona
  → Step 3: channel（可跳过）→ Step 4: done
```

每完成一步调用 `PUT /api/onboarding/state` 更新进度，保证随时关闭都能续接。

### KC-2402：Step 0 — 语言选择

**UI**：全屏居中，两个大卡片——"中文"和"English"，点击即选，没有确认按钮（即点即进）。

**行为**：
- 更新 `localStorage["kodaclaw.locale"]`（复用现有 locale 机制）
- 调用 `PUT /api/onboarding/state { currentStepId: "model", completedSteps: ["language"], selectedLanguage: "zh-CN" }`
- 自动跳入 Step 1

**说明**：语言选择影响后续所有引导文案和 Koda 的默认回复语言（通过 AGENTS.md 模板中的语言指令体现）。

### KC-2403：Step 1 — 模型选择 + API Key + 连通性测试

这是引导程序中最重要、最复杂的一步，分三个子阶段。

**子阶段 A — Provider 选择**：
- 展示 5 个 Provider 卡片（Anthropic / OpenAI / DeepSeek / Google / Ollama/自定义）
- 每个 Provider 卡片显示 logo、名称、一句话特点（如"Claude 是 KodaClaw 推荐的默认选择"）
- 选中 Provider 后展开该 Provider 的模型列表

**子阶段 B — 模型选择**：
- 展示该 Provider 下的预设模型列表（来自 `GET /api/models/presets`，按 Tier 过滤）
- 每个模型卡片显示：名称、Tier 标签（推荐/高级/快速/推理）、context window、一句话描述、估算费用参考
- 标注 "**推荐**" 的模型（每个 Provider 一个）
- 底部有"使用自定义模型名称"展开选项（输入 modelId、baseUrl）
- Ollama 固定展示为"本地模型"，需要用户填写模型名和确认 `http://localhost:11434` 可达

**子阶段 C — API Key 输入 + 连通性测试**：
- 每个 Provider 显示对应的 Key 格式提示（如 `sk-ant-...`）
- 旁边有"如何获取 Key"按钮，展开后显示步骤说明 + 直达链接（Anthropic Console / OpenAI Platform / DeepSeek Platform）
- 费用提醒：小字显示"我们会发送一个 1 token 的测试请求，费用不足 $0.0001"
- 填完 Key 后显示"测试连接"按钮（`data-testid="test-connection-btn"`）
- 调用 `POST /api/models/test-connection`，展示结果：
  - ✅ 连接成功（延迟 XXms）→ 激活"下一步"按钮
  - ❌ 认证失败 / 网络错误 → 显示具体原因 + "查看帮助"
- 连接成功后调用 `POST /api/models`（现有创建 endpoint 流程）持久化配置，并设为 default

**`data-testid`**：`provider-card-{id}`, `model-card-{id}`, `apikey-input`, `test-connection-btn`, `connection-result`

### KC-2404：Step 2 — Persona 选择

**UI**：6 张 Persona 卡片横向排列（宽屏）或 2×3 网格（窄屏）。

每张卡片显示：
- 名称（大字）
- 标语（中字，斜体）
- 2-3 句描述
- 代表性 tags（如 `#简洁` `#技术用户`）

交互：
- 点击卡片 → 选中高亮，卡片展开显示 SOUL.md 预览（前 5 条准则）
- 选中后显示"使用这个风格"按钮
- 底部有"让 Koda 自己来了解我"选项 → 跳过此步骤直接进入 Bootstrap 对话（不写入 SOUL.md，由 Bootstrap 流程决定）

**行为**：选中后调用 `WorkspaceService.WriteFileAsync(soulPath, preset.SoulMarkdown)` 写入 SOUL.md（引导程序专用 API `POST /api/onboarding/apply-persona`，body: `{ presetId }`）。

**新增 API**：
```
POST /api/onboarding/apply-persona
Body: { "presetId": "minimalist-executor" }
→ { ok: true }
```
内部直接写 SOUL.md，无需走 Bootstrap 对话。

**`data-testid`**：`persona-card-{id}`, `persona-preview`, `apply-persona-btn`, `skip-to-bootstrap-btn`

### KC-2405：Step 3 — Telegram 绑定向导（可跳过）

**定位**：明确标注为"可选——你可以之后随时在 Channels 页面绑定"，降低跳过的心理障碍。

**向导步骤**（内嵌在引导程序中，不跳转）：

```
① 说明：Telegram 绑定让你可以通过手机直接和 Koda 对话
   → [开始绑定] [跳过这一步]

② 创建 Bot（带截图说明）：
   1. 打开 Telegram，搜索 @BotFather
   2. 发送 /newbot
   3. 起一个名字（如 "My Koda Bot"）
   4. BotFather 会给你一个 Token：1234567890:ABC...
   → [Token 已复制，下一步]

③ 粘贴 Token：
   [输入框] → [测试连接]（调用 Telegram getMe API 验证 token 有效）
   ✅ Bot 名称：My Koda Bot → [继续]
   ❌ Token 无效 → 重试提示

④ 设置 Delivery Rule：
   三个选项卡片（推荐高亮"草稿审批"）：
   - 草稿审批：Koda 会先展示回复，你确认后再发（推荐新手）
   - 需要审批：每条消息都需要你在 Web 审批
   - 自动发送：Koda 直接发送回复（适合熟练用户）
   → [完成绑定]

⑤ 绑定成功确认：
   "发一条消息给你的 Bot 试试！"
```

**实现**：调用已有的 `POST /api/channels/accounts`（KC-2203，Iter 22 实现），以及 Telegram `getMe` API 验证 token 有效性。

`getMe` 验证在前端调用 `POST /api/models/test-connection` 类似思路，但针对 Telegram——新增：
```
POST /api/channels/test-telegram-token
Body: { "botToken": "..." }
→ { ok: true, botName: "My Koda Bot", botUsername: "my_koda_bot" }
```

**`data-testid`**：`telegram-token-input`, `test-telegram-btn`, `delivery-rule-select`, `finish-telegram-btn`, `skip-telegram-btn`

### KC-2406：Step 4 — 完成页 + 行动建议

**UI**：
- 大标题："Koda 已就绪 ✓"（或英文 "Koda is ready"）
- 配置摘要小卡片（已选模型 / 已选人格 / 是否绑定 Telegram）
- "接下来可以做什么"行动建议列表（3 项，根据已完成步骤动态生成）：

```
// 始终显示
💬 和 Koda 打个招呼，告诉它你在做什么项目

// 如果绑定了 Telegram
📱 打开 Telegram，给 @your_bot 发一条消息

// 如果没绑定 Telegram
📱 在 Channels 页绑定 Telegram，随时随地和 Koda 对话

// 始终显示
⚡ Nightly Memory 自动化已开启——每晚 Koda 会整理今天的记忆
```

- 大按钮："开始使用 KodaClaw"→ 调用 `POST /api/onboarding/complete`，进入主界面

**进入主界面的落点**：直接进入主对话（`mainDesk = "chat"`），而不是某个 Settings 页，让用户立刻能和 Koda 说话。

### KC-2407：Models Settings Desk "从预设选择"集成

在 ModelsSettingsDesk 的"添加模型"表单中，现有空白表单上方加一个"从预设选择"区域：

- 下拉选择 Provider → 展示对应预设列表（mini 版，只显示名称 + Tier）
- 选中预设后自动填充 modelId / baseUrl / contextWindowSize 字段
- 用户仍可手动修改任何字段
- 添加 API Key 后显示"测试连接"按钮（复用 KC-2403 的连通性测试 UI）

这使得引导程序之外也能享受预设带来的便利，不只服务于首次使用场景。

**`data-testid`**：`preset-provider-select`, `preset-model-select`, `test-connection-btn`（复用）

---

## 契约变更

### 新增端点

```
POST /api/onboarding/apply-persona     → 写入 SOUL.md
POST /api/channels/test-telegram-token → 验证 Telegram bot token
```

### 新增前端类型（`contracts.ts`）

```typescript
// 已在 Iter 23 定义的类型在此直接消费，无新增
// 仅 API helper 函数新增
```

### 新增前端组件

```
src/onboarding/OnboardingShell.tsx      # 全屏容器，管理步骤状态
src/onboarding/steps/LanguageStep.tsx
src/onboarding/steps/ModelStep.tsx
src/onboarding/steps/PersonaStep.tsx
src/onboarding/steps/ChannelStep.tsx
src/onboarding/steps/DoneStep.tsx
src/onboarding/components/ProviderCard.tsx
src/onboarding/components/ModelCard.tsx
src/onboarding/components/PersonaCard.tsx
src/onboarding/components/ConnectionTestResult.tsx
src/onboarding/onboarding.css
```

---

## 验收标准

1. 全新 workspace（无 `config/onboarding.json`）启动后，前端自动进入引导程序而非主界面
2. Step 1：选择 Anthropic + Claude Sonnet 4.6 → 填入有效 API Key → 点击"测试连接" → 显示"✅ 连接成功"→ 可继续
3. Step 1：填入无效 API Key → 显示"❌ 认证失败，请检查 Key 是否正确"
4. Step 2：选择"极简执行者" → 点击"使用这个风格" → SOUL.md 被写入对应模板内容
5. Step 3：填入有效 Telegram bot token → 显示 Bot 名称 → 选择 Delivery Rule → 完成绑定 → Channels 页可见新账号
6. Step 4：点击"开始使用"→ 进入主界面主对话页面
7. 引导程序进行到 Step 2 时关闭浏览器 → 重新打开 → 从 Step 2 续接
8. 已完成引导的用户，Settings 页有"重新引导"入口，点击后重置并重新进入引导程序
9. KC-2407：ModelsSettingsDesk 添加模型表单有"从预设选择"下拉，选中后自动填充字段
10. `dotnet test KodaClaw.sln -m:1` 全量通过
11. `cd apps/kodaclaw-web && npm run typecheck && npm test && npm run build`

---

## 关键文件汇总

| 文件 | 操作 |
|------|------|
| `apps/kodaclaw-web/src/App.tsx` | 修改（onboarding 状态检测 + OnboardingShell 路由） |
| `apps/kodaclaw-web/src/onboarding/OnboardingShell.tsx` | 新增 |
| `apps/kodaclaw-web/src/onboarding/steps/*.tsx` | 新增（5 个步骤组件） |
| `apps/kodaclaw-web/src/onboarding/components/*.tsx` | 新增（4 个 UI 组件） |
| `apps/kodaclaw-web/src/onboarding/onboarding.css` | 新增 |
| `apps/kodaclaw-web/src/lib/api.ts` | 修改（新增 onboarding / persona / telegram-test API 函数） |
| `apps/kodaclaw-web/src/components/ModelsSettingsDesk.tsx` | 修改（加预设选择入口） |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.OnboardingEndpoints.cs` | 修改（加 apply-persona） |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.ChannelEndpoints.cs` | 修改（加 test-telegram-token） |
| `tests/KodaClaw.IntegrationTests/Gateway/OnboardingFlowIntegrationTests.cs` | 新增 |
| `apps/kodaclaw-web/tests/kc2401-onboarding.spec.ts` | 新增（Playwright E2E） |

---

## Wave 计划

| Wave | 内容 |
|------|------|
| Wave 1 | KC-2401（入口路由）+ KC-2402（语言选择）+ KC-2403（模型步骤）|
| Wave 2 | KC-2404（Persona 选择）+ `POST /api/onboarding/apply-persona` |
| Wave 3 | KC-2405（Telegram 向导）+ `POST /api/channels/test-telegram-token` |
| Wave 4 | KC-2406（完成页）+ KC-2407（Models Settings 预设集成）+ E2E 测试收口 |
