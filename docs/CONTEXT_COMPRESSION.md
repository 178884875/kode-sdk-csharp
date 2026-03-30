# Context Compression

## Overview

`ContextManager` 在每次模型调用前检测上下文 token 用量，超过阈值时自动触发压缩。压缩采用**三层架构**，确保长会话中关键信息不会丢失。

---

## 三层架构

```
┌──────────────────────────────────────────────────────┐
│  Layer 1 – Core-Memory Block                         │  ~1500 tokens，永不删除
│  <core-memory> system message                        │  每次压缩由 LLM 更新
│  当前任务 / 修改文件 / 关键决策 / 待办事项           │
├──────────────────────────────────────────────────────┤
│  Layer 2 – Summary Stack                             │  叠加式，永不删除
│  <context-summary> system messages                   │  每次压缩追加一条
│  [summary-1] → [summary-2] → [summary-N]            │
├──────────────────────────────────────────────────────┤
│  Layer 3 – Recent Messages                           │  按 token 预算动态选取
│  普通 user / assistant / tool 消息                  │  低价值消息优先删除
└──────────────────────────────────────────────────────┘
```

### 为什么要三层？

朴素的"截断旧消息"策略存在两个根本问题：

1. **摘要被覆盖**：每次压缩生成的摘要本身在下次压缩时可能被删掉，信息指数级衰减。
2. **重要上下文丢失**：早期的任务目标、文件修改记录、关键决策没有持久载体。

三层架构中，Layer 1 和 Layer 2 永不参与压缩删除，Layer 3 才是被压缩的对象。

