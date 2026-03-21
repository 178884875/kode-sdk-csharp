# Iteration 23 FREEZE：引导程序基础数据层

## 冻结日期
2026-03-21

## 动机

Iteration 24 将实现首次使用引导程序的完整 UX，但它依赖三块目前不存在的基础数据层：

1. **模型预设库缺失**：新用户添加模型时面对空白表单——不知道填什么模型名、不知道 context window 多大、不知道 base URL 是什么、不知道哪个模型适合自己。主流模型的参数是公开已知的，完全可以内置。
2. **API Key 连通性测试缺失**：用户填完 Key 后必须等到真正发起对话才知道 Key 是否有效，错误定位成本高，引导程序中这是不可接受的体验断层。
3. **Persona 模板库缺失**：Bootstrap 对话要求用户自己决定 Koda 的人格风格，但大多数用户对此毫无概念，空白起点会造成焦虑和流失。内置一批模板可以把"选择"变成"确认"。
4. **引导进度持久化缺失**：用户中途关闭窗口后重新打开，目前无任何机制记录"做到了第几步"，只能重头来过。

这三块能力在引导程序之外也有独立价值（例如模型预设可以改善 Models Settings Desk 的配置体验），因此单独作为一个迭代交付，不与 UX 耦合。

---

## 用户可感知的变化

- Models Settings Desk 新增"从预设选择"入口，主流模型开箱即用，不再需要手动填写模型名和参数
- 添加模型 API Key 后立刻可以"测试连接"，几秒内知道是否配置正确
- 引导程序（Iter 24）中可以看到 6 种 Koda 人格模板，直接选择而无需从空白开始描述
- 中途中断的引导程序下次打开时从断点续接

---

## 范围

### 在范围内

| KC | 描述 | 规模 |
|----|------|------|
| KC-2301 | 模型预设库：内置主流模型配置 + `GET /api/models/presets` | S |
| KC-2302 | API Key 连通性测试：`POST /api/models/test-connection` | S |
| KC-2303 | Persona 模板库：6 个内置模板 + `GET /api/workspace/persona-presets` | S |
| KC-2304 | Onboarding 状态持久化：进度数据模型 + CRUD API | S |

### 不在范围内

- 引导程序 UX（Step 流程、前端页面）→ Iter 24
- 模型预设的远程更新机制（从 URL 拉取最新列表）→ 后续专项
- 用户自定义 Persona 模板（导出/分享）→ 后续专项
- Persona 模板社区市场 → 后续专项
- Models Settings Desk 的"从预设选择"UI 集成 → Iter 24 一并做

---

## 架构决策

### KC-2301：模型预设库

**数据来源**：内置静态列表（随版本发布），存为嵌入资源文件 `Resources/model-presets.json`，不写入 SQLite，不依赖网络。

**预设字段**：
```csharp
public sealed record ModelPreset(
    string PresetId,            // "anthropic-sonnet-4-6"
    string DisplayName,         // "Claude Sonnet 4.6"
    string Provider,            // "Anthropic" | "OpenAI" | "DeepSeek" | "Google" | "Ollama"
    ModelProviderKind ProviderKind,
    string ModelId,             // "claude-sonnet-4-6"
    string? BaseUrl,            // null = 使用 Provider 默认值；Ollama 填 "http://localhost:11434/v1"
    int ContextWindowSize,      // token 数，影响 ContextManager 阈值计算
    PresetTier Tier,            // Recommended | Advanced | Fast | Reasoning | Local
    string Description,         // 一句话：适合什么场景
    string? CostHint,           // "~$3/M input tokens"（可选，仅参考）
    bool RequiresBaseUrl        // Ollama / 自定义 endpoint 需要用户填写
);

public enum PresetTier { Recommended, Advanced, Fast, Reasoning, Local }
```

**内置预设列表（首批）**：

