# KC-CMD 专项 FREEZE — Channel 命令系统重构与扩展

> 冻结日期：2026-04-10
> 状态：FROZEN
> 专项前缀：KC-CMD-0X（跨迭代，共 4 个 Wave）

---

## 背景与动机

当前 `ChannelTurnOrchestrator.ProcessInboundAsync` 以硬编码 if-else 链处理渠道内斜杠命令（`/new`、`/status`、`/stop`），存在三个核心问题：

1. **扩展性差**：每新增一个命令就必须修改核心编排方法，职责耦合严重
2. **缺少发现机制**：用户无法通过渠道（Telegram/飞书/微信）得知有哪些命令可用
3. **无 Directive 概念**：OpenClaw 等同类系统支持"修饰符命令"（如 `/think`），在消息传入 Agent 前剥离并修改本次 turn 行为参数，KodaClaw 完全缺失

本专项以 OpenClaw 的命令模式为参考基准，分 4 个 Wave 完成重构与功能扩展。

---

## 目标（用户路径）

**Wave 1 完成后**：
- 代码架构对齐，现有 3 个命令行为不变，但可通过 `ChannelCommandRegistry` 统一查询

**Wave 2 完成后**：
- 用户发 `/help` 或 `/?` → 收到所有可用命令的格式化列表
- 用户发 `/think 帮我分析一下` → Agent 接收到的提示词被注入深思前缀，更仔细地回答

**Wave 3 完成后**：
- 用户发 `/compact` → 当前 session 空闲时，触发 LLM 对话历史摘要压缩（保留精华继续对话）；若 Agent 正在处理中则拒绝并提示
- 用户发 `/tools` → 列出当前 session 已加载的所有工具名称
- 用户发 `/whoami` → Koda 回复自己的身份摘要（来自 IDENTITY.md 前几段）
- 用户发 `/stream 帮我写一段代码` → 本次 turn 开启进度流式推送（仅本条消息生效）
- 用户发 `/quiet 帮我写一段代码` → 本次 turn 关闭进度推送（仅本条消息生效）

**Wave 4 完成后**：
- 用户发 `/focus 代码重构 解释一下这个类` → 回答时聚焦于代码重构视角
- 用户发 `/btw 你知道 XYZ 是什么吗` → Koda 回答但不写入会话历史（带外问答）

---

## 范围（IN SCOPE）

### Wave 1 — P0 纯重构

- 新建 `src/KodaClaw.ChannelHub/Commands/` 目录，包含 4 个文件：
  - `ParsedChannelCommand.cs`：解析结果 record（字段：`ControlKind: ChannelControlCommandKind?`、`ControlArg: string?`、`Directives: IReadOnlyList<ChannelDirectiveKind>`、`DirectiveArg: string?`、`CleanedText: string`）+ `ChannelControlCommandKind` + `ChannelDirectiveKind` 枚举。`DirectiveArg` 存储 Directive 的第一个参数（如 `/focus 代码重构 正文` 中的 `"代码重构"`），无参数 Directive 时为 null
  - `ChannelCommandRegistry.cs`：所有命令的静态定义表（Key、Aliases、Description、Category）
  - `ChannelCommandParser.cs`：`Parse(string?) → ParsedChannelCommand` 纯函数，从注册表构建 alias 映射
  - `ChannelCommandDispatcher.cs`：控制命令的执行分发（依赖 `IChannelSessionService`）
- `ChannelTurnContext.cs` 在 Wave 2 创建（Wave 1 无 Directive 消费方，提前创建无意义）
- 修改 `ChannelTurnOrchestrator`：将 if-else 链替换为 `ChannelCommandParser.Parse` + `ChannelCommandDispatcher.DispatchAsync`；删除原有 `IsSessionResetCommand` 和 `SessionResetCommands` 两个 internal 静态成员
- 修改 `ServiceCollectionExtensions`：注册 `ChannelCommandDispatcher`
- 迁移 `SessionResetCommandTests` → 改为测试 `ChannelCommandParser.Parse()`
- 新增 `ChannelCommandParserTests`、`ChannelCommandRegistryTests`（L1）

**不改变任何现有命令行为。**

### Wave 2 — P1 新命令（/help、/think）

