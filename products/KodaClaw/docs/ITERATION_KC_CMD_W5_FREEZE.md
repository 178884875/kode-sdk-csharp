# KC-CMD Wave 5 FREEZE — Channel 模型选择命令

> 冻结日期：2026-04-10
> 状态：FROZEN
> 专项前缀：KC-CMD-W5（紧接 KC-CMD Wave 4）

---

## 背景与动机

用户在手机端使用渠道（Telegram/飞书/微信），无法通过 Web 设置切换模型。当需要使用视觉模型或更强推理模型时，必须回到电脑端操作，体验断裂。

本 Wave 通过两个新命令打通手机端模型切换路径：
- `/model`：查看当前 session 使用的模型
- `/model list`：列出所有可用模型（带索引）
- `/new <索引或模型ID>`：扩展现有 `/new` 命令，支持在新 session 指定模型

**明确限制**：仅支持 **新 session 级别**的模型切换。已创建 session 的 per-turn 跨 provider 切换不在本 Wave 范围（provider 实例创建时绑定，mid-session 更换代价过高）。

---

## 目标（用户路径）

```
用户：/model
Koda：当前模型：Kimi K2.5 [kimi-k2.5]  OpenAI兼容

用户：/model list
Koda：
  🤖 可用模型（共 3 个）

  1. Claude Sonnet 4.6  [claude-sonnet-4-6]  Anthropic     ★ 当前
  2. Kimi K2.5          [kimi-k2.5]          OpenAI兼容
  3. GLM-4 Plus         [glm-4-plus]          OpenAI兼容

  发送 /new <序号> 或 /new <模型ID> 切换模型并开始新会话

用户：/new 2
Koda：已开启新会话，使用模型 Kimi K2.5 (kimi-k2.5)。

用户：/new kimi-k2.5
Koda：已开启新会话，使用模型 Kimi K2.5 (kimi-k2.5)。
```

---

## 范围（IN SCOPE）

### 1. 命令解析（`ChannelCommandParser` / `ChannelCommandRegistry`）

**解析规则**（Parser 无需修改，现有 ControlArg 机制已满足）：

| 用户输入 | `ControlKind` | `ControlArg` |
|---------|--------------|-------------|
| `/model` | `Model` | `null` |
| `/model list` | `Model` | `"list"` |
| `/new` | `NewSession` | `null`（原有行为不变）|
| `/new 2` | `NewSession` | `"2"` |
| `/new kimi-k2.5` | `NewSession` | `"kimi-k2.5"` |

**Registry 变更**：
- `ChannelControlCommandKind` 枚举新增 `Model`
- `ChannelCommandRegistry` 新增 `Model` 条目：
  - Key: `/model`，Aliases: `/model /models`
  - Description: "查看当前模型或列出所有可用模型（/model list）"
  - Category: `Session`
- `ChannelCommandRegistry` 中 `NewSession` 条目 Description 更新，注明支持可选 model 参数

### 2. `IChannelSessionService` 接口扩展

新增一个方法，修改一个方法签名：

```csharp
// 新增：返回当前 session 正在使用的模型 ModelId（ModelEndpoint.ModelId 字段）
// session 不存在时返回 null
Task<string?> GetSessionModelAsync(string sessionId, CancellationToken cancellationToken = default);

// 修改：新增可选参数 modelOverride（向后兼容，现有调用方无需改动）
Task<string> RotateSessionAsync(
    ThreadBinding binding,
    string? modelOverride = null,           // ← 新增
    CancellationToken cancellationToken = default);
```

### 3. `ChannelSessionService` 实现

**新增字段**：

```csharp
// 跟踪每个 session 正在使用的 ModelId（ModelEndpoint.ModelId 字段）
private readonly ConcurrentDictionary<string, string> _sessionModels = new(StringComparer.Ordinal);

// 待消费的模型 override，Key 为 binding.Id（ThreadBinding.Id），rotate 后 ensure 时消费并清除
private readonly ConcurrentDictionary<string, string> _pendingModelOverrides = new(StringComparer.Ordinal);
```

**`EnsureChannelSessionAsync` 修改**：

`EnsureChannelSessionAsync` 中有 **两处** `ResolveConfiguredModelAsync` 调用（resume 路径 line 115 和 fresh create 路径 line 193），统一在方法早期（cache miss 后、`contextWindowSize` 计算后、resume/create 分支前）合并为一次：

```csharp
// 插入位置：var contextWindowSize = ... 之后，if (!isSessionTimedOut && ...) 之前
string configuredModel;
if (_pendingModelOverrides.TryRemove(binding.Id, out var pendingOverride))
    configuredModel = pendingOverride;   // 消费并清除，仅消费一次
else
    configuredModel = await ResolveConfiguredModelAsync(cancellationToken);
```

删除原有的两处 `ResolveConfiguredModelAsync` 调用（line 115 的 `var configuredModel = ...` 和 line 193 的 `var initialModel = ...`），全部改用上方 `configuredModel`。

agent 写入 `_agents` 共有三处，每处之后均追加 `_sessionModels` 写入：

