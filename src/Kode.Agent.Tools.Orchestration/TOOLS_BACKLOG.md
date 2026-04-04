# Orchestration Tools Backlog

所有计划工具均已实现，详见 [README.md](./README.md)。

## 已实现工具（共 10 个）

| 工具 | 模式 | 状态 |
|------|------|------|
| `isolate_task` | Orchestrator-Worker | ✅ 已实现 |
| `pipeline` | Pipeline | ✅ 已实现 |
| `parallel_research` | Parallelization | ✅ 已实现 |
| `retry_with_reflection` | Reflection | ✅ 已实现 |
| `ask_specialist` | Specialist Delegation | ✅ 已实现 |
| `context_distill` | Context Distillation | ✅ 已实现 |
| `validate_and_fix` | Evaluate-and-Fix Loop | ✅ 已实现 |
| `fan_out_fan_in` | Fan-Out / Fan-In | ✅ 已实现 |
| `map_reduce` | Map-Reduce | ✅ 已实现 |
| `debate` | Adversarial Debate | ✅ 已实现 |

## 未来可扩展的方向

- **`checkpoint_and_resume`**：在长 pipeline 中保存检查点，失败后从检查点续跑而非从头重试
- **`tournament`**：多个候选方案各自独立执行，裁判子代理选出最优结果（适合代码生成质量评选）
- **`multi_agent_debate`**：三方以上辩论（适合大型架构决策）
