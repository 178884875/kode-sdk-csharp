# KC-BUG-W5 — Channel 模型选择跨重启持久化

> 创建日期：2026-04-10
> 状态：FROZEN（待审查）
> 关联：KC-CMD Wave 5（`ITERATION_KC_CMD_W5_FREEZE.md`）

---

## 问题描述

Wave 5 的模型选择实现完全依赖内存数据结构，Gateway 重启后丢失：

| 数据结构 | Key | 影响 |
|----------|-----|------|
| `_pendingModelOverrides` | `binding.Id` | 用户 `/new kimi-k2.5` 后重启，下条消息静默回退到默认模型 |
| `_sessionModels` | `binding.SessionId` | `/model` 命令重启后返回 null，直到下条消息触发 `EnsureChannelSessionAsync` |

### 场景 A（主要问题）

```
用户：/new kimi-k2.5
  → RotateSessionAsync 写入 _pendingModelOverrides[binding.Id] = "kimi-k2.5"
  → Gateway 重启，_pendingModelOverrides 清空
  → 用户发消息
  → EnsureChannelSessionAsync：_pendingModelOverrides 无记录 → ResolveConfiguredModelAsync → 默认模型
  → 用户无感知地用了错误模型
```

### 场景 B（功能性 Bug，同样严重）

用户正在用 kimi-k2.5 聊天，Gateway 重启，用户发普通消息：

```
重启后，binding 从 DB 加载：
  PendingModelOverride = null（上次 EnsureChannelSessionAsync 已清零）
  ActiveModelId = "kimi-k2.5"（上次 EnsureChannelSessionAsync 写入）

EnsureChannelSessionAsync（cache miss）：
  → _pendingModelOverrides：空
  → binding.PendingModelOverride：null
  → configuredModel = ResolveConfiguredModelAsync() = "claude-sonnet-4-6"  ✗
  → ResumeFromStoreAsync(overrides: { Model = "claude-sonnet-4-6" })       ✗
  → UpsertAsync: { ActiveModelId = "claude-sonnet-4-6" }  ← 把正确值也覆盖了  ✗
```

场景 B 不是显示问题——resume 使用了错误模型，且永久污染 DB 中的 `ActiveModelId`。

---

## 根因

`_pendingModelOverrides` 和 `_sessionModels` 均为内存 `ConcurrentDictionary`，
没有对应的持久化路径。`ThreadBinding` 已经持久化到 JSON 文件，是天然的载体。

---

## 修复范围（IN SCOPE）

### 1. `ThreadBinding` record 新增两个可选字段

文件：`src/KodaClaw.Contracts/Channels/ThreadBinding.cs`

在现有最后一个可选参数 `DeliveryModeOverride` 之后追加：

```csharp
public sealed record ThreadBinding(
    string Id,
    ChannelConnectorKind ConnectorKind,
    string AccountId,
    string ExternalThreadId,
    ChannelThreadType ThreadType,
    string SessionId,
    SessionKind SessionKind,
    ChannelIdentity ChannelIdentity,
    string PolicyId,
    string DeliveryRuleId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastInboundAt = null,
    DateTimeOffset? LastOutboundAt = null,
    string? LastMessagePreview = null,
    DeliveryMode? DeliveryModeOverride = null,
    string? PendingModelOverride = null,   // ← 新增：下次 EnsureChannelSessionAsync 消费一次后清零
    string? ActiveModelId = null           // ← 新增：当前 session 正在使用的模型 ID，重启后可读
);
```

**向后兼容性**：两个字段均有默认值 `null`。旧 JSON 文件反序列化时缺失字段自动为 `null`，
无需迁移脚本。

---

### 2. `IThreadBindingRepository` 新增 `GetBySessionIdAsync`

文件：`src/KodaClaw.Contracts/Channels/IThreadBindingRepository.cs`

```csharp
/// <summary>按 SessionId 查找 binding。用于 GetSessionModelAsync 的重启后回查路径。</summary>
Task<ThreadBinding?> GetBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default);
```

---

### 3. `JsonThreadBindingRepository` 实现 `GetBySessionIdAsync`

文件：`src/KodaClaw.Storage.Json/Repositories/JsonThreadBindingRepository.cs`