**SDK 前置条件 A**（Wave 2 实现前须合并到 `Kode.Agent.Sdk`）：

**项目引用前置条件**：`ChannelTurnOrchestrator` 在 `KodaClaw.ChannelHub` 中需使用 `AgentRunOptions`（`Kode.Agent.Sdk` 类型）。检查 `KodaClaw.ChannelHub.csproj`——当前仅引用 `KodaClaw.Runtime`，后者传递引用 `Kode.Agent.Sdk`。若 `dotnet build` 报 `AgentRunOptions` 类型找不到，需在 `KodaClaw.ChannelHub.csproj` 显式添加 `<ProjectReference Include="..\..\..\src\Kode.Agent.Sdk\Kode.Agent.Sdk.csproj" />`（Wave 4 的 `IModelProvider`、`ISandboxFactory` 也来自同一 SDK，届时同样需要）。

新增 `AgentRunOptions` record 与 `RunAsync` per-turn 重载。**实现要点**：`options` 不能逐层透传到 `StepAsync`（会破坏已有接口），而是存为 `Agent` 的实例字段 `_currentRunOptions`，在 `RunMultimodalAsync` 开始时赋值、`finally` 块中清空。`RunAsync` 已由 session 层 `SemaphoreSlim` 保护（同一 session 不并发），所以此字段无竞态风险。

```csharp
// Kode.Agent.Sdk/Core/Abstractions/IAgent.cs — 新增 record 和重载
public record AgentRunOptions
{
    public bool? EnableThinking { get; init; }
    public int? ThinkingBudget { get; init; }
}

Task<AgentRunResult> RunAsync(string input, AgentRunOptions? options,
    CancellationToken cancellationToken = default);

// Agent.cs — 实现
private AgentRunOptions? _currentRunOptions;

public async Task<AgentRunResult> RunAsync(string input, AgentRunOptions? options,
    CancellationToken cancellationToken = default)
{
    _currentRunOptions = options;
    try { return await RunMultimodalAsync([new TextContent { Text = input }], cancellationToken); }
    finally { _currentRunOptions = null; }
    // 注意：必须 await，否则 finally 在 Task 返回时立即执行，此时 RunMultimodalAsync 尚未运行，
    // _currentRunOptions 会在 BuildModelRequest() 调用前被清空。
}

// BuildModelRequest() 中
EnableThinking = _currentRunOptions?.EnableThinking ?? _config.EnableThinking,
ThinkingBudget = _currentRunOptions?.ThinkingBudget ?? _config.ThinkingBudget,
```

**SDK 前置条件 B**（Wave 2，同步修改 KodaClaw.Runtime）：

`IChannelSessionService.RunInboundTurnAsync` 增加可选参数（向后兼容，不破坏现有调用方）：
```csharp
Task<ChannelTurnExecutionResult> RunInboundTurnAsync(
    ThreadBinding binding, ChannelPolicy policy,
    ChannelEventEnvelope envelope, bool hasExplicitMention,
    AgentRunOptions? runOptions = null,          // ← 新增
    CancellationToken cancellationToken = default);
```
`ChannelSessionService` 实现中将 `runOptions` 透传给 `handle.Agent.RunAsync(prompt, runOptions, ct)`。

---

- 新建 `ChannelTurnContext.cs`：per-turn 参数覆盖 record，本 Wave 含以下字段 + `FromDirectives()` 工厂方法：
  - `string? PromptPrefix`：拼接在 CleanedText 前（所有模型通用）
  - `bool? EnableThinking`：**nullable**，null = 不覆盖 session config；true = 本 turn 开启 extended thinking
  - `int? ThinkingBudget`：thinking token 预算（`/think` 触发时默认 8000）
