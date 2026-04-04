# Kode.Agent.Tools.Orchestration

[English](./README.md) | 中文

Kode Agent SDK 编排工具包。提供 10 种多代理协调模式——隔离子代理、顺序流水线、并行调研、自我修正重试、专家委托、上下文蒸馏、验证修复循环、扇出/扇入、Map-Reduce 和对抗辩论——不依赖任何应用层基础设施。

## 安装

```bash
dotnet add package Kode.Agent.Tools.Orchestration
```

## 目录结构

```
src/Kode.Agent.Tools.Orchestration/
├── IsolateTask/          # isolate_task — 单次隔离子代理
├── Pipeline/             # pipeline — 顺序多阶段执行
├── ParallelResearch/     # parallel_research — 并发子代理
├── RetryWithReflection/  # retry_with_reflection — 故障感知重试
├── AskSpecialist/        # ask_specialist — 专家角色子代理
├── ContextDistill/       # context_distill — 大内容压缩摘要
├── ValidateAndFix/       # validate_and_fix — 执行-验证-修复循环
├── FanOutFanIn/          # fan_out_fan_in — 并行收集 + 综合分析
├── MapReduce/            # map_reduce — 分块并行 + 汇总
├── Debate/               # debate — 正反方 + 裁判辩论
├── Internal/             # SubAgentRunner、InMemoryAgentStore（内部共享）
└── ServiceCollectionExtensions.cs
```

## 工具说明

### isolate_task

在隔离子代理中运行任务。子代理的完整消息历史在完成后丢弃，只返回最终摘要。保护父代理的上下文窗口不被大量中间结果占满。

```json
{
  "tool": "isolate_task",
  "arguments": {
    "task": "列出 src/ 目录下所有 TODO 注释并按类别汇总。",
    "tools": ["fs_grep", "fs_list"],
    "maxIterations": 15
  }
}
```

---

### pipeline

按顺序执行多个阶段，每个阶段在隔离子代理中运行。上一阶段的摘要自动作为下一阶段的上下文前置。各阶段不共享上下文——只有摘要向前传递。

```json
{
  "tool": "pipeline",
  "arguments": {
    "stages": [
      { "name": "收集", "task": "扫描 workspace/memory/ 列出所有记忆文件。", "tools": ["fs_list"] },
      { "name": "整合", "task": "基于收集结果，识别重复项并提出合并方案。",  "tools": ["fs_read"] }
    ],
    "stopOnFailure": true
  }
}
```

---

### parallel_research

同时运行多个独立调研任务，每个任务在隔离子代理中执行。总耗时约等于最慢单个任务的时间。

```json
{
  "tool": "parallel_research",
  "arguments": {
    "tasks": [
      { "name": "认证模块", "task": "汇总 src/auth/ 的认证流程。" },
      { "name": "存储层",   "task": "汇总 src/storage/ 的存储逻辑。" }
    ],
    "maxConcurrency": 3,
    "failFast": false
  }
}
```

---

### retry_with_reflection

运行任务，失败时将错误信息连同反思提示词一起传给下次重试的子代理，让它诊断失败原因并换一种方式尝试。

| 属性 | 值 |
|------|----|
| 默认重试次数 | 2（共 3 次尝试） |
| 最大重试次数 | 5 |

```json
{
  "tool": "retry_with_reflection",
  "arguments": {
    "task": "找到日志系统的配置文件并报告当前日志级别。",
    "maxRetries": 2,
    "tools": ["fs_glob", "fs_read", "fs_grep"]
  }
}
```

> 即使最终失败也返回 `success: false` 和完整尝试记录，不抛出 Fail 错误。

---

### ask_specialist

将任务路由到注入了自定义 system prompt 的专家角色子代理。专家的完整上下文在完成后丢弃，只返回答案。

```json
{
  "tool": "ask_specialist",
  "arguments": {
    "task": "审查认证模块是否存在安全漏洞。",
    "specialistRole": "你是一位专注 OWASP Top 10 的高级应用安全工程师。",
    "tools": ["fs_read", "fs_grep"]
  }
}
```

---

### context_distill

将大量内容传给只推理（无工具）的子代理，压缩为聚焦摘要。适用于工具输出或累积上下文即将溢出时。

