# Iteration 20 FREEZE：Memory Pipeline & Session Continuity

## 冻结日期
2026-03-21

## 动机

经过 18 个迭代，KodaClaw 已建立起记忆架构的完整骨架：`workspace_memory_append` 工具、`memory/YYYY-MM-DD.md` 日记文件、MEMORY.md 长期索引、HEARTBEAT.md 自动整合任务、SUMMARY.md 渠道线程摘要。但这条流水线有四处断点，导致系统在持续使用下逐渐退化：

1. **SDK ContextManager 从未启用**：主会话 context 只增不减，最终会触达模型硬性 token 上限导致会话不可用，而 SDK 已内置在线压缩机制却从未接入
2. **Nightly Memory Consolidation 默认关闭**：`workspace_memory_append` 每天写文件，但 HEARTBEAT.md 里的整合 automation 默认 `enabled: false`，MEMORY.md 永远停在 bootstrap 初始值
3. **SUMMARY.md 无限增长**：每个 channel turn 追加一行，没有截断，最终撑爆 system prompt 字符预算，反而丢失最新上下文
4. **Daily memory 跨日期缺口**：主会话只加载 `memory/{今天}.md`，跨天对话或间隔使用时 `memory/{昨天}.md` 里的内容会被遗漏

## 用户可感知的变化

- 主会话长时间使用后不再因 token 超限而卡死或行为异常
- 多天使用后，MEMORY.md 开始自动积累已学到的事实（无需手动 enable automation）
- Telegram 等 channel 的对话历史保持清晰可读，不会因为消息过多而膨胀失控
- 昨天和今天的对话记忆在同一个主会话中不再断层

## 范围

### 在范围内

| KC | 描述 |
|----|------|
| KC-2001 | 主会话启用 SDK ContextManager（在线 context 压缩） |
| KC-2002 | Nightly Memory Consolidation 改为默认 enabled |
| KC-2003 | SUMMARY.md 滚动窗口（最多 60 行，截头保尾） |
| KC-2004 | 主会话跨日期 daily memory 加载（今天 + 昨天） |

同时补录两个本迭代前已完成的 bug fix：
- `KC-BUG-001`：Skills sandbox AllowPaths 修复（三层路径无法被 SkillsLoader 扫描）
- `KC-BUG-002`：SUMMARY.md 内容从硬编码字符串改为真实对话摘要（`channel_send` capture）

### 不在范围内

- Session lifecycle UI（rotate 按钮、archive、context fill 指示器）→ Iteration 21
- Channel session 时效策略（超过 N 天未活动时放弃 resume）→ Iteration 21
- SUMMARY.md 在 session 有完整历史时跳过加载（token 优化）→ Iteration 21
- Automation 跨次运行状态文件（`LAST_RUN.md`）→ Iteration 22
- Channel 长期记忆（SUMMARY.md 压缩为段落摘要，而非截断）→ Iteration 22
- ContextManager 用于 channel / automation session → Iteration 21（channel session 生命周期更短，优先级低）

## 关键设计决定

| 问题 | 决定 | 理由 |
|------|------|------|
| ContextManager 压缩模型 | 使用与主会话相同的配置模型，通过 `MainSessionOptions.CompressionModel` 可覆盖，默认 `claude-haiku-4-5-20251001` | 避免写死旧模型名；haiku 足够压缩摘要，成本低 |
| ContextManager 阈值 | MaxTokens=80_000，CompressToTokens=50_000 | 主会话 system prompt 本身就有 4-8k token，给对话历史留足空间；不要太激进压缩 |
| Nightly consolidation 默认 enabled | 是 | 这是产品设计的核心路径，不该是可选功能；已有用户的 HEARTBEAT.md 不自动迁移（需手动 enable） |
| SUMMARY.md 截断策略 | 保留最后 60 行（约 30 轮对话），截头保尾 | 最近的对话最有价值；60 行约 6-8k token，在字符预算内 |
| 跨日期加载策略 | 加载今天 + 昨天（2 个文件，最多） | 覆盖跨午夜对话和隔天使用；加载更多天会让 system prompt 过重 |
| 昨天文件不存在时 | 静默跳过，不报错 | 首次使用或恰好今天才有记忆时正常 |

## 契约变更

### 新增配置字段（非破坏性）

`MainSessionOptions` 新增：
```csharp
public string? CompressionModel { get; init; }  // null = 使用主模型
public int ContextMaxTokens { get; init; } = 80_000
public int ContextCompressToTokens { get; init; } = 50_000
```

### SUMMARY.md 格式（向后兼容）

截断只影响旧行数超过 60 的情况，格式本身不变：
```
- [timestamp] delivered: user: "..." → koda: "..."
```

### HEARTBEAT.md 模板变更

`Nightly Memory Consolidation` 的 `enabled: false` → `enabled: true`（仅影响新 workspace）

## 验收标准

1. 主会话连续运行 200+ 轮后，ContextManager 触发压缩，会话继续正常工作，不报 token 超限错误
2. 新创建 workspace 的 HEARTBEAT.md 中，Nightly Memory Consolidation 默认 `enabled: true`
3. SUMMARY.md 超过 60 行时，新增一行后文件仍为 60 行（截头）
4. 主会话 system prompt 包含昨天的 daily memory 文件（如果存在）
5. `dotnet test KodaClaw.sln -m:1` 全量通过

## 关键文件汇总

| 文件 | 操作 |
|------|------|
| `src/KodaClaw.Runtime/MainSessionOptions.cs` | 修改（新增 CompressionModel / ContextMaxTokens / ContextCompressToTokens） |
| `src/KodaClaw.Runtime/MainSessionService.cs` | 修改（CreateAgentConfig 加 Context；BuildSystemPromptAsync 加昨天文件） |
| `src/KodaClaw.Workspace/DefaultWorkspaceTemplates.cs` | 修改（Nightly consolidation enabled: true） |
| `src/KodaClaw.ChannelHub/ChannelThreadSummaryWriter.cs` | 修改（WriteAsync 写入后截断到 60 行） |
| `tests/KodaClaw.UnitTests/Runtime/MainSessionOptionsTests.cs` | 新增（ContextManager 默认值验证） |
| `tests/KodaClaw.UnitTests/ChannelHub/ChannelThreadSummaryWriterTests.cs` | 修改（加滚动窗口测试） |
| `tests/KodaClaw.ContractTests/Workspace/WorkspaceTemplateContractTests.cs` | 修改（验证 Nightly consolidation 默认 enabled） |
