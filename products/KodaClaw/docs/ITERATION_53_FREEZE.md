# Iteration 53 FREEZE — Skills 会话启动自动激活

**日期**：2026-03-25
**类型**：新功能
**范围**：Kode.Agent.Sdk / KodaClaw.Runtime / KodaClaw.Contracts

---

## 背景与动机

迭代 52 补全了 5 个内置技能（`koda-workspace` + 4 个新增），用户可在 SkillsDesk 浏览，Agent 也可通过 `skill_list`/`skill_activate` 主动调用。但触发路径完全依赖 Agent 自己判断——用户说"帮我写个自动化"，Agent 不一定会想到先 `skill_activate koda-automation`。

对于覆盖平台核心协议的 `builtin-core` 技能，让 Agent 被动发现意义不大：这些技能描述的是 KodaClaw 本身的工作方式，理应在会话启动时就注入到 context，而不是等 Agent 碰巧想起来再激活。

**SDK 已有机制**：`TemplateSkillsConfig.AutoActivate`（`Agent.cs:2855`），但需要通过 Template 系统触发，KodaClaw 目前不使用 Template。最干净的修复是把 `AutoActivate` 直接加到 `SkillsConfig`（技能配置的自然归属处），与 `Paths`/`Include`/`Exclude` 并列。

---

## 范围（IN SCOPE）

### 轨道一：SDK `SkillsConfig.AutoActivate` 字段（KC-5301）

**`SkillsConfig` 新增字段**（`Kode.Agent.Sdk/Core/Skills/SkillTypes.cs`）：

```csharp
public record SkillsConfig
{
    public required IReadOnlyList<string> Paths { get; init; }
    public IReadOnlyList<string>? Include { get; init; }
    public IReadOnlyList<string>? Exclude { get; init; }
    public IReadOnlyList<string>? Trusted { get; init; }
    public bool ValidateOnLoad { get; init; } = true;

    // 新增：会话启动时自动激活的技能名列表
    public IReadOnlyList<string>? AutoActivate { get; init; }
}
```

**`Agent.cs` 调用点变更**（`Agent.cs` skills 初始化区块，约第 2845 行之前）：

在 Template `AutoActivate` 逻辑**之前**，加一段从 `SkillsConfig` 读取的 auto-activate：

```csharp
// 1. SkillsConfig.AutoActivate（KodaClaw 配置路径，不依赖 Template）
if (_config.Skills?.AutoActivate is { Count: > 0 })
{
    var autoActivated = await _skillsManager.AutoActivateAsync(
        _config.Skills.AutoActivate, cancellationToken);
    // 注入 context + 发 SkillActivatedEvent（复用现有 Template 路径的代码）
}

// 2. Template AutoActivate（原有逻辑，不动）
if (templateSkills?.AutoActivate is { Count: > 0 }) { ... }
```

两者不互斥，但 KodaClaw 只用路径 1。

### 轨道二：KodaClaw 三类 Session 接入 AutoActivate（KC-5302）

#### 按 Session 类型选择自动激活的技能

不同 session 的职责不同，按需激活，避免无关技能占用 context：

| Session 类型 | 自动激活技能 | 理由 |
|-------------|------------|------|
| **Chat**（主对话） | `koda-workspace`、`koda-memory` | 所有主对话都涉及 workspace 协议和记忆管理 |
| **Channel**（渠道消息） | `koda-workspace`、`koda-channels` | 渠道 session 需要了解 channel_send 工具和消息格式 |
| **Automation**（自动化任务） | `koda-workspace`、`koda-automation` | 自动化任务需要了解 HEARTBEAT.md 语法和调度规则 |

`koda-canvas` **不自动激活**：Canvas 是按需能力，仅在用户明确要求创作时有用，自动激活会在所有 session 中增加不必要的 context 占用。

#### 常量定义

常量类放在 `KodaClaw.Runtime/BuiltinSkills.cs`——它描述的是 session 配置行为而非公共 API 契约，不需要跨模块共享，不放 Contracts：

```csharp
// KodaClaw.Runtime/BuiltinSkills.cs
public static class BuiltinSkills
{
    public const string KodaWorkspace  = "koda-workspace";
    public const string KodaMemory     = "koda-memory";
    public const string KodaChannels   = "koda-channels";
    public const string KodaAutomation = "koda-automation";
    public const string KodaCanvas     = "koda-canvas";

    // 按 session 类型的默认自动激活列表
    public static readonly IReadOnlyList<string> ChatAutoActivate =
        [KodaWorkspace, KodaMemory];
    public static readonly IReadOnlyList<string> ChannelAutoActivate =
        [KodaWorkspace, KodaChannels];
    public static readonly IReadOnlyList<string> AutomationAutoActivate =
        [KodaWorkspace, KodaAutomation];
}
```

#### Session Service 改动（3 文件，共 5 处）

注意：主对话 session 的 skills 配置在 `MainSessionService.cs`，不在 `ChatSessionService.cs`（后者是独立的 chat 历史 / 会话管理服务，不含 `SkillsConfig`）。