- `ChannelCommandRegistry` 追加 `Help` 控制命令（aliases: `/help /commands /?`）
- `ChannelControlCommandKind` 枚举扩展 `Help`
- `ChannelDirectiveKind` 枚举新增 `Think`
- `ChannelCommandParser` 扩展 Directive 前缀剥离逻辑（`/think 正文` → `Directives=[Think]`, `CleanedText=正文`；`/think` 单独行 → `CleanedText=""`）
- `ChannelCommandDispatcher` 新增 `Help` case（调 `FormatHelpText()` + `SendNotificationAsync`）
- `ChannelTurnOrchestrator` 在 Agent turn 前应用 `ChannelTurnContext`：
  - **prompt 组装**：`finalText = (ctx.PromptPrefix ?? "") + parsed.CleanedText`，再 `envelope with { Text = finalText }` 替换（不直接修改 CleanedText，保持分离）
  - `Think` directive 设 `EnableThinking=true, ThinkingBudget=8000`
  - 调用 `_channelSessionService.RunInboundTurnAsync(..., runOptions: new AgentRunOptions { EnableThinking = ctx.EnableThinking, ThinkingBudget = ctx.ThinkingBudget }, ct)` 透传（通过接口，不直接访问 agent）
  - **Control + Directive 优先级**：若 `parsed.ControlKind` 非 null，直接走 Dispatcher 分支并返回，Directives 不应用
- **空正文 guard**：Orchestrator 进入 Agent turn 前，若 `ctx.Directives` 非空且 `parsed.CleanedText.Trim()` 为空，按首个 Directive 类型发送对应使用提示并直接返回，不运行 Agent：
  - `Think` → "用法：`/think <您想深度思考的问题>`，Koda 会更仔细地回答。"
  - `Focus`（Wave 4）→ "用法：`/focus <视角> <您的问题>`，Koda 会聚焦该视角回答。"
  - 其余 Directive → "请在命令后提供要处理的内容。"
- 新增 `ChannelCommandDispatcherTests`、`ChannelTurnContextTests`（L1）

### Wave 3 — P2 扩展命令（/compact、/tools、/whoami、/stream、/quiet）

- `IChannelSessionService` 新增两个方法：
  - `GetSessionToolNamesAsync(string sessionId, CancellationToken) → Task<IReadOnlyList<string>>`：直接返回 `_sessionOptions.Tools`（session 配置的工具名列表），session 不存在时返回空列表。**不**访问 `Agent._tools`（私有字段）或 `ToolRegistry`（未对外暴露）。
  - `CompressSessionContextAsync(string sessionId, CancellationToken) → Task<string>`：触发 LLM 对话历史摘要压缩，返回用户提示文本。**实现要点**：必须在 `_sessionLocks.GetOrAdd(sessionId, ...)` 的 SemaphoreSlim 内执行，避免与正在进行的 turn 并发访问 `_messages`。持有锁后再检查 `RuntimeState`：若 `== Working` 则释放锁并返回"Koda 正在处理中，请稍后再压缩。"（belt-and-suspenders guard，理论上锁已阻止并发，但保留此检查以应对边缘状态）；否则调 `agent.ForceCompressAsync(ct)`，返回"已完成上下文压缩，对话记忆已整理。"
- `ChannelCommandRegistry` 追加 `Compact`（alias: `/compact`）、`Tools`（alias: `/tools`）、`WhoAmI`（aliases: `/whoami /me`）三个控制命令
- `ChannelControlCommandKind` 枚举扩展 `Compact`、`Tools`、`WhoAmI`
- `ChannelDirectiveKind` 枚举新增 `Stream`、`Quiet`
- `ChannelCommandDispatcher` 注入 `IWorkspaceService`，实现三个 case：
  - `Compact`：调 `_channelSessionService.CompressSessionContextAsync`
  - `Tools`：调 `_channelSessionService.GetSessionToolNamesAsync`，格式化为换行列表
  - `WhoAmI`：`File.ReadAllTextAsync(Path.Combine(_workspaceService.RootPath, "workspace", "IDENTITY.md"))`，取前 500 字符发送（`IWorkspaceService` 无文件读取方法，直接用 `RootPath` 拼路径；文件不存在时返回"身份文件未初始化。"）
- `ChannelTurnContext` 新增 `EnableProgressStreamingOverride` 字段
- `ChannelTurnOrchestrator` 应用 streaming override（局部变量覆盖 `_sessionOptions.EnableProgressStreaming`，仅影响本次 turn）
- **新增单元测试（L1）**：
  - `ChannelCommandParserTests` 扩展：`/compact`、`/tools`、`/whoami`、`/stream <正文>`、`/quiet <正文>` 解析验证
  - `ChannelCommandDispatcherTests` 扩展：`Compact`/`Tools`/`WhoAmI` 三个 case 的 mock 行为测试
  - `ChannelSessionServiceTests` 新增：`CompressSessionContextAsync` 的 not-processing guard 测试（`RuntimeState == Working` 时返回提示文本，不执行压缩）
  - `ChannelTurnOrchestratorTests` 扩展：`/stream`、`/quiet` 覆盖局部变量生效，不影响全局 `_sessionOptions`

