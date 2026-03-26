# Iteration 58 FREEZE — 记忆系统 Phase 1（会话摘要 + 元数据基座 + 冷热分离）

**日期**：2026-03-26
**类型**：新功能（新增用户可感知能力）
**范围**：`KodaClaw.Storage` / `KodaClaw.Runtime` / `KodaClaw.Workspace` / `KodaClaw.Gateway/skills`
**设计方案**：`docs/记忆系统设计方案.md`

---

## 背景与动机

当前记忆系统存在三个核心问题：

1. **会话记忆丢失**：主会话轮转/结束后，对话内容仅保留在 `sessions/` 目录的原始 JSONL 中，无结构化摘要，Agent 无法在后续 session 中回顾历史讨论
2. **记忆无遗忘机制**：MEMORY.md 只增不减（依赖 Agent 手动整理），最终超出 200 行限制或充满过时信息
3. **历史不可追溯**：记忆条目无来源链接，无法追踪"这条记忆是从哪次对话产生的"

本迭代建立记忆系统的基础设施层，后续迭代在此基础上扩展 Auto Dream 整合管道和 topics/ 索引。

---

## 关键设计决策

### 1. MEMORY.md 保持纯 Markdown，元数据存 SQLite

MEMORY.md 继续由 Agent 通过 `workspace_protocol_update(target=memory)` 自由写入纯 markdown，保持 workspace-as-protocol 的文件可读性。

结构化元数据（优先级 P0-P3、状态 active/dormant/archived、last_accessed、来源路径）存入 SQLite `memory_entries` 表，由代码层维护，Agent 不感知。

两者通过 `key`（MEMORY.md 中 `## section` 的标识）松耦合。

**不做**：在 MEMORY.md 中嵌入 YAML frontmatter 或结构化字段。

### 2. 引用追踪仅限显式工具操作

仅在以下工具执行时更新 `last_accessed`：
- `workspace_read(target=memory)` → 更新全部 active 条目
- `workspace_protocol_update(target=memory, section=X)` → 更新对应 section
- `workspace_read(target=memory_search)` → 更新匹配到的条目

不做关键词匹配或 LLM 语义判断。零误判、实现简单。

### 3. 会话摘要在 session 轮转时由 LLM 生成

复用 `ChannelThreadSummaryWriter` 的 LLM 压缩模式。摘要写入 `workspace/memory/sessions/{date}-{type}-{shortId}.md`。

低价值会话跳过规则：
- 消息数 < 5 → 跳过
- Automation session → 跳过（结果在 Inbox）
- 纯自动轮转（workspace rotation）无用户消息 → 跳过

### 4. 冷热分离由代码调度，降级文件存 workspace

降级逻辑在 Nightly Memory Consolidation automation 完成后由代码层执行（后处理钩子）：
- 解析当前 MEMORY.md 的 `## section` → 同步到 SQLite `memory_entries`
- 按 priority + last_accessed 判断是否降级
- 降级：从 MEMORY.md 移除 section → 写入 `workspace/memory/dormant/{key}.md`
- 二次降级：dormant → `workspace/memory/archive/{key}.md`

升级：当 `workspace_read(target=memory_search)` 匹配到 dormant/archive 文件时，更新 status → active，下次整合时重新纳入 MEMORY.md。

### 5. 已有记忆的兼容迁移

**场景**：用户已有 workspace 中存在 MEMORY.md 内容和 daily logs。

**迁移策略**（在 `WorkspaceService.EnsureInitializedAsync` 中执行）：

1. **新目录创建**：幂等创建 `workspace/memory/sessions/`、`workspace/memory/topics/`、`workspace/memory/dormant/`、`workspace/memory/archive/`
2. **SQLite 表创建**：`memory_entries` 表在 `control-plane.db` 中幂等创建
3. **存量 MEMORY.md 导入**：首次检测到 `memory_entries` 表为空但 MEMORY.md 非默认内容时，解析 `## section` → 每个 section 生成一条 `memory_entries` 记录（priority=2, status=active, last_accessed=now, source_path=null）
4. **HEARTBEAT.md 不覆盖**：已有 workspace 的 HEARTBEAT.md 保持不变（用户可能已自定义），新 workspace 使用更新后的模板
5. **旧目录兼容**：`workspace/memory/facts/` 和 `workspace/memory/conversations/` 保留不删（可能有用户手动放的文件），但不再在代码中引用

**迁移标记**：在 `WorkspaceAppConfig` 中新增 `MemorySchemaVersion: int`（默认 0）。迁移完成后设为 1，避免重复执行。

---

## 范围（IN SCOPE）

### KC-5801：`memory_entries` SQLite 表 + `IMemoryMetadataRepository`