| 文件 | `SkillsConfig` 构造位置 | 新增 `AutoActivate` |
|------|----------------------|-------------------|
| `MainSessionService.cs` | ~456（新建）、~710（resume） | `BuiltinSkills.ChatAutoActivate` |
| `ChannelSessionService.cs` | ~107（DM 新建）、~386（resume） | `BuiltinSkills.ChannelAutoActivate` |
| `AutomationSessionService.cs` | ~248（自动化执行） | `BuiltinSkills.AutomationAutoActivate` |

改动形如：

```csharp
Skills = new SkillsConfig
{
    Paths = skillsPaths,
    ValidateOnLoad = true,
    AutoActivate = BuiltinSkills.ChatAutoActivate,  // 新增
}
```

#### 容错行为

`AutoActivateAsync` 对不存在的技能名**静默跳过**（SDK 已有该行为）。因此：
- 迭代 52 未完成时（新技能文件不存在），此处配置不会报错
- 用户 workspace 中未安装对应技能时，也不会阻断 session 启动

---

## 非目标（OUT OF SCOPE）

- 不基于 frontmatter `kind` 字段**动态推导** AutoActivate 列表（运行时读文件开销不值得，列表由发布者显式维护）
- 不修改 Template `AutoActivate` 路径（保持原有 Template 系统不变）
- 不为 `koda-canvas` 添加自动激活（按需激活，保持 context 精简）
- 不修改 `SkillsLoader`、`SkillsManager` 的 `AutoActivateAsync` 实现（已符合要求）
- 不向前端暴露"哪些技能被自动激活"的 API（Monitor channel 的 `SkillActivatedEvent` 已可观察）

---

## 关键契约

### SkillsConfig（SDK）

```csharp
public record SkillsConfig
{
    public required IReadOnlyList<string> Paths { get; init; }
    public IReadOnlyList<string>? Include { get; init; }
    public IReadOnlyList<string>? Exclude { get; init; }
    public IReadOnlyList<string>? Trusted { get; init; }
    public bool ValidateOnLoad { get; init; } = true;
    public IReadOnlyList<string>? AutoActivate { get; init; }  // 新增
}
```

### BuiltinSkills 常量

```csharp
public static class BuiltinSkills
{
    public const string KodaWorkspace  = "koda-workspace";
    public const string KodaMemory     = "koda-memory";
    public const string KodaChannels   = "koda-channels";
    public const string KodaAutomation = "koda-automation";
    public const string KodaCanvas     = "koda-canvas";

    public static readonly IReadOnlyList<string> ChatAutoActivate       = [KodaWorkspace, KodaMemory];
    public static readonly IReadOnlyList<string> ChannelAutoActivate    = [KodaWorkspace, KodaChannels];
    public static readonly IReadOnlyList<string> AutomationAutoActivate = [KodaWorkspace, KodaAutomation];
}
```

---

## 受影响模块

| 模块 | 变更类型 |
|------|---------|
| `Kode.Agent.Sdk` | `SkillsConfig` 新增 `AutoActivate` 字段；`Agent.cs` 新增从 `SkillsConfig` 读取并调用 `AutoActivateAsync` 的逻辑 |
| `KodaClaw.Runtime` | 新增 `BuiltinSkills.cs` 常量类；`MainSessionService`（2 处）、`ChannelSessionService`（2 处）、`AutomationSessionService`（1 处）共 5 处 `SkillsConfig` 配置加 `AutoActivate` |

---

## 验证矩阵

| 层级 | 验证内容 | 测试项目 | 工具 |
|------|---------|---------|------|
| L0 | `dotnet build` 0 错 0 警告 | — | dotnet |
| L1 | `SkillsConfigAutoActivateTests`：`AutoActivate` 为 null 时 session 正常启动（无副作用）；技能名不存在时静默跳过；存在技能时 `SkillActivatedEvent` 被 emit；Chat/Channel/Automation 三类 session 各激活预期技能集合 | `KodaClaw.UnitTests` + `Kode.Agent.Tests` | xUnit |
| L2 | `AutoActivateIntegrationTests`：完整 Gateway 启动 → 发消息 → SSE 流中出现 `skill_activated` Monitor 事件，`activatedBy="auto"`，skill 名包含 `koda-workspace` | `KodaClaw.IntegrationTests` | WebApplicationFactory |
| L5 | Dogfood：主会话开始，Agent 直接引用 HEARTBEAT.md 语法或 workspace 协议细节（无需先 `skill_activate`）；新会话的 system prompt token 消耗合理（无 koda-canvas 内容注入） | — | 真实 Gateway + Web |

---

## 依赖关系

**本迭代依赖迭代 52 完成**：

- KC-5201 必须完成（新技能 frontmatter 规范确立后，`kind: builtin-core` 作为后续技能管理的基准）
- KC-5202 必须完成（4 个新技能文件存在，AutoActivate 才有内容可激活；否则 session 启动时静默跳过，功能正确但效果为零）
- KC-5203 不是硬依赖（可并行，不影响本迭代的后端逻辑）