### Wave 4 — P3 高级命令（/focus、/btw）

- `ChannelDirectiveKind` 枚举新增 `Focus`
- `ChannelTurnContext` 新增 `FocusConstraint` 字段（追加到 prompt 尾部）
- `ChannelCommandParser` 解析 `/focus <topic> 正文`：**topic 为第一个空格前的连续字符串**，之后全部为正文。例：`/focus 代码重构 解释一下这个类` → `DirectiveArg="代码重构"`，`CleanedText="解释一下这个类"`；若 `/focus` 后无空格（只有 topic 无正文），则 CleanedText 为空
- `ChannelTurnContext.FromDirectives(parsed)` 从 `parsed.DirectiveArg` 读取 `FocusConstraint`
- `ChannelTurnOrchestrator` 将 `FocusConstraint` 以约束语句形式追加到 prompt 尾部（如 `\n\n[视角约束：请聚焦于 {focus} 的角度回答]`）
- `/btw`：`ChannelControlCommandKind` 枚举新增 `SideQuestion`；`ChannelCommandDispatcher` 创建 ephemeral agent，步骤：
  1. 读取 IDENTITY.md + SOUL.md 内容（`File.ReadAllTextAsync(Path.Combine(_workspaceService.RootPath, "workspace", "IDENTITY.md"))`，SOUL.md 同理；文件不存在时 graceful fallback 为空字符串）
  2. 新建 `EphemeralAgentStore`（KodaClaw.ChannelHub 内部 `sealed class`，实现 `IAgentStore`，所有方法 no-op 或返回空集合；参考 `Kode.Agent.Tools.Orchestration.Internal.InMemoryAgentStore` 但不依赖其 internal 类）
  3. 调 `Agent.CreateAsync(Guid.NewGuid().ToString(), config, new AgentDependencies { Store = new EphemeralAgentStore(), ModelProvider = _modelProvider, SandboxFactory = _sandboxFactory, ToolRegistry = new EphemeralToolRegistry() }, ct)`（`AgentDependencies` 有 4 个 `required` 字段，均需提供；`EphemeralToolRegistry` 与 `EphemeralAgentStore` 同文件定义，实现 `IToolRegistry`，所有方法 no-op 或返回空集合，/btw 不需要工具；SDK 方法名为 `Agent.CreateAsync`，无 `AgentRuntime` 类）
  4. 运行 `agent.RunAsync(parsed.ControlArg ?? "", ct)`（`parsed.ControlArg` 是剥离 `/btw` 前缀后的正文；`envelope.Text` 仍含命令前缀，不可直接传入）
  5. `await using` 析构 agent，不写入 session 历史

---

## 范围（OUT OF SCOPE）

- Web UI 命令管理界面（命令纯渠道侧，无需前端）
- Gateway API 新端点（无需）
- `/model <alias>` per-turn 模型切换（`AgentRunOptions` Wave 2 引入后技术可行，但模型别名解析依赖 ModelHub，需单独 Wave 评估）
- `/deep` MaxIterations 临时提升（同上，`AgentRunOptions` 扩展 `MaxIterations?` 字段后可做，不在本专项）
- `/exec`、`/bash` 渠道暴露 shell（安全边界，不做）
- 鉴权分层（owner-only 命令）—— 渠道现有账号隔离已足够，不做额外权限层

---

## 关键设计决策

### 1. Directive 的 CleanedText 替换策略

`BuildPrompt(binding, envelope, hasExplicitMention)` 当前使用 `envelope.Text`。Directive 剥离后，Orchestrator 在调用 `BuildPrompt` 前创建临时 envelope 副本替换 `Text` 字段（`envelope with { Text = parsed.CleanedText }`）。**此设计不修改 `IChannelSessionService` 中的 CleanedText 流转接口**，副作用为零。