**模块**：`KodaClaw.Storage`

- `memory_entries` 表 DDL（id, key, title, priority, status, last_accessed, created_at, source_path, topic, demoted_at, updated_at）
- `IMemoryMetadataRepository` 接口（Upsert, GetByKey, ListByStatus, ListDueForDemotion, UpdateLastAccessed, UpdateStatus, Delete）
- `SqliteMemoryMetadataRepository` 实现
- 幂等表创建（与 control-plane.db 的其他表共存）

### KC-5802：存量 MEMORY.md 迁移 + 新目录创建

**模块**：`KodaClaw.Workspace` + `KodaClaw.Contracts`

- `WorkspaceAppConfig` 新增 `MemorySchemaVersion` 字段
- `KodaClawWorkspaceLayout` 新增 `MemorySessionsDirectory`、`MemoryTopicsDirectory`、`MemoryDormantDirectory`、`MemoryArchiveDirectory` 常量
- `WorkspaceService.RequiredDirectories` 追加 4 个新目录
- `MemoryMigrationService`：检测 `MemorySchemaVersion==0` → 解析 MEMORY.md sections → 批量写入 `memory_entries` → 设 `MemorySchemaVersion=1`
- MEMORY.md section 解析器：正则匹配 `^## (.+)$`，提取 section title 和 body

### KC-5803：`ISessionSummaryService` + LLM 摘要生成

**模块**：`KodaClaw.Runtime`

- `ISessionSummaryService` 接口 + `SessionSummaryService` 实现
- 摘要生成 prompt：输入消息历史 → 输出 JSON（topics, keywords, decisions, user_expressions, follow_ups）
- 摘要写入 `workspace/memory/sessions/{date}-{type}-{shortId}.md`（结构化 markdown）
- 低价值会话过滤（< 5 条消息 / automation / 无用户消息）
- `SessionSummaryContext` record（SessionType, BindingId, Messages）
- `SessionSummary` record（所有结构化字段）

### KC-5804：Session 轮转时触发摘要生成

**模块**：`KodaClaw.Runtime`

- `MainSessionService.RotateMainSessionAsync` 在 dispose 旧 agent 前调用 `ISessionSummaryService.GenerateSummaryAsync`
- `ChannelSessionService.RotateSessionAsync` 同样触发
- 摘要生成失败不阻塞轮转（try-catch，记录诊断事件）
- 摘要生成后在 `memory_entries` 中插入条目（source_path 指向摘要文件）

### KC-5805：引用追踪 + workspace_read(target=memory_search)

**模块**：`KodaClaw.Runtime`

- `WorkspaceReadTool` 新增 target `memory_search`：接受 query 参数，在 MEMORY.md + dormant/ + archive/ + topics/ 中做关键词匹配（`string.Contains` 忽略大小写）
- `WorkspaceReadTool` 执行 target=memory 时调用 `_memoryMetadataRepository?.UpdateLastAccessedAsync(...)` 更新全部 active 条目
- `WorkspaceProtocolUpdateTool` 执行 target=memory 且指定 section 时更新对应条目的 last_accessed
- dormant/archive 中匹配到的条目自动升级 status → active

### KC-5806：冷热分离调度（PostConsolidation 后处理）

**模块**：`KodaClaw.Runtime`

- `IMemoryConsolidationService` 接口 + `MemoryConsolidationService` 实现
- `PostConsolidationAsync`：
  1. 解析当前 MEMORY.md sections → 同步到 `memory_entries`（新增/更新）
  2. 遍历 active 条目，按 priority 降级规则检查 last_accessed
  3. 降级：从 MEMORY.md 删除 section → 写入 `dormant/{key}.md`（或 P3 直接 → `archive/`）
  4. 扫描 dormant/ 中 status=active 的条目（已被搜索升级） → 提取内容准备下次整合纳入
- `AutomationSessionService` 完成 "Memory Consolidation" 类型 automation 后触发 `PostConsolidationAsync`
- 降级后 git commit

### KC-5807：`koda-memory` 技能 v2.0 + HEARTBEAT 模板更新

**模块**：`KodaClaw.Gateway/skills` + `KodaClaw.Workspace`

- `koda-memory/SKILL.md` v2.0：
  - 新增会话摘要说明（session 轮转自动生成，无需 Agent 手动触发）
  - 新增 `workspace_read(target=memory_search)` 用法（召回温/冷记忆）
  - 新增来源溯源说明（MEMORY.md 条目应附 `sessions/` 摘要链接）
  - 新增降级机制说明（P0-P3 优先级的含义，Agent 在整合时应标注优先级）