| DisplayName | Provider | ModelId | ContextWindowSize | Tier |
|---|---|---|---|---|
| Claude Sonnet 4.6 | Anthropic | claude-sonnet-4-6 | 200_000 | Recommended |
| Claude Opus 4.6 | Anthropic | claude-opus-4-6 | 200_000 | Advanced |
| Claude Haiku 4.5 | Anthropic | claude-haiku-4-5-20251001 | 200_000 | Fast |
| GPT-4o | OpenAI | gpt-4o | 128_000 | Recommended |
| GPT-4o mini | OpenAI | gpt-4o-mini | 128_000 | Fast |
| o3-mini | OpenAI | o3-mini | 200_000 | Reasoning |
| DeepSeek V3 | DeepSeek | deepseek-chat | 64_000 | Recommended |
| DeepSeek R1 | DeepSeek | deepseek-reasoner | 64_000 | Reasoning |
| Gemini 2.0 Flash | Google | gemini-2.0-flash | 1_000_000 | Fast |
| Gemini 1.5 Pro | Google | gemini-1.5-pro | 2_000_000 | Advanced |
| Ollama（本地） | Ollama | （用户填写） | 32_000 | Local |
| 自定义 | Custom | （用户填写） | 128_000 | — |

**API**：
```
GET /api/models/presets
→ ModelPreset[]（全部，按 Provider 分组）

GET /api/models/presets/{presetId}
→ ModelPreset
```

**与现有 `ModelEndpoint` 的关系**：预设是**只读的配置模板**，用户选择一个预设后，前端用预设数据**预填** `CreateModelEndpointRequest` 表单，用户填入 API Key 后正常走现有 `POST /api/models` 流程创建 endpoint。预设本身不写入数据库，不与 endpoint 绑定。

### KC-2302：API Key 连通性测试

**目的**：用户填完 API Key 后立刻验证，不等到第一次对话才报错。

**实现**：发送一个最小化 API 调用（single-token completion，`max_tokens=1`），只验证认证和网络连通，不消耗有意义的 token（成本 < $0.0001）。

**新端点**：
```
POST /api/models/test-connection
Body: {
  "presetId": "anthropic-sonnet-4-6",   // 使用预设（提供 provider / baseUrl）
  "modelId": "claude-sonnet-4-6",        // 或直接指定
  "baseUrl": null,
  "apiKey": "sk-ant-..."
}
→ {
  "ok": true,
  "latencyMs": 320,
  "modelId": "claude-sonnet-4-6",
  "error": null
}
```

**安全注意**：
- API Key **不持久化**，只在请求生命周期内使用
- 端点需要 Gateway token 认证（防止本机其他程序探测）
- 响应不反射 API Key 内容

**实现位置**：`src/KodaClaw.Gateway/Endpoints/GatewayApp.ModelEndpoints.cs`，注入 `IModelProviderFactory`（已有）动态构建临时 provider 实例做测试调用。

### KC-2303：Persona 模板库

**数据来源**：内置嵌入资源 `Resources/persona-presets.json`，不写入 SQLite。

**数据结构**：
```csharp
public sealed record PersonaPreset(
    string PresetId,          // "minimalist-executor"
    string DisplayName,       // "极简执行者"
    string TagLine,           // "只给结论，不废话"
    string Description,       // 2-3 句：适合谁、什么特点
    string[] Tags,            // ["简洁", "高效", "技术用户"]
    string SoulMarkdown,      // 完整的 SOUL.md 内容
    string IdentityMarkdown   // 建议的 IDENTITY.md 起始内容（用户可覆盖）
);
```

**6 个内置模板及设计说明**：

