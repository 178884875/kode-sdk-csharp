---
name: koda-orchestration
description: 高级多代理编排工具——扇出/扇入并行分析、Map-Reduce 大批量处理、对抗辩论决策支持
license: built-in
compatibility: KodaClaw 1.x
allowed-tools: fan_out_fan_in map_reduce debate
metadata:
  kind: builtin-core
  version: "1.0"
  tags: "orchestration, multi-agent, parallelization, debate, map-reduce"
---

# KodaClaw Orchestration — 高级编排工具指南

激活本 skill 后，以下三个重型编排工具将解锁：`fan_out_fan_in`、`map_reduce`、`debate`。

> **基础编排工具**（`isolate_task`、`pipeline`、`parallel_research`、`retry_with_reflection`、
> `ask_specialist`、`context_distill`、`validate_and_fix`）**始终可用**，无需激活本 skill。

---

## fan_out_fan_in — 扇出/扇入

**适用场景**：需要先并行收集多个维度的信息，再综合分析得出结论。

```
fan_out_fan_in(
  tasks=[
    { name="前端", task="汇总 React 组件架构和状态管理方式" },
    { name="后端", task="汇总 API 层、中间件和数据库访问模式" },
    { name="测试", task="汇总测试覆盖率和测试策略" }
  ],
  synthesisTask="基于以上三个维度，识别系统最脆弱的集成点并给出加固建议",
  maxConcurrency=3
)
```

**与 `parallel_research` 的区别**：`parallel_research` 只并行收集，不综合；`fan_out_fan_in` 有专属的综合代理（fan-in），适合需要跨维度分析的场景。

---

## map_reduce — 大批量处理

**适用场景**：对大量同质数据（文件列表、记忆条目、日志行）做批量分析后汇总。

```
map_reduce(
  items=["memory/session-001.md", "memory/session-002.md", ..., "memory/session-050.md"],
  mapTask="读取 {item}，提取其中的关键决策和行动项",
  reduceTask="将所有提取的决策和行动项去重合并，按主题分组",
  chunkSize=5,
  tools=["fs_read"]
)
```

**占位符规则**：`mapTask` 中的 `{item}` 会被替换为每个 chunk 的内容列表。

**典型用途**：
- KodaClaw 记忆整合：批量分析 50+ 个 session 文件
- 代码审查：批量分析多个模块后汇总问题
- 日志分析：分批处理大量日志行

---

## debate — 对抗辩论

**适用场景**：面临高风险决策、技术方案选型、架构权衡时，强制暴露正反两方论据。

```
debate(
  topic="是否将认证系统从 JWT 迁移到 Session-based？",
  contextInfo="当前系统：50k DAU，JWT 存储在 localStorage，历史上有 XSS 攻击记录。",
  rounds=2
)
```

**执行流程**：
1. 正方子代理论证（AllowNoTools，纯推理）
2. 反方子代理论证（看到正方论点，纯推理）
3. 若 `rounds > 1`：正方反驳 → 反方再反驳
4. 裁判子代理综合双方论点，给出最终判决

**返回值中的 `verdict`** 是裁判的综合判断，可直接引用为决策依据。

---

## 选择指南

| 场景 | 推荐工具 |
|------|---------|
| 多个独立问题，不需要综合 | `parallel_research`（始终可用） |
| 多维度收集 + 综合分析 | `fan_out_fan_in` ← 本 skill |
| 大量同质数据批量处理 | `map_reduce` ← 本 skill |
| 高风险决策需要辩证论证 | `debate` ← 本 skill |
| 顺序依赖的多步骤任务 | `pipeline`（始终可用） |