```csharp
// 1. resume 成功（原 line 157）
_agents[binding.SessionId] = resumed;
_sessionModels[binding.SessionId] = configuredModel;   // ← 新增

// 2. resume 失败 → fallback fresh create（原 line 178）
_agents[binding.SessionId] = createdAfterFallback;
_sessionModels[binding.SessionId] = configuredModel;   // ← 新增

// 3. 全新 create（原 line 201）
_agents[binding.SessionId] = created;
_sessionModels[binding.SessionId] = configuredModel;   // ← 新增
```

**`RotateSessionAsync` 修改**：

在 `_agents.Remove(binding.SessionId)` 后，同步清除 `_sessionModels`：

```csharp
_sessionModels.TryRemove(binding.SessionId, out _);
```

若 `modelOverride != null`，存入 `_pendingModelOverrides`：

```csharp
if (modelOverride is not null)
    _pendingModelOverrides[binding.Id] = modelOverride;
```

**`GetSessionModelAsync` 实现**：

```csharp
public Task<string?> GetSessionModelAsync(string sessionId, CancellationToken cancellationToken = default)
{
    _sessionModels.TryGetValue(sessionId, out var model);
    return Task.FromResult<string?>(model);
}
```

### 4. `ChannelCommandDispatcher` 扩展

**新增构造函数参数**（可选，与现有 nullable 参数模式一致）：

```csharp
private readonly IModelRegistryRepository? _modelRegistryRepository;

public ChannelCommandDispatcher(
    IChannelSessionService channelSessionService,
    ChannelDeliveryDispatchService deliveryDispatchService,
    IWorkspaceService? workspaceService = null,
    IModelProvider? modelProvider = null,
    ISandboxFactory? sandboxFactory = null,
    IModelRegistryRepository? modelRegistryRepository = null)   // ← 新增
```

`IModelRegistryRepository` 已在 DI 容器中注册（由 `KodaClaw.Gateway` 合成），无需修改 `ServiceCollectionExtensions.cs`。

**switch 新增一个 case（`Model`），修改一个现有 case（`NewSession`，透传 `ControlArg`）**：

```csharp
ChannelControlCommandKind.Model => await HandleModelAsync(sessionId, parsed.ControlArg, cancellationToken),
```

`NewSession` case 改为透传 `ControlArg`：

```csharp
ChannelControlCommandKind.NewSession => await HandleNewSessionAsync(binding, parsed.ControlArg, cancellationToken),
```

**`HandleModelAsync` 实现**：

```csharp
private async Task<string> HandleModelAsync(string sessionId, string? controlArg, CancellationToken ct)
{
    if (controlArg is null)
    {
        // /model：显示当前模型
        var modelId = await _channelSessionService.GetSessionModelAsync(sessionId, ct);
        if (modelId is null)
            return "当前会话尚未创建，将使用默认模型。";

        // 尝试从注册表查出 DisplayName 和 Provider，失败时降级显示裸 ModelId
        if (_modelRegistryRepository is not null)
        {
            var all = await _modelRegistryRepository.ListAsync(ct);
            var endpoint = all.FirstOrDefault(m => m.ModelId == modelId);
            if (endpoint is not null)
            {
                var providerLabel = endpoint.Provider switch
                {
                    ModelProviderKind.Anthropic          => "Anthropic",
                    ModelProviderKind.AnthropicCompatible => "Anthropic兼容",
                    ModelProviderKind.OpenAI             => "OpenAI",
                    ModelProviderKind.OpenAICompatible   => "OpenAI兼容",
                    _                                    => endpoint.Provider.ToString()
                };
                return $"当前模型：{endpoint.DisplayName} [{endpoint.ModelId}]  {providerLabel}";
            }
        }
        return $"当前模型：{modelId}";
    }

    if (controlArg == "list")
    {
        // /model list：列出所有可用模型
        if (_modelRegistryRepository is null)
            return "模型注册表不可用。";

        var models = await _modelRegistryRepository.ListAsync(ct);
        var enabled = models.Where(m => m.Enabled).ToList();
        if (enabled.Count == 0)
            return "暂无可用模型。";

        var currentModelId = await _channelSessionService.GetSessionModelAsync(sessionId, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"🤖 可用模型（共 {enabled.Count} 个）");
        sb.AppendLine();
        for (int i = 0; i < enabled.Count; i++)
        {
            var m = enabled[i];
            var providerLabel = m.Provider switch
            {
                ModelProviderKind.Anthropic          => "Anthropic",
                ModelProviderKind.AnthropicCompatible => "Anthropic兼容",
                ModelProviderKind.OpenAI             => "OpenAI",
                ModelProviderKind.OpenAICompatible   => "OpenAI兼容",
                _                                    => m.Provider.ToString()
            };
            var current = m.ModelId == currentModelId ? "  ★ 当前" : "";
            sb.AppendLine($"  {i + 1}. {m.DisplayName,-20} [{m.ModelId}]  {providerLabel}{current}");
        }
        sb.AppendLine();
        sb.Append("发送 /new <序号> 或 /new <模型ID> 切换模型并开始新会话");
        return sb.ToString();
    }

    return "未知子命令。支持：/model（当前模型）、/model list（所有模型）。";
}
```