| ID | 显示名 | 标语 | 适合谁 | 挑选原因 |
|---|---|---|---|---|
| `minimalist-executor` | 极简执行者 | 只给结论，不废话 | 开发者、工程师、忙碌管理者 | KodaClaw 核心早期用户群，最反感啰嗦 AI |
| `deep-analyst` | 深度分析师 | 展示推理，不急下结论 | 研究人员、学者、重要决策者 | 覆盖"帮我想清楚问题"场景，与极简形成最大对比 |
| `creative-partner` | 创意伙伴 | 发散思维，不怕天马行空 | 设计师、写作者、营销人 | 覆盖创意行业，需要 Koda 语气轻松、鼓励发散 |
| `patient-guide` | 耐心向导 | 用例子解释，确认你理解了再继续 | 非技术用户、学习新领域的人 | 降低普通用户门槛，KodaClaw 产品目标明确覆盖 |
| `pragmatic-advisor` | 务实顾问 | 一切落到可执行的下一步 | 创业者、产品经理、商业决策者 | 覆盖职场/商业场景，关注"怎么做"而非"为什么" |
| `balanced-default` | 均衡默认 | 适合所有人的安全起点 | 不确定自己风格的新用户 | 必须有——避免强制选择造成焦虑，先用再调整 |

**SOUL.md 内容原则**（每个模板）：
- 约 200-400 字，Markdown 格式
- 包含：核心行为准则（5-8 条）、沟通风格、处理不确定时的策略
- 不包含用户个人信息（那是 USER.md 的职责）
- 不写死 Koda 的名字（由 IDENTITY.md 决定）

**API**：
```
GET /api/workspace/persona-presets
→ PersonaPreset[]

GET /api/workspace/persona-presets/{presetId}
→ PersonaPreset
```

**应用预设**：前端选择 persona 后，调用已有的 `workspace_protocol_update(target=soul, content=preset.SoulMarkdown)` 工具（在 Bootstrap 流程中），或在引导程序专用 API 中直接写入文件——两种路径均不新增写入接口，复用现有 `WorkspaceService`。

### KC-2304：Onboarding 状态持久化

**存储**：`{workspaceRoot}/config/onboarding.json`，通过 `WorkspaceService` 读写，不用 SQLite。

**数据结构**：
```csharp
public sealed record OnboardingState(
    bool IsCompleted,
    string? CurrentStepId,          // "language" | "model" | "persona" | "channel" | "done"
    string[] CompletedSteps,
    string? SelectedLanguage,        // "zh-CN" | "en-US"
    string? SelectedPresetId,        // 选择的模型预设 ID
    string? SelectedPersonaPresetId, // 选择的 persona 预设 ID
    bool ChannelStepSkipped,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt
);
```

**Gateway 启动行为**：`GatewayApp` 启动时检查 `onboarding.json`：
- 不存在或 `IsCompleted=false` → 响应头加 `X-KodaClaw-Onboarding-Pending: true`
- 前端读取此 header，决定是否进入引导流程（Iter 24 实现）

**API**：
```
GET  /api/onboarding/state          → OnboardingState（不存在时返回默认空状态）
PUT  /api/onboarding/state          → 更新当前步骤和已完成步骤
POST /api/onboarding/complete       → 标记完成（写 IsCompleted=true, CompletedAt）
POST /api/onboarding/reset          → 重置为未完成（用于"重新引导"场景）
```

---

## 契约变更

### 新增类型（`KodaClaw.Contracts`）

```csharp
// 新文件
ModelPreset.cs
PresetTier.cs
ModelConnectionTestRequest.cs
ModelConnectionTestResponse.cs
PersonaPreset.cs
OnboardingState.cs
```

### 新增端点

```
GET  /api/models/presets
GET  /api/models/presets/{presetId}
POST /api/models/test-connection

GET  /api/workspace/persona-presets
GET  /api/workspace/persona-presets/{presetId}

GET  /api/onboarding/state
PUT  /api/onboarding/state
POST /api/onboarding/complete
POST /api/onboarding/reset
```

### 新增前端类型（`contracts.ts`）

```typescript
export interface ModelPreset { ... }
export type PresetTier = "Recommended" | "Advanced" | "Fast" | "Reasoning" | "Local"
export interface ModelConnectionTestRequest { ... }
export interface ModelConnectionTestResponse { ok: boolean; latencyMs: number; error?: string }
export interface PersonaPreset { ... }
export interface OnboardingState { ... }
```