> **注意**：此"不修改接口"仅指 CleanedText/envelope 替换策略本身（Wave 1 设计）。Wave 2 前置条件 B 会在 `IChannelSessionService.RunInboundTurnAsync` 追加可选参数 `AgentRunOptions? runOptions = null`，属于向后兼容的接口扩展，不属于此约束范围。

已确认：`ChannelEventEnvelope` 为 `sealed record`（`KodaClaw.Contracts/Channels/ChannelEventEnvelope.cs`），`with` 表达式可用，无需额外适配。

### 2. Dispatcher 依赖注入边界

`ChannelCommandDispatcher` 通过构造函数注入：
- `IChannelSessionService`（session 操作）
- `ChannelDeliveryDispatchService`（发送通知）
- `IWorkspaceService`（Wave 3，读 IDENTITY.md；Wave 4，读 SOUL.md）
- `IModelProvider`（Wave 4，ephemeral agent 需要）
- `ISandboxFactory`（Wave 4，ephemeral agent 需要）
- `IDiagnosticsService?`（可选，记录事件）

不注入 `IChannelAccountRepository`（由 Orchestrator 在调用前解析 account，再传给 Dispatcher）。`EmptyToolRegistry`（Wave 4 内部 no-op 实现，不注入，直接 `new`）。

### 3. 注册表与解析器静态化

`ChannelCommandRegistry` 和 `ChannelCommandParser` 均为静态类。  
命令定义在编译期固定，不支持运行时动态注册（渠道命令不需要插件扩展）。

### 4. `/compact` SDK 层风险评估与设计

**背景**：经代码审查，`Agent` 类当前没有手动压缩的 public API。`_contextManager.CompressAsync` 是 public 方法，但 `_contextManager` 字段和 `_messages` 字段均为 `private`。

**需要的 SDK 变更（Wave 3 前置条件）**：在 `Kode.Agent.Sdk` 的 `Agent` 类中新增：
```csharp
/// <summary>
/// Force context compression between turns. Safe to call only when the agent is idle
/// (RuntimeState != Working). Caller is responsible for checking agent state before invoking.
/// </summary>
public async Task<bool> ForceCompressAsync(CancellationToken cancellationToken = default)
{
    var systemPromptTokens = ContextManager.EstimateSystemPromptTokens(_systemPrompt);
    var result = await _contextManager.CompressAsync(
        _messages, _eventBus.GetTimelineSnapshot(),
        _filePool, _sandbox, systemPromptTokens, force: true, cancellationToken);
    if (result == null) return false;
    _messages.Clear();
    _messages.AddRange(result.RetainedMessages);
    await _hookManager.RunMessagesChangedAsync(_messages, cancellationToken);
    await SaveStateAsync(cancellationToken);
    _eventBus.EmitMonitor(new ContextCompressionEvent
    {
        Type = "context_compression", Phase = "end",
        Summary = string.Join("\n", result.Summary.Content.OfType<TextContent>().Select(t => t.Text)),
        Ratio = result.Ratio
    });
    return true;
}
```

**线程安全分析**：
- `_messages` 没有专用互斥锁，但 `Agent` 的 turn 执行是顺序的（单一 `_processingCts`）
- not-processing guard（`RuntimeState == Working` → 拒绝）保证不在 Agent 运行中调用
- turn 间隙无并发访问 `_messages`，因此风险可控
- **前置条件**：Wave 3 实现前，必须先在 SDK 仓库中合并 `ForceCompressAsync` 方法

---

## 非目标（明确排除）

- 不将命令系统暴露为 MCP 工具
- 不支持跨渠道命令差异化（所有渠道共享同一命令集）
- 不支持用户自定义命令（用 Skill 系统代替）

---

## 验证策略

| 层级 | 内容 | 工具 |
|------|------|------|
| L0 | 每个 Wave 完成后编译 0 错 0 警告 | `dotnet build KodaClaw.sln` |
| L1 | 解析器、注册表、Dispatcher、TurnContext 单元测试 | xUnit，无 I/O |
| L5 | Wave 2 完成后 Dogfood：Telegram 发 `/help` 可见命令列表；发 `/think` 后回答质量提升 | 真实 Gateway |

channels 模块必须过 L5 Dogfood，不可省略。
