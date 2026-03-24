# Iteration 43 FREEZE — Model Hub 质量补全

**冻结日期**：2026-03-23
**范围摘要**：在现有 ModelEndpoint 体系上补全 `maxOutputTokens` 与 `isReasoning` 两个缺失字段，同步修复 3 个已知 Bug（SecretRef 前缀不匹配、首个模型不自动成为默认、删除默认模型无防守），并完善前端表单 UX（字段顺序、contextWindowSize 补全、高级选项折叠）。

---

## 新功能范围

### 字段扩展：maxOutputTokens + isReasoning

**为什么**：`contextWindowSize`（模型能"看到"多少 token）与 `maxOutputTokens`（每次回复最多生成多少 token）是两个独立概念，当前系统混淆了两者。Runtime 在构建模型请求时从未设置 `max_tokens` 参数，导致推理模型（DeepSeek R1、o3-mini）在 automation 场景下输出不可控。`isReasoning` 标记推理模型，允许 Runtime 跳过 tool_use 注入（R1 不支持 function calling）。

**新增字段**：
- `maxOutputTokens: int`（默认 8192）
- `isReasoning: bool`（默认 false）

**影响层**：
```
model-presets.json
  → ModelPreset C# record
  → ModelEndpoint C# record
  → CreateModelEndpointRequest / UpdateModelEndpointRequest
  → SQLite 迁移（新增两列，带默认值）
  → RegistryAwareModelProvider（构建请求时注入 MaxTokens）
  → contracts.ts（ModelPreset + ModelEndpoint + 两个 Request 类型）
  → ModelDraft + 表单 UI
```

**非目标**：
- 不添加 Google provider 支持（后端 provider 路由尚未实现）
- 不改变 provider 层级结构（保持扁平 endpoint 列表）
- 不添加 cost 精细计费字段（costHint 字符串已够用）
- 不实现 extended thinking / streaming thinking tokens（另立专项）
- `isReasoning` 在本迭代仅用于跳过 tool_use 注入，不做其他特殊处理

**合理的 maxOutputTokens 参考值**（写入 presets）：
| 模型 | maxOutputTokens | isReasoning |
|------|----------------|-------------|
| Claude Sonnet/Opus/Haiku 4.x | 8192 | false |
| GPT-4o / GPT-4o-mini | 16384 | false |
| o3-mini | 65536 | true |
| DeepSeek V3 | 8192 | false |
| DeepSeek R1 | 8192 | true |
| Ollama | 4096 | false |
| Custom | 8192 | false |

---

## Bug Fix 范围

### KC-BUG-001：SecretRef 前缀不匹配
- **症状**：编辑已配置 API Key 的模型端点时，"已配置"徽章不显示，始终展示密码输入框
- **根因**：前端检查 `apiKeySecretRef?.startsWith("platform:models:")` ，但后端生成 `keychain:models:{id}`
- **修复**：前端改为 `startsWith("keychain:models:")`

### KC-BUG-002：首个模型不自动成为默认
- **症状**：用户创建第一个模型端点后，需手动点"设为默认"才能启动 session
- **根因**：`GatewayApp.ModelEndpoints.cs` POST handler 硬编码 `IsDefault: false`
- **修复**：创建时查询现有 endpoint 数量，若为 0 则设 `IsDefault: true`

### KC-BUG-003：删除默认模型无防守
- **症状**：可删除唯一的默认模型，导致系统无默认模型，session 启动失败
- **根因**：DELETE handler 未检查 `endpoint.IsDefault`
- **修复**：删除前检查；若是默认模型且无其他可用 endpoint，返回 409；若有其他启用的 endpoint，自动切换默认后再删

---

## 前端 UX 补全范围

### 表单字段顺序调整
当前顺序（预设在 Provider 前）→ 正确顺序：
```
Provider → 预设（可选，按 Provider 过滤）→ 显示名称 → Model ID
→ Base URL → API Key → [高级选项折叠：API Key 环境变量] → 已启用
→ 上下文窗口 / 最大输出 token → 推理模型 → 支持能力
```

### contextWindowSize 补全
`ModelEndpoint` 前端类型补 `contextWindowSize?: number`，`ModelDraft` 和 `toUpdateRequest` 补对应字段，表单加数字输入（只读展示预设值，可手动覆盖）。

### API Key 环境变量折叠到"高级选项"
用可展开的 disclosure 区块包裹，默认折叠，减少普通用户认知负担。

---

## 验收标准

1. 创建一个 DeepSeek R1 端点（isReasoning=true），session 启动时工具不被注入
2. 创建一个 GPT-4o 端点（maxOutputTokens=16384），SDK 实际调用带 `max_tokens=16384`
3. 首个模型创建后自动成为默认，不需要手动操作
4. 尝试删除唯一默认模型 → 返回 409；存在其他启用端点时 → 自动切换默认后成功删除
5. 编辑已有 API Key 的端点 → 显示"已配置"徽章
6. L0 dotnet build 0 错；npm typecheck 通过；L2 集成测试全绿