---

## 验收标准

1. `GET /api/models/presets` 返回 ≥ 10 个预设，包含 Anthropic / OpenAI / DeepSeek / Google / Ollama 五类
2. `POST /api/models/test-connection` 传入有效 Anthropic Key + 预设 ID，返回 `{ ok: true, latencyMs: N }`
3. `POST /api/models/test-connection` 传入无效 Key，返回 `{ ok: false, error: "authentication_error" }`（不暴露 Key 内容）
4. `GET /api/workspace/persona-presets` 返回恰好 6 个模板，每个模板 `SoulMarkdown` 长度在 200-400 字之间
5. `GET /api/onboarding/state` 在 workspace 首次初始化后返回 `{ isCompleted: false, currentStepId: null }`
6. 连续调用 `PUT /api/onboarding/state`（推进步骤）+ 重启 Gateway + `GET /api/onboarding/state`，进度正确持久化
7. `dotnet test KodaClaw.sln -m:1` 全量通过
8. `cd apps/kodaclaw-web && npm run typecheck && npm run build`

---

## 关键文件汇总

| 文件 | 操作 |
|------|------|
| `src/KodaClaw.Contracts/ModelPreset.cs` | 新增 |
| `src/KodaClaw.Contracts/PresetTier.cs` | 新增 |
| `src/KodaClaw.Contracts/ModelConnectionTestRequest.cs` | 新增 |
| `src/KodaClaw.Contracts/ModelConnectionTestResponse.cs` | 新增 |
| `src/KodaClaw.Contracts/PersonaPreset.cs` | 新增 |
| `src/KodaClaw.Contracts/OnboardingState.cs` | 新增 |
| `src/KodaClaw.Gateway/ModelPresetService.cs` | 新增（加载嵌入资源） |
| `src/KodaClaw.Gateway/PersonaPresetService.cs` | 新增（加载嵌入资源） |
| `src/KodaClaw.Gateway/ModelConnectionTestService.cs` | 新增（测试调用） |
| `src/KodaClaw.Workspace/OnboardingStateService.cs` | 新增（读写 config/onboarding.json） |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.ModelEndpoints.cs` | 修改（加 presets + test-connection） |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.WorkspaceEndpoints.cs` | 修改（加 persona-presets） |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.OnboardingEndpoints.cs` | 新增 |
| `src/KodaClaw.Gateway/Resources/model-presets.json` | 新增（嵌入资源） |
| `src/KodaClaw.Gateway/Resources/persona-presets.json` | 新增（嵌入资源） |
| `apps/kodaclaw-web/src/types/contracts.ts` | 修改（新增类型） |
| `apps/kodaclaw-web/src/lib/api.ts` | 修改（新增 API 函数） |
| `tests/KodaClaw.ContractTests/Models/ModelPresetContractTests.cs` | 新增 |
| `tests/KodaClaw.ContractTests/Workspace/PersonaPresetContractTests.cs` | 新增 |
| `tests/KodaClaw.IntegrationTests/Gateway/ModelPresetsApiIntegrationTests.cs` | 新增 |
| `tests/KodaClaw.IntegrationTests/Gateway/ModelConnectionTestIntegrationTests.cs` | 新增 |
| `tests/KodaClaw.IntegrationTests/Gateway/OnboardingStateIntegrationTests.cs` | 新增 |

---

## Wave 计划

| Wave | 内容 |
|------|------|
| Wave 1 | KC-2301（模型预设库）+ KC-2302（连通性测试）— 后端数据 + API |
| Wave 2 | KC-2303（Persona 模板库）— 后端数据 + API + SOUL.md 内容编写 |
| Wave 3 | KC-2304（Onboarding 状态持久化）— 数据模型 + API + 前端类型同步 |
