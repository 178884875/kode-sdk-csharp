# Iteration 54 FREEZE — agentskills.io 标准对齐 + `allowed-tools` 功能化

**日期**：2026-03-25
**类型**：优化/重构（中改，跨 SDK + KodaClaw 双仓）
**范围**：`Kode.Agent.Sdk` / `KodaClaw.Gateway` / `KodaClaw.Contracts` / `kodaclaw-web`

---

## 背景与动机

迭代 52 引入了扩展 frontmatter（`kind/version/tags/requires`），用于 SkillsDesk UI 展示。但这 4 个字段均为非标准顶层字段，不符合 [agentskills.io](https://agentskills.io/) 规范，在 `skills-ref validate` 下会报未知字段错误。

同时，`requires` 字段目前**只有 UI 展示效果，没有实际功能**：`SkillActivation.ToolsGranted` 在激活时已被填充（`SkillsManager.cs:82`），但 `Agent.cs` 从未将其传递给 `PermissionManager`，工具权限列表在整个会话中保持静态。

**两个问题可一次性解决**：

1. 将非标准字段迁移到标准位置，`allowed-tools` 替换 `requires`，`kind/version/tags` 移入 `metadata:` 块
2. 实现 `allowed-tools` 的真实语义：skill 激活时自动扩展 session 工具白名单

### 架构决策：SDK 是标准解析的唯一事实来源

KodaClaw 目前存在两套并行解析器：

- **SDK `SkillsLoader.ParseSkillFile()`**：运行时解析，用于 `SkillsManager`、`Agent` 功能链路
- **KodaClaw `SkillFrontmatterParser.Parse()`**：Gateway UI 解析，用于 `/api/skills` 展示

这两套解析器同读一个 SKILL.md，互相不知道对方的存在。标准字段的 bug（`allowed-tools` 连字符不识别、按逗号而非空格分隔）只修 KodaClaw 层无法解决运行时问题。

**本迭代决策**：
- SDK 修复所有标准字段解析，并暴露 `public static SkillMetadata ParseFrontmatter(string content)` 纯方法
- KodaClaw `SkillFrontmatterParser` 改为调用 SDK 方法取基础字段，只在其上提取 `kind/tags/version`（KodaClaw 专属 UI 字段，存于 `metadata:` 块），不再自维护 YAML 解析逻辑

---

## 范围（IN SCOPE）

### KC-5401：SDK `SkillsLoader` 修复 + 暴露静态解析方法

#### Bug 修复（`SkillsLoader.cs:ParseSkillFile`）

**问题 1：`allowed-tools`（连字符）未被识别**

```csharp
// 现有代码（缺失连字符形式）
case "allowedtools":
case "allowed_tools":
    allowedTools = value.Split(',', ...);  // 逗号分隔 ← 错误
    break;

// 修复后
case "allowed-tools":   // ← 新增，标准格式
case "allowedtools":    // 保留向后兼容
case "allowed_tools":   // 保留向后兼容
    allowedTools = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    break;
```

标准格式：`allowed-tools: workspace_read workspace_write`（空格分隔）。

**问题 2：`metadata:` 嵌套块未解析**

`SkillMetadata.Metadata`（`IReadOnlyDictionary<string, JsonElement>?`）字段始终为 null。需在 `ParseSkillFile` 中添加嵌套块检测：

```csharp
// 检测进入 metadata: 块
if (key == "metadata" && string.IsNullOrEmpty(value))
{
    inMetadataBlock = true;
    continue;
}

// metadata 子行（以空白字符开头）
if (inMetadataBlock && line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
{
    // 解析 "  key: value" → 存入 metadataDict
}
else if (inMetadataBlock)
{
    inMetadataBlock = false; // 遇到非缩进行，退出 metadata 块
}
```

最终 `metadataDict` 序列化为 `IReadOnlyDictionary<string, JsonElement>` 赋给 `SkillMetadata.Metadata`。

#### 暴露静态解析方法

```csharp
// SkillsLoader.cs 新增 public 方法
/// <summary>
/// Parses SKILL.md frontmatter from raw content string.
/// Exposed for host-layer parsers to reuse without requiring ISandbox.
/// </summary>
public static SkillMetadata ParseFrontmatter(string content)
{
    var (metadata, _) = ParseSkillFile(content);
    return metadata;
}
```

`ParseSkillFile` 保持 `private static`，`ParseFrontmatter` 是唯一公开入口。

---

### KC-5402：`PermissionManager.GrantTools` + `Agent.cs` 工具注入

#### `PermissionManager` 新增动态授权方法

```csharp
/// <summary>
/// Grants additional tools at runtime (e.g. from activated skills).
/// No-op when no allowlist is configured (all tools already permitted).
/// Thread-safe.
/// </summary>
public void GrantTools(IEnumerable<string> toolNames)
{
    if (_allowTools == null) return;  // 无白名单 = 全放行，不需要操作
    lock (_lock)
    {
        foreach (var tool in toolNames)
        {
            if (!string.IsNullOrWhiteSpace(tool))
                _allowTools.Add(tool.Trim());
        }
    }
}
```

`_allowTools == null` 的语义是"无限制"，`GrantTools` 不应将其从 null 变为非 null（否则会把全放行模式误变为白名单模式）。

#### `Agent.cs` 工具注入（两条激活路径）

在 `InitializeSkillsAsync` 中，`SkillsConfig.AutoActivate` 和 Template `AutoActivate` 两条路径激活完成后，各自收集已激活技能的 `AllowedTools` 并注入权限：

```csharp
// 路径 1：SkillsConfig.AutoActivate 激活后
if (autoActivated.Count > 0)
{
    var granted = autoActivated
        .SelectMany(s => s.AllowedTools ?? [])
        .Distinct(StringComparer.OrdinalIgnoreCase);
    _permissionManager.GrantTools(granted);
    // ... 原有 RemindAsync + EmitMonitor 逻辑不变
}

// 路径 2：Template AutoActivate 激活后（同模式）
```

注意：`InitializeSkillsAsync` 在首条消息处理前同步完成，不存在并发安全问题。`GrantTools` 内的 `lock` 是防御性措施。

---

### KC-5403：KodaClaw 层迁移

#### SKILL.md 格式迁移（5 个文件）

5 个内置技能统一迁移至标准格式：

```yaml
---
name: koda-automation
description: HEARTBEAT.md 自动化规则编写指南——语法、调度、渠道推送、delivery-mode 配置
license: built-in
compatibility: KodaClaw 1.x
allowed-tools: workspace_protocol_update workspace_read
metadata:
  kind: builtin-core
  version: "1.0"
  tags: "automation, heartbeat, scheduling"
---
```

字段迁移对照：

| 旧（非标准顶层） | 新（标准位置） |
|---------------|-------------|
| `requires: [a, b]` | `allowed-tools: a b`（空格分隔，标准字段） |
| `kind: builtin-core` | `metadata.kind: builtin-core` |
| `version: "1.0"` | `metadata.version: "1.0"` |
| `tags: [a, b, c]` | `metadata.tags: "a, b, c"`（逗号分隔字符串） |
| —（未有） | `compatibility: KodaClaw 1.x`（标准字段，新增） |

#### `SkillFrontmatterParser` 精简重构

**原来**：自维护完整 YAML 解析逻辑，读取 `kind/version/tags/requires` 等非标字段。

**改后**：调用 `SkillsLoader.ParseFrontmatter(content)` 获取标准字段，再从 `Metadata` dict 提取 KodaClaw 专属字段：

```csharp
internal static ParsedFrontmatter Parse(string content)
{
    var meta = SkillsLoader.ParseFrontmatter(content);

    var kind = GetMetaString(meta.Metadata, "kind") is { Length: > 0 } k ? k : "optional";
    var version = GetMetaString(meta.Metadata, "version");
    var tags = GetMetaString(meta.Metadata, "tags") is { } t
        ? t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : [];

    return new ParsedFrontmatter(
        Description:   meta.Description,
        Kind:          kind,
        Version:       version,
        Tags:          tags,
        AllowedTools:  meta.AllowedTools ?? [],
        Compatibility: meta.Compatibility);
}

private static string? GetMetaString(IReadOnlyDictionary<string, JsonElement>? meta, string key)
{
    if (meta == null || !meta.TryGetValue(key, out var el)) return null;
    return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText().Trim('"');
}
```

`ParseInlineList` 可以删除（不再需要）。Gateway 端点的 `File.ReadAllLinesAsync` 改为 `File.ReadAllTextAsync`，传 string 而非 string[]。

#### `ParsedFrontmatter` record 字段变更

```csharp
// 迁移前
internal sealed record ParsedFrontmatter(
    string? Description,
    string Kind,
    string? Version,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Requires);       // ← 删除

// 迁移后
internal sealed record ParsedFrontmatter(
    string? Description,
    string Kind,
    string? Version,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> AllowedTools,    // ← 重命名
    string? Compatibility);               // ← 新增
```

#### `SkillContracts.cs` 字段变更

```csharp
// 迁移前
public sealed record SkillDescriptor(
    string Name, string? Description, string Source, string Path,
    bool HasResources, string Kind, IReadOnlyList<string> Tags,
    IReadOnlyList<string> Requires, string? Version);

// 迁移后
public sealed record SkillDescriptor(
    string Name, string? Description, string Source, string Path,
    bool HasResources, string Kind, IReadOnlyList<string> Tags,
    IReadOnlyList<string> AllowedTools,   // Requires → AllowedTools
    string? Version,
    string? Compatibility);               // 新增
```

#### `GatewayApp.SkillsEndpoints.cs` 调用点更新

```csharp
// 改为读 string 而非 string[]
var content = await File.ReadAllTextAsync(skillFile, cancellationToken);
frontmatter = SkillFrontmatterParser.Parse(content);

// SkillDescriptor 构造更新字段名
items.Add(new SkillDescriptor(
    ...
    AllowedTools:  frontmatter.AllowedTools,   // Requires → AllowedTools
    Compatibility: frontmatter.Compatibility));
```

---

### KC-5404：前端 + 测试

#### 前端 `contracts.ts`

```typescript
export interface SkillDescriptor {
  name: string;
  description?: string | null;
  source: string;
  path: string;
  hasResources: boolean;
  kind: string;
  tags: string[];
  allowedTools: string[];    // requires → allowedTools
  version?: string | null;
  compatibility?: string | null;  // 新增
}
```

#### 前端 `SkillsDesk.tsx`

- `skill.requires` → `skill.allowedTools`
- i18n key `skillRequiresLabel` → `skillAllowedToolsLabel`（中文："所需工具"，English："Allowed tools"）
- 新增 `compatibility` 行：有值时显示于 kind badge 同行或单独行，使用 `--text-tertiary` 色

#### 测试变更矩阵

| 测试文件 | 变更类型 |
|---------|---------|
| `SkillFrontmatterParserTests.cs` | 全量重写：`metadata:` 块、`allowed-tools` 空格分隔、`compatibility`；删除 `requires` 顶层解析测试 |
| `SkillDescriptorContractTests.cs` | `requires` → `allowedTools`，加 `compatibility` round-trip |
| `SkillsEndpointIntegrationTests.cs` | 同上字段重命名 |
| `SkillsLoaderTests.cs`（新增，SDK） | `allowed-tools` 连字符解析、空格分隔、`metadata:` 块解析、`ParseFrontmatter` public 方法 |
| `PermissionManagerGrantToolsTests.cs`（新增，SDK） | `_allowTools == null` 时 no-op；`_allowTools != null` 时成功添加；线程安全基础验证 |
| `AgentSkillToolGrantTests.cs`（新增，SDK L2） | AutoActivate 激活含 `allowed-tools` 的 skill → 对应工具在白名单中可执行 |

---

## 非目标（OUT OF SCOPE）

- 不引入 YamlDotNet 或其他 YAML 库（手写简单解析器已够用）
- 不实现 `allowed-tools` 的 Pattern 语法（如 `Bash(git:*)`，标准标注为 Experimental）
- 不暴露"哪些技能授权了哪些工具"给前端 UI（Monitor `SkillActivatedEvent` 已可观察）
- 不修改 `SkillsManager.AutoActivateAsync` 的实现（已符合要求）
- 不处理 `DenyTools` 与 `GrantTools` 的优先级（现有语义：Deny 优先于 Allow，不变）
- 不发布到 agentskills.io skill 市场（本迭代只做格式对齐，不做发布流程）

---

## 关键契约

### SDK `SkillsLoader`（新增 public API）

```csharp
public static SkillMetadata ParseFrontmatter(string content);
```

### `PermissionManager`（新增方法）

```csharp
/// No-op when _allowTools is null (all tools already permitted).
public void GrantTools(IEnumerable<string> toolNames);
```

### SKILL.md 标准格式（5 个内置技能统一遵守）

```yaml
---
name: <kebab-case>
description: <1-1024 chars>
license: built-in
compatibility: KodaClaw 1.x
allowed-tools: <space-delimited tool names>
metadata:
  kind: builtin-core | optional
  version: "x.y"
  tags: "<comma, separated, strings>"
---
```

---

## 受影响模块

| 模块 | 变更类型 |
|------|---------|
| `Kode.Agent.Sdk` | `SkillsLoader`：修 `allowed-tools` 解析 + `metadata:` 块 + 暴露 `ParseFrontmatter`；`PermissionManager`：新增 `GrantTools`；`Agent.cs`：两条激活路径后调用 `GrantTools` |
| `KodaClaw.Gateway/Infrastructure` | `SkillFrontmatterParser`：精简为 SDK 委托 + KodaClaw 字段提取；`ParsedFrontmatter`：`Requires`→`AllowedTools`，加 `Compatibility` |
| `KodaClaw.Gateway/Endpoints` | `GatewayApp.SkillsEndpoints.cs`：入参改 string，字段重命名 |
| `KodaClaw.Gateway/skills` | 5 个 SKILL.md 格式迁移 |
| `KodaClaw.Contracts` | `SkillDescriptor`：`Requires`→`AllowedTools`，加 `Compatibility` |
| `kodaclaw-web` | `contracts.ts` + `SkillsDesk.tsx` 字段重命名 + compatibility 展示 |
| 测试 | 3 个文件修改 + 3 个新文件（SDK L1×2 + SDK L2×1） |

---

## 验证矩阵

| 层级 | 验证内容 | 测试项目 | 工具 |
|------|---------|---------|------|
| L0 | `dotnet build` 0 错 0 警告；`npm run typecheck` 通过 | — | dotnet / vite |
| L1 | SDK：`SkillsLoaderTests`（allowed-tools 连字符/下划线/无分隔符三种写法均解析，空格分隔正确，metadata 块提取）；`PermissionManagerGrantToolsTests`（null allowTools no-op，非 null 正确添加） | `Kode.Agent.Tests` | xUnit |
| L1 | KodaClaw：`SkillFrontmatterParserTests`（全量重写，metadata.kind/tags/version 提取，allowed-tools 读取，compatibility 读取，缺失字段返回 defaults） | `KodaClaw.UnitTests` | xUnit |
| L2 | `AgentSkillToolGrantTests`：agent 加载含 `allowed-tools: custom_tool` 的 skill → AutoActivate 后 `custom_tool` 可执行（不被 allowlist 拦截） | `Kode.Agent.Tests` | xUnit |
| L3 | `SkillDescriptorContractTests`：`allowedTools` + `compatibility` JSON round-trip（camelCase 序列化，null optional 字段正确省略） | `KodaClaw.ContractTests` | xUnit |
| L2 | `SkillsEndpointIntegrationTests`：`GET /api/skills` 返回 `allowedTools` 数组 + `compatibility` 字段 | `KodaClaw.IntegrationTests` | WebApplicationFactory |
| L5 | Dogfood：主会话中 `koda-workspace` 自动激活后，Agent 可直接调用 `workspace_protocol_update`（若白名单限制启用，工具不被拦截）；SkillsDesk 显示 `allowedTools` 行和 `compatibility` 行 | — | 真实 Gateway + Web |

---

## 依赖关系

**依赖迭代 52 + 53 完成**（已完成）：
- 5 个内置 SKILL.md 文件已存在（KC-5202）
- `SkillsConfig.AutoActivate` 已接入三类 session（KC-5302）
- `SkillFrontmatterParser` + `SkillDescriptor` 结构已建立（KC-5201）

本迭代是在 52+53 基础上的标准对齐与功能补全，不引入新的外部依赖。