`ChannelQuery` 已有 `SessionId` 过滤条件，直接复用 `ListAsync`：

```csharp
public async Task<ThreadBinding?> GetBySessionIdAsync(
    string sessionId, CancellationToken cancellationToken = default)
{
    var results = await ListAsync(
        new ChannelQuery(SessionId: sessionId, Limit: 1),
        cancellationToken);
    return results.FirstOrDefault();
}
```

---

### 4. `ChannelSessionService` — 四处修改

文件：`src/KodaClaw.Runtime/Sessions/ChannelSessionService.cs`

#### 4a. `EnsureChannelSessionAsync`：`configuredModel` 计算拆分为两步

**为什么不能统一计算**：`PendingModelOverride` 用于新建 session（`/new X` 后），
`ActiveModelId` 用于恢复 session（重启后 resume）。两者语义不同：
- Resume 路径需要 `ActiveModelId` 兜底，还原上次使用的模型
- Fresh create 路径（含 Group 超时）**不能**用 `ActiveModelId`，否则超时后新 session 会错误继承旧 session 的模型

**Step 1**：统一计算 `explicitOverride`（两条路径共用），替换现有整段 `configuredModel` 计算：

```csharp
// 替换现有：
// string configuredModel;
// if (_pendingModelOverrides.TryRemove(...)) ... else ...

string? explicitOverride = null;
if (_pendingModelOverrides.TryRemove(binding.Id, out var memOverride))
    explicitOverride = memOverride;                    // 同进程快路径
else if (binding.PendingModelOverride is not null)
    explicitOverride = binding.PendingModelOverride;   // 重启后从 DB 读取
```

**Step 2**：`configuredModel` 在各自分支内声明，Resume 路径多一个 `ActiveModelId` 兜底。

Resume 分支顶部（`if (!isSessionTimedOut && store.ExistsAsync(...))` 之后第一行）：

```csharp
var configuredModel = explicitOverride
    ?? binding.ActiveModelId                        // 重启后恢复上次模型
    ?? await ResolveConfiguredModelAsync(cancellationToken);
```

Fresh create 路径顶部（现有 `var initialModel = ...` 所在位置）：

```csharp
var configuredModel = explicitOverride
    ?? await ResolveConfiguredModelAsync(cancellationToken);
// 不使用 binding.ActiveModelId：避免超时后带入旧 session 的模型
```

> Fallback create（resume 失败 → catch 块内创建）使用 resume 分支的同一个 `configuredModel` 变量，语义正确——失败后仍尝试用用户上次的模型。

#### 4b. `EnsureChannelSessionAsync`：三处 `_agents` 写入后追加 binding upsert

三处位置均一致。以 resume 成功为例，现有代码：

```csharp
_agents[binding.SessionId] = resumed;
_sessionModels[binding.SessionId] = configuredModel;
```

改为：

```csharp
_agents[binding.SessionId] = resumed;
_sessionModels[binding.SessionId] = configuredModel;
if (_threadBindingRepository is not null)
    await _threadBindingRepository.UpsertAsync(
        binding with
        {
            ActiveModelId = configuredModel,
            PendingModelOverride = null,    // 无论从哪条路径消费，一律清零
            UpdatedAt = DateTimeOffset.UtcNow
        },
        cancellationToken);
```

**fallback create（resume 失败）** 和 **fresh create** 两处相同处理，共三处。

#### 4c. `RotateSessionAsync`：同步内存状态 + upsert binding

**内存同步**（修复 Wave 5 原有 bug）：当前代码 `if (modelOverride is not null)` 在
`modelOverride = null` 时不清除内存中可能存在的旧 override，导致用户连发
`/new kimi-k2.5` 再 `/new`（plain）后，内存旧值仍会污染下次 session 的模型选择。

在现有 `if (modelOverride is not null) _pendingModelOverrides[binding.Id] = modelOverride;` 处替换为：

```csharp
// 先无条件清除旧值，再按需写入新值，保证内存与 DB 始终一致
_pendingModelOverrides.TryRemove(binding.Id, out _);
if (modelOverride is not null)
    _pendingModelOverrides[binding.Id] = modelOverride;
```

