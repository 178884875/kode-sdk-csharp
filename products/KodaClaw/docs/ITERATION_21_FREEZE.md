# Iteration 21 FREEZE：Context Intelligence & Session Lifecycle

## 冻结日期
2026-03-21

## 动机

Iteration 20 建立了记忆流水线的四条核心路径，但留下了三处架构债和一个重要的 UX 缺口：

1. **压缩模型硬编码**：`CompressionModel = "claude-haiku-4-5-20251001"` 写死在 Options 里，与产品定位矛盾——普通用户不理解"压缩模型"是什么，更无法判断哪个模型适合自己的 provider/key；当用户使用 OpenAI 时 haiku 根本不存在；Context 阈值是绝对值（80k/50k），与模型实际 context window 大小无关，对不同 window 大小的模型行为不一致
2. **Daily log 永不清理**：Nightly Memory Consolidation 执行后，`memory/YYYY-MM-DD.md` 的内容已合并进 MEMORY.md，但文件本身从未删除；长期使用后 `workspace/memory/` 堆积数百个孤儿文件，system prompt 加载路径也一直重复注入旧日期文件
3. **MEMORY.md 无上限**：Nightly Consolidation 是覆盖写，但提示词没有保留策略约束，MEMORY.md 随时间无限增长，最终撑满 session 启动时的 system prompt 字符预算
4. **用户无法主动重置会话**：主会话没有"开启新对话"入口，用户必须通过手动删除 session 文件才能清空上下文；这是最常见的用户期望之一

---

## 用户可感知的变化

- 使用任何 provider（Anthropic / OpenAI）、任何模型，上下文压缩都能可靠工作，不会因模型名不对报错
- workspace 磁盘占用可预期：memory/ 目录只保留最近 7 天的日记文件
- MEMORY.md 始终维持在一个合理大小，不会因为长期使用而撑爆 system prompt
- 用户可以点击按钮开启全新对话（清空 session 历史），而不影响 workspace 记忆文件

---

## 范围

### 在范围内

| KC | 描述 | 规模 |
|----|------|------|
| KC-2101 | 压缩模型 & Context 阈值架构重构 | M |
| KC-2102 | Daily log 清理：Heartbeat 模板补 `fs_rm` 指令 | XS |
| KC-2103 | MEMORY.md 保留策略：Nightly Consolidation 加约束 | XS |
| KC-2104 | Session Rotate API + Web UI 入口 | S |

### 不在范围内

- Channel session 时效策略（超过 N 天放弃 resume）→ Iteration 22
- SUMMARY.md 在 session 有完整历史时跳过加载 → Iteration 22
- Automation 跨次运行状态文件（`LAST_RUN.md`）→ Iteration 22
- Inbox / Canvas artifacts 预加载到 system prompt → Iteration 22
- ModelHub 完整的 model capability metadata 体系 → 专项迭代

---

## 关键设计决定

### KC-2101：压缩模型架构

| 问题 | 决定 | 理由 |
|------|------|------|
| 压缩模型如何选择 | 不设 `CompressionModel`，由 SDK 使用 session 自身模型 | 永远不会有 provider 不匹配或 Key 权限问题；compression 触发极少，成本差异可忽略 |
| Context 阈值如何表达 | 改为比例参数：`ContextCompressionTriggerRatio`（默认 0.75）和 `ContextCompressionTargetRatio`（默认 0.40） | 不同模型 window 大小不同，比例语义稳定；实际 token 数在运行时从 ModelHub `ContextWindowSize` 计算 |
| ModelHub 如何提供 window size | `ModelEndpointConfig` 加 `ContextWindowSize`（默认 128_000） | 现代主流模型窗口均 ≥128k；用户添加自定义模型时可填写；比维护完整 model registry 轻量 |
| `CompressionModel` 字段处理 | 从所有三个 Options 类中删除，SDK 默认行为即使用主模型 | 消除用户配置负担；不暴露无法理解的配置项 |

**运行时计算逻辑**（在各 Service 的 `CreateAgentConfig` 中）：

```
modelConfig = ModelHub.GetEndpointConfig(model)
window      = modelConfig.ContextWindowSize  // 默认 128_000
MaxTokens         = (int)(window * TriggerRatio)  // 0.75 × 128k = 96_000
CompressToTokens  = (int)(window * TargetRatio)   // 0.40 × 128k = 51_200
Context = new ContextManagerOptions { MaxTokens, CompressToTokens }
// CompressionModel 不设 → SDK 用 session model
```