- `DefaultWorkspaceTemplates.Heartbeat()` 更新 Nightly Memory Consolidation prompt：
  - 新增读取 `workspace/memory/sessions/` 未整合摘要的步骤
  - 新增为条目标注来源链接和优先级建议的步骤
  - 仅影响新 workspace

### KC-5808：测试

**模块**：Tests

- `SqliteMemoryMetadataRepositoryTests`（L1, ~8 个：CRUD + 降级查询 + 幂等）
- `MemoryMigrationServiceTests`（L1, ~6 个：空 MEMORY / 多 section / 默认模板跳过 / 幂等）
- `SessionSummaryServiceTests`（L1, ~6 个：正常生成 / 低价值跳过 / LLM 失败降级）
- `MemoryConsolidationServiceTests`（L1, ~6 个：降级规则 / P0 不降级 / 升级 / section 同步）
- `MemorySearchTests`（L1, ~4 个：关键词匹配 / 跨目录 / 升级触发）
- `SessionRotationSummaryIntegrationTests`（L2, ~4 个：轮转触发摘要 / 失败不阻塞）
- `MemoryMigrationContractTests`（L3, ~4 个：迁移前后 MEMORY.md 不变 / SQLite 数据一致）
- 全量回归通过

---

## 范围外（OUT OF SCOPE）

- **topics/ 自动生成**：Phase 2（Iter 59），由 Auto Dream 管道在整合时维护
- **前端 MemoryDesk UI**：Phase 2+，优先级低
- **原始会话 30 天 TTL 清理**：Phase 2（已有 `SessionRetentionService` KC-4802 可扩展）
- **摘要隐私脱敏**：Phase 2
- **跨 workspace 记忆**：不做
- **向量索引 / 语义搜索**：不做，关键词匹配足够（本地文件数量有限）

---

## 迁移矩阵

| 场景 | 处理方式 |
|------|---------|
| 全新 workspace（首次启动） | 新目录 + 空 `memory_entries` 表 + `MemorySchemaVersion=1` |
| 已有 workspace，MEMORY.md 非空 | 解析 sections → 写入 `memory_entries`（P2/active/now） → `MemorySchemaVersion=1` |
| 已有 workspace，MEMORY.md 为默认模板 | 跳过导入 → `MemorySchemaVersion=1` |
| 已有 daily logs（`memory/2026-03-*.md`） | 保留不动，Nightly Consolidation 正常处理 |
| 已有 HEARTBEAT.md（用户已自定义） | 不覆盖，用户可手动更新 prompt |
| `memory/facts/` `memory/conversations/` 有文件 | 保留不删，但代码不再引用 |

---

## 风险

| 风险 | 影响 | 缓解 |
|------|------|------|
| LLM 摘要生成延迟导致轮转变慢 | 用户感知到 session 切换延迟 | 摘要异步生成（fire-and-forget），不阻塞轮转 |
| MEMORY.md section 解析不准确 | 迁移遗漏或错误 | 正则简单（`^## `），边界清晰；迁移后 MEMORY.md 内容不变 |
| Agent 整合时不遵循 koda-memory 指导 | 来源链接、优先级标注缺失 | 降级逻辑不依赖 Agent 标注（代码用 SQLite 元数据），Agent 标注是锦上添花 |
| 降级后用户找不到记忆 | 记忆"消失"感 | `memory_search` 搜索全层；降级只是从 MEMORY.md 移出，文件仍在 |

---

## 非目标

- 不改变现有 `workspace_memory_append` 和 `workspace_protocol_update` 的接口
- 不改变现有 MEMORY.md 的格式要求（Agent 继续自由写 markdown）
- 不改变 Automation 系统的执行模型（仍然是 prompt-driven）
- 不引入新的外部依赖（复用 SQLite、LLM provider）

---

## 修订记录

**2026-03-26 — 后续重构**：Iter 58 引入的 SQLite `memory_entries` 表、`IMemoryMetadataRepository`、`MemoryMigrationService` 已在后续优化中移除，改为纯文件方案（`IMemoryFileService` + `MemoryFrontmatterParser`）。详见 `docs/记忆系统设计方案.md` 附录 A。

**2026-03-26 — 优先级标签语义化**：P0-P3 数字优先级替换为语义标签（permanent/lasting/standard/ephemeral）。`MemoryFrontmatterParser` 兼容旧数字格式自动转换。

**2026-03-26 — 可靠性优化**：`SessionSummaryService` 新增 3 次重试 + pending 文件延迟重试机制；摘要截断策略从取前 12K 改为首 2K + 尾 10K；`PostConsolidationAsync` 记录 `LastConsolidationAt` 用于健康监控。详见 `docs/记忆系统设计方案.md` 附录 B。