**DB 持久化**：将现有的：

```csharp
if (_threadBindingRepository is not null)
{
    await _threadBindingRepository.UpsertAsync(
        binding with { SessionId = newSessionId, UpdatedAt = DateTimeOffset.UtcNow },
        cancellationToken);
}
```

替换为：

```csharp
if (_threadBindingRepository is not null)
{
    await _threadBindingRepository.UpsertAsync(
        binding with
        {
            SessionId = newSessionId,
            PendingModelOverride = modelOverride,  // null 表示无 override，覆盖旧值
            ActiveModelId = null,                  // 旧 session 已蒸发，新 session 尚未创建
            UpdatedAt = DateTimeOffset.UtcNow
        },
        cancellationToken);
}
```

#### 4d. `GetSessionModelAsync`：`_sessionModels` 缺失时从 DB 回查

将现有的：

```csharp
public Task<string?> GetSessionModelAsync(string sessionId, CancellationToken cancellationToken = default)
{
    _sessionModels.TryGetValue(sessionId, out var model);
    return Task.FromResult<string?>(model);
}
```

替换为：

```csharp
public async Task<string?> GetSessionModelAsync(
    string sessionId, CancellationToken cancellationToken = default)
{
    if (_sessionModels.TryGetValue(sessionId, out var model))
        return model;

    // 重启后 _sessionModels 为空时，从持久化 binding 回查
    if (_threadBindingRepository is not null)
    {
        var binding = await _threadBindingRepository.GetBySessionIdAsync(sessionId, cancellationToken);
        return binding?.ActiveModelId;
    }

    return null;
}
```

---

### 5. `IChannelSessionService` — 无变更

接口签名不变，调用方无需修改。

---

### 6. `ServiceCollectionExtensions` — 无变更

---

## 完整数据流（修复后）

### 场景 A：`/new kimi-k2.5` → 重启 → 下条消息

```
RotateSessionAsync(binding, "kimi-k2.5")
  → _pendingModelOverrides[binding.Id] = "kimi-k2.5"                          （内存）
  → UpsertAsync({ SessionId=新ID, PendingModelOverride="kimi-k2.5", ActiveModelId=null })  （持久化）

Gateway 重启
  → _pendingModelOverrides 清空
  → JSON 保留 PendingModelOverride = "kimi-k2.5"

用户发消息
  → Orchestrator 加载 binding（PendingModelOverride="kimi-k2.5", ActiveModelId=null）
  → EnsureChannelSessionAsync
    → explicitOverride: TryRemove=false，binding.PendingModelOverride="kimi-k2.5"
      → explicitOverride = "kimi-k2.5"
    → store 不存在（新 SessionId）→ fresh create 路径
      → configuredModel = explicitOverride = "kimi-k2.5"  ✓
    → CreateAsync(Model="kimi-k2.5") 成功
    → UpsertAsync({ ActiveModelId="kimi-k2.5", PendingModelOverride=null })  ✓
```

### 场景 B：kimi-k2.5 session 活跃 → 重启 → 普通消息

```
重启前 binding 状态：
  PendingModelOverride = null（EnsureChannelSessionAsync 创建时已清零）
  ActiveModelId = "kimi-k2.5"（EnsureChannelSessionAsync 创建时写入）

Gateway 重启
  → _agents, _sessionModels, _pendingModelOverrides 全部清空

用户发普通消息
  → Orchestrator 加载 binding（PendingModelOverride=null, ActiveModelId="kimi-k2.5"）
  → EnsureChannelSessionAsync（cache miss）
    → explicitOverride: TryRemove=false，PendingModelOverride=null
      → explicitOverride = null
    → store 存在（旧 SessionId 未轮转）→ resume 路径
      → configuredModel = null ?? "kimi-k2.5" ?? ResolveConfiguredModelAsync()
                       = "kimi-k2.5"  ✓
    → ResumeFromStoreAsync(Model="kimi-k2.5")  ✓
    → UpsertAsync({ ActiveModelId="kimi-k2.5", PendingModelOverride=null })  ✓
```

### 场景 C：Group session 超时 → 自动新建 session

