# Iteration 66 FREEZE — Onboarding UX 优化 + GLM Coding Plan 接入

> 冻结日期：2026-04-05
> 状态：FROZEN

---

## 目标

优化首次启动配置页（ModelStep）的用户体验，解决当前"标准 API 用户"与"Coding Plan 用户"混杂在同一列表导致的选择困惑，同时让 GLM Coding Plan 用户能顺畅完成配置。

**用户路径（标准 API）**：打开 onboarding → 选择 [标准 API] → Provider tabs 同现有逻辑 → 选模型 → 填 API Key → 测试 → 保存。

**用户路径（GLM Coding Plan）**：打开 onboarding → 选择 [Coding Plan] → 自动跳入 GLM provider → 看到 glm-5.1 / glm-5-turbo → 填 Coding Plan Key → 测试 → 保存（endpoint 自动设为 AnthropicCompatible）。

---

## 范围（IN SCOPE）

- **`model-presets.json`**：新增可选字段 `accessMode: "coding-plan"`，打标 GLM Coding Plan 专属 preset（`glm-5.1`、`glm-5-turbo`、`glm-5-anthropic`）
- **`contracts.ts`**：`ModelPreset` 接口新增 `accessMode?: 'api' | 'coding-plan'`
- **`ModelStep.tsx`**：
  - 顶部新增模式切换 pill：`[标准 API]` / `[Coding Plan]`
  - 标准 API 模式：过滤掉所有 `accessMode === 'coding-plan'` 的 preset（现有 8 个 Provider tab 逻辑不变）
  - Coding Plan 模式：Provider tabs 第一期只显示 `智谱 GLM`，展示 preset glm-5.1 / glm-5-turbo（隐藏 glm-5-anthropic 冗余项）
  - Coding Plan 模式下：`API 协议` 选择器不渲染；API Key label 改为 "Coding Plan Key"；hint 改为"在智谱订阅页获取"（带链接）
  - Save 逻辑：Coding Plan 模式下 `provider` 直接取 `preset.provider`（即 `AnthropicCompatible`），不经过 `protocol` 状态

---

## 非目标（OUT OF SCOPE）

- MiniMax Token Plan：预留 Provider tab 入口但不实现（二期）
- Kimi / MiMo Coding Plan：当前无此类计划，不处理
- Settings Desk 同步支持 Coding Plan 模式：Settings 里继续沿用现有进阶配置方式
- 后端 API 变更：无，复用现有 `createModelEndpoint` + `AnthropicCompatible` 路径

---

## 关键设计决策

### 1. `accessMode` 用字段打标，不靠 provider 推断

`xiaomi-mimo-v2-pro-anthropic` 也是 `AnthropicCompatible`，但不是 Coding Plan；不能用 `provider` 作为判断依据，必须显式字段。

### 2. glm-5-anthropic 从 Coding Plan 模型列表隐藏

`glm-5-anthropic` 与 `glm-5`（OpenAI-compat）是同一模型的两个协议变体，Coding Plan 列表里只展示升级型号（glm-5.1 / glm-5-turbo），减少冗余。

### 3. Coding Plan 模式下固定 provider，不出协议选择器

路径（Coding Plan）已经决定协议（Anthropic-compatible），协议选择器带来的认知负担大于灵活性收益，直接隐藏。

### 4. 二期 MiniMax 入口预留不实现

Coding Plan Provider tabs 里保留 `MiniMax` tab（disabled 或灰显"即将支持"），保持扩展点，但本期不写任何逻辑。

---

## 变更文件清单

| 文件 | 变更 |
|------|------|
| `src/KodaClaw.Gateway/Resources/model-presets.json` | glm-5.1、glm-5-turbo、glm-5-anthropic 加 `"accessMode": "coding-plan"` |
| `apps/kodaclaw-web/src/types/contracts.ts` | `ModelPreset` 加 `accessMode?: 'api' \| 'coding-plan'` |
| `apps/kodaclaw-web/src/onboarding/steps/ModelStep.tsx` | 模式切换 pill + 过滤逻辑 + Coding Plan 路径 + Save 逻辑适配 |
| `apps/kodaclaw-web/src/onboarding/onboarding.css` | 模式切换 pill 样式（复用 `ob-provider-tab` 基础样式即可） |

---

## 验证命令

```bash
npm run typecheck                                    # L0: 无类型错误
dotnet build KodaClaw.sln                           # L0: 0 错 0 警告
dotnet test KodaClaw.sln -m:1                       # L1/L2: 全量回归（preset JSON 契约测试应通过）
```

L5 Dogfood：
- 标准 API 模式：GLM 下只看到 glm-5 / glm-4.7，不出现 glm-5.1
- Coding Plan 模式：GLM 下看到 glm-5.1 / glm-5-turbo；填 Coding Plan Key → 测试通过 → 保存 → endpoint provider 为 AnthropicCompatible