> 设计参考：MemGPT（[arxiv 2310.08560](https://arxiv.org/abs/2310.08560)）将 LLM 上下文类比为操作系统虚拟内存，Core Memory = RAM（永远可见），Recall Memory = 磁盘（可检索）。

---

## 触发与压缩流程

```
每次模型调用前
       │
       ▼
  ContextManager.Analyze()
  CJK-aware token 估算
       │
  totalTokens > MaxTokens?
       │ Yes
       ▼
  分离 pinned（Layer 1+2）vs regular（Layer 3）消息
       │
  按重要性打分，token 预算内选取保留消息
       │
  IContextSummarizer.SummarizeAsync()
  ┌─────────────────────────────────────┐
  │ LlmContextSummarizer（默认）        │
  │   - 生成语义摘要                    │
  │   - 更新 core-memory block          │
  │   - 失败自动降级到 StaticSummarizer │
  └─────────────────────────────────────┘
       │
  重建消息列表：
  [core-memory] + [summary stack] + [new summary] + [retained]
       │
       ▼
  继续调用模型
```

---

## Token 估算

旧版使用 `chars / 4` 均一估算，对中文严重低估（实际偏差约 4×）。

新版区分字符类型：

```csharp
// CJK 字符（汉字/假名/韩文）≈ 1.5 tokens/char
// 其他字符（英文/符号）≈ 0.25 tokens/char
```

> 数据来源：[arxiv 2305.15425](https://arxiv.org/abs/2305.15425)（NeurIPS 2023）实测中文为英文的 1.76×；本实现取保守值 1.5×。

---

## 消息重要性打分

Layer 3 消息在 token 超出预算时按分数从低到高删除：

| 分量 | 范围 | 说明 |
|------|------|------|
| Recency | 0–40 | 越新得分越高，线性插值 |
| Role | 0–30 | User 消息 30，Assistant 15 |
| ToolType | -20–+20 | `fs_write`/`workspace_*` = +20；纯 `bash_logs` 轮询 = -20 |

**最终得分 = Recency + Role + ToolType**，低分消息优先被移除。

### tool_use / tool_result 原子对保护

压缩前先扫描全部消息，建立 `toolUseId → message index` 和 `toolResultId → message index` 双向映射。移除某条消息时：

- 若该消息含 `tool_use`，自动同时移除含对应 `tool_result` 的消息（反之亦然）；
- 若配对消息位于 `minRecentCount` 保护区，则**整对不移除**，保证 API 合法性。

这避免了 `SanitizeOrphanToolResults` 不得不将孤立 tool_result 改写为文本的情况，减少信息损失。

### 系统 prompt token 计入

`Analyze(messages, systemPromptTokens)` 新增第二参数（默认 0）。`Agent.cs` 在每次压缩判断前调用 `ContextManager.EstimateSystemPromptTokens(_systemPrompt)` 得到估算值并传入，确保 system prompt（通常 3 000–10 000 tokens）被计入触发阈值，避免"消息看起来没超限但加上 system prompt 已经超窗口"的情况。

> 设计参考：[LLMLingua-2](https://arxiv.org/abs/2403.12968)（Microsoft Research）的 Budget Controller 按重要性分配压缩比例；通用权重公式 `Relevance×0.4 + Importance×0.3 + Recency×0.2 + Frequency×0.1`。

---

## IContextSummarizer 接口

```csharp
public interface IContextSummarizer
{
    Task<SummaryResult> SummarizeAsync(
        IReadOnlyList<Message> removedMessages,
        ContextManagerOptions options,
        CancellationToken cancellationToken = default);
}

public record SummaryResult(
    string Summary,            // 注入为 <context-summary> system message
    string? CoreMemoryUpdate   // null = 保留旧 core-memory 不变
);
```

### 内置实现

| 实现 | 行为 |
|------|------|
| `LlmContextSummarizer` | 调用 `IModelProvider.CompleteAsync`，一次请求同时生成 summary 和 core-memory。输入限制 12,000 字符，连续 `bash_logs` 轮询自动去重。失败自动降级。 |
| `StaticContextSummarizer` | 纯文本统计（消息数、工具调用数、首尾用户消息摘要）。不调用模型，零额外成本。 |

### 自定义 Summarizer

```csharp
public class MyCustomSummarizer : IContextSummarizer
{
    public async Task<SummaryResult> SummarizeAsync(
        IReadOnlyList<Message> removedMessages,
        ContextManagerOptions options,
        CancellationToken cancellationToken = default)
    {
        // 自定义摘要逻辑
        return new SummaryResult("...", "## Current Task\n...");
    }
}

// 注入：
var summarizer = new MyCustomSummarizer();
var contextManager = new ContextManager(store, agentId, options, summarizer);
```

---

## 配置参考

```csharp
var config = new AgentConfig
{
    Context = new ContextManagerOptions
    {
        // 触发压缩的 token 上限
        // KodaClaw 默认：DefaultContextWindowSize × 0.75 = 96,000
        MaxTokens = 96_000,

        // 压缩后 Layer 3 的 token 目标
        // KodaClaw 默认：DefaultContextWindowSize × 0.40 = 51,200
        CompressToTokens = 51_200,

        // 压缩用模型，null = 使用 Agent 主模型
        // 建议设置为轻量模型（如 claude-haiku-4-5-20251001）以降低成本
        CompressionModel = "claude-haiku-4-5-20251001",

        // 自定义压缩 prompt，空字符串 = 使用内置英文 prompt（见下节）
        CompressionPrompt = "",

        // 是否启用 core-memory block（MemGPT 风格任务状态持久化）
        EnableCoreMemory = true,

        // 最少保留的最近消息数（防止 summary stack 过大时丢失所有近期上下文）
        MinRecentMessages = 6,

        // summary stack 最大深度；超限时触发递归合并
        MaxSummaryDepth = 5,
    }
};
```

---

## CompressionPrompt 接入指南

`CompressionPrompt` 是面向**接入方产品**的核心扩展点，允许不同业务场景的压缩行为在不修改 SDK 的前提下独立定制。

### 默认行为（空字符串）

`LlmContextSummarizer` 内置英文 prompt，适合通用对话 / 代码助手场景：
- `<summary>`：任务目标、已完成步骤、关键文件路径、重要决策、剩余工作
- `<core-memory>`：Current Task / Modified Files / Key Decisions / Next Steps

### 何时需要自定义

| 场景 | 默认 prompt 的问题 | 应侧重的信息 |
|------|------------------|-------------|
| 渠道消息（IM Bot） | "Modified Files"无意义，丢失消息收发记录 | 发送方账号、消息内容、发送结果、用户偏好 |
| 自动化任务 | 对话式摘要格式不适合结构化日志 | 任务名、执行步骤状态（✓/✗）、产出物、错误处理 |
| 客服 / 工单系统 | 通用摘要丢失工单 ID 和 SLA 信息 | 工单号、问题描述、当前状态、承诺时间 |
| 数据分析 Agent | 文件路径不如数据集名称和指标重要 | 数据集、查询逻辑、关键发现、图表路径 |

### 接入示例

**最小接入（使用默认 prompt）**：
```csharp
Context = new ContextManagerOptions
{
    MaxTokens = 96_000,
    CompressToTokens = 51_200,
    // CompressionPrompt 留空 → 使用内置 prompt
}
```

**渠道 Bot 场景**：
```csharp
Context = new ContextManagerOptions
{
    MaxTokens = 96_000,
    CompressToTokens = 51_200,
    CompressionPrompt = """
        You are compressing a channel conversation history.
        Produce your response in EXACTLY this XML format:

        <summary>
        Concise summary (under 400 words).
        Include: sender accounts, key messages received, replies sent, delivery outcomes.
        Omit: repetitive polling, transient error retries.
        </summary>

        <core-memory>
        ## Active Channels
        [Channel accounts in use]

        ## Recent Thread
        [Last 2-3 turns with sender attribution]

        ## Delivery Status
        [Pending / failed deliveries, if any]
        </core-memory>
        """,
}
```

**自动化任务场景**：
```csharp
Context = new ContextManagerOptions
{
    MaxTokens = 96_000,
    CompressToTokens = 51_200,
    CompressionPrompt = """
        You are compressing an automation task history.
        Produce your response in EXACTLY this XML format:

        <summary>
        Summary (under 400 words).
        Include: task name, trigger time, steps with ✓/✗ status, outputs, error handling.
        </summary>

        <core-memory>
        ## Task
        [Task name and schedule]

        ## Execution Log
        [Steps: ✓ success / ✗ failed / ⚠ partial]

        ## Outputs
        [Files written, messages sent, API calls made]

        ## Next Steps
        [Remaining work in this run]
        </core-memory>
        """,
}
```

### Prompt 编写规则

1. **格式固定**：必须要求模型输出 `<summary>` + `<core-memory>` 两段 XML，其余内容不输出。`LlmContextSummarizer.ParseResponse()` 依赖这两个 tag 提取结果；如果缺失 `<summary>`，整个响应会被当作 summary 文本，`core-memory` 不更新。
2. **字数预算**：`<summary>` 建议 300–500 词，`<core-memory>` 建议 200–400 词，合计不超过 700 词。`LlmContextSummarizer` 设定 `MaxTokens = 1200`，超出会截断。
3. **语言一致性**：Prompt 用英文编写（模型对英文指令响应更稳定），summary 内容可以是中英混合。
4. **`<core-memory>` 结构**：每个 `##` section 都会在下次压缩时被完整覆盖，建议保持扁平列表，避免深层嵌套。
5. **`EnableCoreMemory = false` 时**：`<core-memory>` 段即使 LLM 生成了也会被忽略，可在 prompt 中省略该段以节省 token。

### KodaClaw 中的覆盖路径

KodaClaw 的三个 session service 均通过 `*SessionOptions.CompressionPrompt` 向下透传：

```
MainSessionOptions.CompressionPrompt           = ""（空，使用内置默认）
ChannelSessionOptions.DmCompressionPrompt      = ChannelCompressionPrompts.Dm
ChannelSessionOptions.GroupCompressionPrompt   = ChannelCompressionPrompts.Group
AutomationSessionOptions.CompressionPrompt     = AutomationCompressionPrompts.Default
```

Channel session 运行时按 `ChannelThreadType` 选择 prompt：
- `DirectMessage` → `DmCompressionPrompt`（Owner 信任级别，关注个人任务 + workspace 变更）
- `Group` → `GroupCompressionPrompt`（多参与者，关注群组上下文 + @Koda 的对话）

Prompt 常量集中在 `KodaClaw.Runtime/Sessions/CompressionPrompts.cs`，后续产品按需覆盖对应 Options 属性即可。

KodaClaw 阈值计算路径（不变）：

```
*SessionOptions.DefaultContextWindowSize = 128,000
ContextCompressionTriggerRatio = 0.75  →  MaxTokens = 96,000
ContextCompressionTargetRatio  = 0.40  →  CompressToTokens = 51,200
```

---

## 压缩后的消息结构示例

第一次压缩后：

```
[system] <core-memory>
  ## Current Task
  实现钉钉 ActionCard 消息发送
  ## Modified Files
  - src/.../DingTalkConnector.cs
  ## Key Decisions
  - Stream 模式优先于 Webhook
</core-memory>

[system] <context-summary timestamp="..." window="window-1">
  用户要求接入钉钉机器人。已完成 Stream 模式建连和基础文本收发...
</context-summary>

[user] 现在帮我加群聊支持
[assistant] 好的，我来看一下当前的实现...
...（最近消息）
```

第二次压缩后（summary 叠加）：

```
[system] <core-memory>（更新后）
[system] <context-summary window="window-1">（保留，永不删除）
[system] <context-summary window="window-2">（新增）
...（最近消息）
```

---

## 已知局限

- **Summary Stack 无上限**：多次压缩后摘要条目会累积。如果会话极长（10+ 次压缩），摘要本身可能占据较多 token。后续可考虑对超过 N 条的摘要做递归合并。
- **工具结果语义丢失**：`fs_read` 读取的文件内容不会保存在摘要中（仅记录文件路径）。如需跨压缩保留文件内容，使用 `IFilePool` + `RecoveredFile` 机制。
- **单模型上下文**：压缩调用使用同一 `IModelProvider`，不支持为压缩单独路由到不同 endpoint。如需此能力，可实现自定义 `IContextSummarizer`。

---

## 参考文献

| 文献 | 贡献 |
|------|------|
| [MemGPT: Towards LLMs as Operating Systems](https://arxiv.org/abs/2310.08560) | 三层虚拟内存架构，Core Memory 永不驱逐 |
| [LLMLingua-2: Data Distillation for Efficient and Faithful Task-Agnostic Prompt Compression](https://arxiv.org/abs/2403.12968) | 基于重要性的 token 预算分配，Budget Controller |
| [Language Model Tokenizers Introduce Unfairness Between Languages](https://arxiv.org/abs/2305.15425) | CJK 语言 token 倍率实测数据（NeurIPS 2023） |
| [Effective Context Engineering for AI Agents](https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents) | Claude Code 三层压缩策略（micro/auto/manual） |
| [Letta (formerly MemGPT)](https://github.com/letta-ai/letta) | Memory Block 实现参考，autonomous memory editing |