**`HandleNewSessionAsync` 修改**（支持 `controlArg`）：

```csharp
private async Task<string> HandleNewSessionAsync(ThreadBinding binding, string? controlArg, CancellationToken ct)
{
    string? modelOverride = null;
    string? modelDisplayName = null;

    if (controlArg is not null && _modelRegistryRepository is not null)
    {
        var models = await _modelRegistryRepository.ListAsync(ct);
        var enabled = models.Where(m => m.Enabled).ToList();

        ModelEndpoint? resolved = null;

        if (int.TryParse(controlArg, out var idx) && idx >= 1 && idx <= enabled.Count)
        {
            resolved = enabled[idx - 1];  // 1-based → 0-based
        }
        else
        {
            // 按 ModelId 精确匹配（忽略大小写），再按 DisplayName
            resolved = enabled.FirstOrDefault(m =>
                string.Equals(m.ModelId, controlArg, StringComparison.OrdinalIgnoreCase))
                ?? enabled.FirstOrDefault(m =>
                string.Equals(m.DisplayName, controlArg, StringComparison.OrdinalIgnoreCase));
        }

        if (resolved is null)
            return $"未找到模型 "{controlArg}"，请发送 /model list 查看可用列表。";

        modelOverride = resolved.ModelId;
        modelDisplayName = resolved.DisplayName;
    }
    else if (controlArg is not null && _modelRegistryRepository is null)
    {
        return "模型注册表不可用，无法按指定模型创建会话。";
    }

    await _channelSessionService.RotateSessionAsync(binding, modelOverride, ct);

    return modelDisplayName is not null
        ? $"已开启新会话，使用模型 {modelDisplayName} ({modelOverride})。"
        : "已开启新会话。";
}
```

### 5. `ServiceCollectionExtensions` 变更

无需修改。`IModelRegistryRepository` 已在 DI 容器注册，ASP.NET DI 自动将其注入 `ChannelCommandDispatcher` 的可选参数。

---

## 排序与索引规则

`IModelRegistryRepository.ListAsync()` 固定排序：`IsDefault DESC, CreatedAt ASC`（`JsonModelRegistryRepository` 实现确认）。

因此：
- **索引 1 始终是默认模型**
- 索引在同次 session 内稳定（用户先 `/model list` 看到列表，立刻 `/new 2`，列表不会在此期间改变）
- 仅展示 `Enabled == true` 的模型

---

## 范围（OUT OF SCOPE）

- per-turn 跨 provider 模型切换（Provider 实例在 session 创建时绑定，mid-session 切换代价过高）
- 模型别名系统（workspace 级别的 `vision`/`deep` 快捷名，单独评估）
- per-channel binding 默认模型配置（单独评估）
- 自动多模态检测（发图自动切 vision 模型，单独评估）

---

## 验证策略

| 层级 | 内容 | 工具 |
|------|------|------|
| L0 | 编译 0 错 0 警告 | `dotnet build KodaClaw.sln` |
| L1 | 新增单元测试（见下） | xUnit，无 I/O |
| L5 | Dogfood：发 `/model list` 看到模型列表；发 `/new 2` 后确认新 session 使用对应模型 | 真实 Gateway + Telegram |

**新增单元测试（L1）**：

- `ChannelCommandParserTests` 扩展：
  - `/model` → `ControlKind=Model, ControlArg=null`
  - `/model list` → `ControlKind=Model, ControlArg="list"`
  - `/new 2` → `ControlKind=NewSession, ControlArg="2"`
  - `/new kimi-k2.5` → `ControlKind=NewSession, ControlArg="kimi-k2.5"`

- `ChannelCommandRegistryTests` 扩展：`Model` 枚举值存在，别名 `/model /models` 均能 Find

- `ChannelCommandDispatcherTests` 扩展：
  - `HandleModelAsync(null)` → 调 `GetSessionModelAsync`，返回含 modelId 的文本
  - `HandleModelAsync(null)` session 不存在 → 返回提示文本
  - `HandleModelAsync("list")` → 调 `ListAsync`，格式化含索引的列表
  - `HandleModelAsync("list")` 当前模型标 ★
  - `HandleNewSessionAsync("2")` → 调 `ListAsync`，`RotateSessionAsync` 带正确 modelId
  - `HandleNewSessionAsync("kimi-k2.5")` → 按 ModelId 匹配成功后调 `RotateSessionAsync`
  - `HandleNewSessionAsync("99")` index 越界 → 返回错误文本，不调 `RotateSessionAsync`
  - `HandleNewSessionAsync("不存在")` → 返回错误文本

- `ChannelSessionServiceTests` 扩展：
  - `RotateSessionAsync(binding, "kimi-k2.5")` 后 `EnsureChannelSessionAsync` 使用 override 模型而非 `ResolveConfiguredModelAsync` 结果
  - override 被消费后清除，第二次 `EnsureChannelSessionAsync` 回到默认模型
  - `GetSessionModelAsync` 在 `EnsureChannelSessionAsync` 后返回正确 modelId
  - `RotateSessionAsync` 后 `GetSessionModelAsync` 返回 null（旧 session 已清除）