### KC-2102：Daily log 清理

Heartbeat Nightly Consolidation section 在现有 `workspace_protocol_update` 调用之后，追加 `fs_rm` 清理指令：
```
After writing MEMORY.md, delete today's daily log file using fs_rm to prevent accumulation.
Keep: only today and yesterday's files if they contain unflushed entries.
```

同时补充 7 天保留策略说明：整合日志文件时，7 天前的 `memory/YYYY-MM-DD.md` 应一并删除。

### KC-2103：MEMORY.md 保留策略

Heartbeat Nightly Consolidation 提示词补充：
```
Retention rule: keep high-signal facts from the past 90 days. Remove entries that are:
- superseded by newer facts
- transient (task-specific, no longer relevant)
- exact duplicates
Target size: under 200 lines.
```

### KC-2104：Session Rotate API

**API 设计**：
```
POST /api/sessions/rotate
Body: { "reason": "user_initiated" }   // optional
Response: { "previousSessionId": "...", "ok": true }
```

语义：
- 删除当前 main session 的 SDK store（等效于清空对话历史）
- **不影响** workspace 文件（MEMORY.md / daily logs 等保留）
- 下一次 `/api/chat/stream` 请求时自动创建新 session

**Web UI 入口**：
- V2 shell 的 GlobalRail 或 Chat 顶部 header 加"新对话"按钮（`data-testid="new-session-button"`）
- 点击后弹出确认对话框，确认后调用 `POST /api/sessions/rotate`，前端清空消息列表

---

## 契约变更

### Options 类字段变更（破坏性，仅内部）

删除字段（三个 Options 类）：
```csharp
// 删除
public string CompressionModel { get; init; }
public int ContextMaxTokens { get; init; }
public int ContextCompressToTokens { get; init; }
```

新增字段（三个 Options 类）：
```csharp
public double ContextCompressionTriggerRatio { get; init; } = 0.75;
public double ContextCompressionTargetRatio  { get; init; } = 0.40;
```

### ModelHub 新增字段（非破坏性）

`ModelEndpointConfig` 新增：
```csharp
public int ContextWindowSize { get; init; } = 128_000;
```

### 新 Gateway 端点

```
POST /api/sessions/rotate → { ok: true, previousSessionId: string }
```

### Heartbeat 模板变更（行为变更）

- Nightly Consolidation：执行后删除已整合的 daily log 文件（`fs_rm`）
- Nightly Consolidation：新增 MEMORY.md 大小约束（90 天保留规则，目标 <200 行）

---

## 验收标准

1. 使用 OpenAI 模型（非 Anthropic）时，ContextManager 能正常压缩，不报 haiku 相关错误
2. 新创建 workspace 的 HEARTBEAT.md Nightly Consolidation 提示词包含 `fs_rm` 清理指令
3. Nightly Consolidation 提示词包含 90 天保留规则描述
4. `POST /api/sessions/rotate` 成功后，再次发起 chat 请求创建全新 session
5. Web UI 存在"新对话"按钮，点击后确认并触发 rotate
6. `dotnet test KodaClaw.sln -m:1` 全量通过

---

## 关键文件汇总

| 文件 | 操作 |
|------|------|
| `src/KodaClaw.Runtime/MainSessionOptions.cs` | 修改（删旧字段，加比例字段） |
| `src/KodaClaw.Runtime/ChannelSessionOptions.cs` | 修改（删旧字段，加比例字段） |
| `src/KodaClaw.Runtime/AutomationSessionOptions.cs` | 修改（删旧字段，加比例字段） |
| `src/KodaClaw.Runtime/MainSessionService.cs` | 修改（用 ModelHub + 比例计算 ContextManager 参数） |
| `src/KodaClaw.Runtime/ChannelSessionService.cs` | 修改（同上） |
| `src/KodaClaw.Runtime/AutomationSessionService.cs` | 修改（同上） |
| `src/KodaClaw.ModelHub/ModelEndpointConfig.cs` | 修改（加 ContextWindowSize） |
| `src/KodaClaw.Workspace/DefaultWorkspaceTemplates.cs` | 修改（Nightly Consolidation 加 fs_rm + 保留规则） |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.SessionEndpoints.cs` | 修改（加 rotate 端点） |
| `apps/kodaclaw-web/src/` | 修改（加新对话按钮） |
| 相关 Unit/Integration/Contract 测试 | 新增/修改 |