```json
{
  "tool": "context_distill",
  "arguments": {
    "content": "...一万行构建日志...",
    "focusQuestion": "测试失败的根本原因是什么？",
    "maxOutputWords": 200
  }
}
```

---

### validate_and_fix

运行任务，然后用独立验证子代理按结构化标准评估输出；若不符合预期则让执行子代理修正，最多循环 `maxFixRounds` 次。

| 属性 | 值 |
|------|----|
| 验证器 | 纯推理子代理，无工具 |
| 返回值 | 始终返回 `ToolResult.Ok`，检查 `passed` 字段 |

```json
{
  "tool": "validate_and_fix",
  "arguments": {
    "task": "生成日志系统的 JSON 配置文件。",
    "validationCriteria": "必须包含 level、sinks、format 三个键；level 只能是 debug/info/warn/error 之一。",
    "maxFixRounds": 2,
    "tools": ["fs_write", "fs_read"]
  }
}
```

---

### fan_out_fan_in

并行执行多个子任务（扇出），再用专属综合子代理分析所有结果（扇入）。综合代理接收所有任务摘要作为上下文。

```json
{
  "tool": "fan_out_fan_in",
  "arguments": {
    "tasks": [
      { "name": "前端", "task": "汇总 React 组件架构。" },
      { "name": "后端", "task": "汇总 API 层和中间件。" }
    ],
    "synthesisTask": "基于前后端摘要，识别集成痛点。",
    "maxConcurrency": 2
  }
}
```

---

### map_reduce

将大量条目拆分成 chunk，每个 chunk 由并行 map 子代理处理（用 `{item}` 占位），最后 reduce 子代理汇总所有 map 结果。

```json
{
  "tool": "map_reduce",
  "arguments": {
    "items": ["file1.md", "file2.md", "file3.md", "file4.md", "file5.md"],
    "mapTask": "读取 {item} 并提取其关键主题。",
    "reduceTask": "将所有提取的主题合并为一个去重后的主题列表。",
    "chunkSize": 2,
    "tools": ["fs_read"]
  }
}
```

---

### debate

运行正方和反方两个对抗辩论子代理（可多轮），再由裁判子代理评估论点并作出判决。强制暴露两方论据，提升高风险决策质量。

| 属性 | 值 |
|------|----|
| 子代理工具 | 无（纯推理） |
| 默认轮次 | 1，最多 3 轮 |

```json
{
  "tool": "debate",
  "arguments": {
    "topic": "我们是否应该将公开 API 从 REST 迁移到 GraphQL？",
    "contextInfo": "当前 API 约 200 个端点，主要消费方是移动客户端。",
    "rounds": 2
  }
}
```

---

## 使用方式

### 注册所有编排工具

```csharp
using Kode.Agent.Tools.Orchestration;

toolRegistry.RegisterOrchestrationTools(
    modelProvider,
    modelId,
    sandboxFactory,
    loggerFactory); // 可选
```

### 通过依赖注入（如 KodaClaw）

```csharp
services.AddKodaClawRuntime(options =>
{
    options.DefaultModel = "gpt-4o-mini";
    // ISandboxFactory 可用时自动注册编排工具
});
```

### 子代理工具白名单

所有编排工具生成的子代理均受以下硬编码白名单限制，不论调用方请求什么工具：

```
fs_read  fs_glob  fs_grep  fs_list
bash_run  bash_logs  bash_kill
todo_read
```

写工具（`fs_write`、`fs_edit`、`fs_rm`）和渠道工具（`channel_send`）**永远不会**授予子代理。

## 设计模式对照

每个工具对应 [Anthropic《构建有效代理》](https://www.anthropic.com/research/building-effective-agents) 中的一种模式：

| 工具 | 模式 |
|------|------|
| `isolate_task` | Orchestrator-Worker |
| `pipeline` | Pipeline |
| `parallel_research` | Parallelization |
| `retry_with_reflection` | Reflection |
| `ask_specialist` | Specialist Delegation |
| `context_distill` | Context Distillation |
| `validate_and_fix` | Evaluate-and-Fix Loop |
| `fan_out_fan_in` | Fan-Out / Fan-In |
| `map_reduce` | Map-Reduce |
| `debate` | Adversarial Debate |

## 许可证

本包属于 Kode Agent SDK 项目的一部分，MIT 许可证。
