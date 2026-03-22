# Iteration 35 FREEZE — Registry-First Model Routing

冻结日期：2026-03-22

---

## 背景与动机

KodaClaw 的 Model Hub 已有完整的多 endpoint Registry（SQLite + `IModelRegistryRepository`），支持多 provider、capability flags、OS Keychain 密钥存储。但 chat / channel / automation session 仍然走环境变量路径（`KODACLAW_DEFAULT_MODEL` + API key env var），与 Registry 完全割裂。只有 `generate_image` 工具在用 Registry。

两个实际问题：
1. **普通用户在 Models Desk 配置好模型后，chat 并不会用它**，必须同时配环境变量，有 CLI 门槛
2. **API Key 没有普通用户可用的存储入口**：现在要求用户填 env var 名称或 SecretRef 字符串，对非技术用户完全不可用

本迭代目标：**让 Registry 成为所有 session 的唯一路由来源，env var 降级为首次启动自动种子**。

---

## 范围（IN）

### Phase 1（KC-3501）：API Key 直接输入 → PlatformSecretStore

**目标**：普通用户在 Models Desk 填入 API Key → 安全存入 OS Keychain → 不需要知道 SecretRef 格式。

- `CreateModelEndpointRequest` / `UpdateModelEndpointRequest` 新增 `string? ApiKeyValue = null`
- Gateway POST `/api/models` 端点：若 `ApiKeyValue` 非空 → 调用 `ISecretStore.UpsertAsync(new SecretRef("platform", "models", endpointId), apiKeyValue)` → endpoint 的 `ApiKeySecretRef = $"platform:models:{endpointId}"`
- Gateway PUT `/api/models/{id}` 端点：同上，`apiKeyValue` 非空时更新 Keychain，为空时不覆盖现有 key
- GET 端点：`ApiKeySecretRef` 继续返回（前端可据此推断 `hasApiKey = !!apiKeySecretRef || !!apiKeyEnvironmentVariable`），**不回显 key 明文**
- Frontend ModelsSettingsDesk：endpoint 表单新增 "API Key" 密码输入框；若已有 `apiKeySecretRef` 则显示 `已配置`（不显示 placeholder 输入框）；仅输入了新值时才随请求发送 `apiKeyValue`

### Phase 2（KC-3502）：RegistryAwareModelProvider

**目标**：chat / channel / automation session 通过 Registry capability routing 选 endpoint，与 `generate_image` 的路由逻辑完全一致。

- 新增 `RegistryAwareModelProvider : IModelProvider`（位于 `KodaClaw.Runtime`）
  - 注入：`IModelRegistryRepository`, `ISecretStore`, `IRuntimeModelProviderFactory`, `IRuntimeConfigurationResolver`（fallback 用）
  - `StreamAsync` / `CompleteAsync`：
    1. `await registry.ResolveDefaultForAsync(TextChat | ToolCalling)`
    2. 若无 enabled endpoint → fallback 调用基于 `IRuntimeConfigurationResolver` 的 `DynamicModelProvider` 路径
    3. 若有 → `ResolveApiKeyAsync(endpoint)`（同 `OpenAIImageGenerationService` 的逻辑：先 SecretRef → 再 env var）
    4. 构建 `RuntimeConfigurationSnapshot` → `_factory.Create(kind, snapshot)` → call through，request model 字段替换为 `endpoint.ModelId`
  - `ValidateAsync`：同上逻辑，endpoint 存在且 key 可解析则返回 true，否则 fallback
- `ServiceCollectionExtensions.cs`：将 `TryAddSingleton<IModelProvider, DynamicModelProvider>()` 替换为 `TryAddSingleton<IModelProvider, RegistryAwareModelProvider>()`；`DynamicModelProvider` 保留注册为具名实现（内部 fallback 用）

### Phase 3（KC-3503）：env var 首次启动自动 seed

**目标**：已有 .env / env var 配置的用户，升级后无需重新手动录入模型，Gateway 启动时一次性将其 seed 进 Registry。

- 新增 `ModelRegistrySeedService : IHostedService`（位于 `KodaClaw.Gateway`）
- `StartAsync`：
  1. `ListAsync()` 有 endpoint → 直接返回（幂等，不重复 seed）
  2. 调用 `RuntimeConfigurationBootstrap.Resolve(_configuration)` 获取 snapshot
  3. snapshot 无 `DefaultModel` → 返回（用户未配置任何模型，不强制）
  4. 确定 `ModelProviderKind`（`Anthropic`/`OpenAI`，按 model 名前缀推断）
  5. 构建 `ModelEndpoint`，`ApiKeyEnvironmentVariable` 照旧，`Capabilities = TextChat | ToolCalling`，`IsDefault = true`，记 source: `"env_seed"`
  6. `AddAsync(endpoint)`，写 diagnostic 日志
- 在 `GatewayApp.Composition.cs` 注册 `AddHostedService<ModelRegistrySeedService>()`

---

## 非目标（OUT）

- **per-session-type model binding**：channel 用不同模型、automation 用便宜模型 → Iter 36
- **primary / fallback 链**：endpoint A 失败 → 自动试 endpoint B → Iter 36
- **ContextWindowSize 从 Registry 传给 session**：MainSession 仍用 `DefaultContextWindowSize = 128k` → Iter 36
- **Chat Vision 输入**（用户粘贴图片到聊天框）→ Iter 36
- **模型健康检测轮询**（自动禁用失效 endpoint）→ 后续
- **.env 文件废弃**：seed 后 env var 仍可作 fallback，本期只打通路径，不删配置支持

---

## 关键契约变更

| 类型 | 变更 |
|------|------|
| 修改 record | `CreateModelEndpointRequest`：新增 `ApiKeyValue?: string` |
| 修改 record | `UpdateModelEndpointRequest`：新增 `ApiKeyValue?: string` |
| 新增类 | `RegistryAwareModelProvider`（KodaClaw.Runtime） |
| 新增类 | `ModelRegistrySeedService`（KodaClaw.Gateway） |
| 修改注册 | `IModelProvider` singleton 由 `DynamicModelProvider` → `RegistryAwareModelProvider` |

---

## 验证命令

```bash
# L0
dotnet build
npm run typecheck && npm run build

# L1 - RegistryAwareModelProvider 单元测试
dotnet test tests/KodaClaw.UnitTests --filter "RegistryAwareModelProvider"

# L2 - Gateway API + seed 集成测试
dotnet test tests/KodaClaw.IntegrationTests --filter "ModelEndpointApiKey"
dotnet test tests/KodaClaw.IntegrationTests --filter "ModelRegistrySeed"

# L2 全量回归
dotnet test KodaClaw.sln -m:1

# 前端
npm run test -- src/__tests__/models-settings-desk.spec.tsx
```

---

## 受影响文件

| 文件 | 类型 |
|------|------|
| `src/KodaClaw.Contracts/CreateModelEndpointRequest.cs` | 修改 |
| `src/KodaClaw.Contracts/UpdateModelEndpointRequest.cs` | 修改（如存在） |
| `src/KodaClaw.Runtime/RegistryAwareModelProvider.cs` | 新增 |
| `src/KodaClaw.Runtime/ServiceCollectionExtensions.cs` | 修改 |
| `src/KodaClaw.Gateway/ModelRegistrySeedService.cs` | 新增 |
| `src/KodaClaw.Gateway/Composition/GatewayApp.Composition.cs` | 修改 |
| `src/KodaClaw.Gateway/Endpoints/GatewayApp.ModelEndpoints.cs` | 修改 |
| `apps/kodaclaw-web/src/components/ModelsSettingsDesk.tsx` | 修改 |