```
binding 状态（超时前）：
  PendingModelOverride = null
  ActiveModelId = "kimi-k2.5"（上个 session 写入）

用户发消息，isSessionTimedOut = true（Group，超过 N 天）
  → EnsureChannelSessionAsync（cache miss）
    → explicitOverride = null
    → isSessionTimedOut = true → 跳过 resume 分支 → fresh create 路径
      → configuredModel = null ?? ResolveConfiguredModelAsync()
                       = "claude-sonnet-4-6"（默认模型）  ✓
    （ActiveModelId 不参与 fresh create 计算，超时后不继承旧模型）
    → CreateAsync(Model="claude-sonnet-4-6")
    → UpsertAsync({ ActiveModelId="claude-sonnet-4-6", PendingModelOverride=null })  ✓
```

### 场景 D：`/model` 重启后在首条消息前查询

```
binding.ActiveModelId = "kimi-k2.5"（持久化）

GetSessionModelAsync("session-xyz")
  → _sessionModels 无记录（重启后清空）
  → GetBySessionIdAsync("session-xyz") → 读 JSON → binding.ActiveModelId = "kimi-k2.5"
  → 返回 "kimi-k2.5"  ✓
```

---

## 注意事项

1. **`_pendingModelOverrides` 内存路径保留**：同进程内正常流程无需 DB 读（`TryRemove` 先命中），
   DB 路径仅作重启后兜底，不影响热路径性能。

2. **三处 `UpsertAsync` 追加**：每次 session 创建/恢复时额外一次 JSON 文件写，
   `EnsureChannelSessionAsync` 的 cache-miss 路径本身就存在多次 IO，此开销可忽略。

3. **`GetBySessionIdAsync` 扫描开销**：调用 `ListAsync(SessionId: sessionId, Limit: 1)`，
   底层 `ScanDirectoryAsync` 扫描全部 binding 文件。正常用户的 binding 数量极少（十级别），
   且该方法仅在 `_sessionModels` miss 时（重启后首次 `/model`）触发，不在热路径上。

4. **`_threadBindingRepository is null` 保护**：所有新增 DB 路径均有 null 检查，
   无 repo 时降级到原有行为，不引入新的 NullReferenceException 风险。

5. **并发安全**：`EnsureChannelSessionAsync` 通过 `_sessionLocks[sessionId]` 串行化，
   同一 session 不会并发创建。不同 session 并发使用各自的 binding，无交叉。

---

## 范围（OUT OF SCOPE）

- `IChannelSessionService` 接口签名变更
- SQLite 迁移（`ThreadBinding` 为 JSON 文件存储，无 SQL schema）
- `_sessionModels` 内存字典的持久化（已通过 `ActiveModelId` 覆盖）

---

## 验证策略

| 层级 | 内容 | 工具 |
|------|------|------|
| L0 | 编译 0 错 0 警告 | `dotnet build KodaClaw.sln` |
| L1 | 新增单元测试（见下） | xUnit，无 I/O |
| L3 | `ThreadBinding` JSON 序列化/反序列化含新字段 | ContractTests Golden |
| L5 | Dogfood：`/new kimi-k2.5` → 重启 Gateway → 发消息 → 确认模型正确 | 真实 Gateway + Telegram |

**新增 L1 单元测试**：

- `JsonThreadBindingRepositoryTests` 扩展：
  - `GetBySessionIdAsync` 返回匹配 SessionId 的 binding
  - `GetBySessionIdAsync` 返回 null 当 SessionId 不存在

- `ThreadBindingContractTests`（新建）：
  - 旧 JSON（无 `PendingModelOverride`/`ActiveModelId` 字段）反序列化后两字段为 null
  - 含新字段的 JSON 正确序列化/反序列化，值不丢失

- `ChannelSessionServiceModelTests` 扩展：
  - `GetSessionModelAsync` 在 `_sessionModels` 无记录时调用 `GetBySessionIdAsync` 回查
  - `RotateSessionAsync` 调用 `UpsertAsync` 时携带正确的 `PendingModelOverride` 和 `ActiveModelId = null`
  - `/new kimi-k2.5` 后立即 `/new`（plain）：`_pendingModelOverrides` 内存旧值被清除，下次 session 使用默认模型
