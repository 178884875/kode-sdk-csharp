# Iteration 59 FREEZE — 记忆系统 Phase 2（Auto Dream 管道 + Topics 索引 + 清理与隐私）

**日期**：2026-03-26
**类型**：新功能（新增用户可感知能力）
**范围**：`KodaClaw.Runtime` / `KodaClaw.Automation` / `KodaClaw.Gateway` / `kodaclaw-web`
**前置**：Iter 58（记忆系统 Phase 1）完成

---

## 背景与动机

Iter 58 建立了记忆系统的基础设施：会话摘要生成、SQLite 元数据、冷热分离调度、workspace_read(memory_search) 召回。

本迭代在此基础上完成记忆系统的"智能层"：

1. **Auto Dream 整合管道**：将 Nightly Consolidation 从单一 prompt 升级为结构化多阶段管道，Agent 整合日志和会话摘要后，代码层执行后处理（降级、元数据同步、清理）
2. **Topics 索引**：Auto Dream 整合时自动创建/更新 `workspace/memory/topics/` 文件，提供按主题聚合的会话时间线视图
3. **原始会话 TTL 清理**：已有摘要的原始 session 超过 30 天后自动删除，释放存储
4. **摘要隐私脱敏**：会话摘要生成时过滤敏感信息，支持用户标记私密会话

---

## 关键设计决策

### 1. Auto Dream 仍然是 Agent prompt-driven + 代码后处理

不将 Auto Dream 变成纯代码管道。Agent 通过 LLM 做语义理解（合并重复、解决矛盾、串联主题），代码做结构化操作（降级调度、元数据同步、文件清理）。

**流程**：
```
HEARTBEAT Nightly Consolidation 触发
    ↓
Agent 执行整合 prompt（读日志 + 读摘要 + 读 MEMORY.md → 写 MEMORY.md + 写 topics/）
    ↓
AutomationSessionService 检测完成
    ↓
PostConsolidationAsync（代码层，Iter 58 已有）
    ├─ 同步 MEMORY.md sections → SQLite
    ├─ 执行降级规则
    ├─ 清理过期 session 目录
    └─ git commit
```

### 2. Topics 仅在 Auto Dream 时维护

Agent 在 Nightly Consolidation prompt 中被指导创建/更新 topics/ 文件。日常对话中不直接操作 topics/。

Topics 文件格式固定：
```markdown
# {主题名}

## 概述
{一句话描述}

## 相关会话
- {date}：{摘要}（来源：sessions/{file}）

## 当前状态
{最新进展}
```

Agent 通过 `fs_write` 写入 topics/ 文件（workspace 目录在 automation sandbox 可写范围内）。

### 3. 原始会话清理复用 SessionRetentionService

Iter 48 已有 `SessionRetentionService`（KC-4802），负责 auto-* session 的 TTL 清理。扩展其逻辑：
- 新增规则：main-* session 如果在 `workspace/memory/sessions/` 中有对应摘要文件，且 session 创建时间 > 30 天 → 可删除
- 活跃 session（`ActiveMainSessionId`）永远跳过

### 4. 隐私脱敏在摘要生成 prompt 中完成

不做代码层正则脱敏（误判率高）。在 `SessionSummaryService` 的 LLM prompt 中加入指令：
- 不在摘要中包含密码、API Key、私钥等敏感信息
- 用户在对话中说"不用记"/"这个别记"的内容不纳入摘要
- 支持 `privacy_level: private` 标记（未来扩展，当前不暴露 UI）

---

## 范围（IN SCOPE）

### KC-5901：HEARTBEAT 模板升级 + Auto Dream 整合指导

**模块**：`KodaClaw.Workspace` + `KodaClaw.Gateway/skills`

- `DefaultWorkspaceTemplates.Heartbeat()` Nightly Memory Consolidation prompt 重写为多阶段指令：
  1. 读取今日日志 + 未整合的会话摘要（`workspace/memory/sessions/`）
  2. 读取当前 MEMORY.md
  3. 整合：合并重复、解决矛盾（保留时间线演变）、更新已有 section
  4. 为跨会话反复出现的主题创建/更新 `workspace/memory/topics/{topic}.md`
  5. 每条记忆附来源链接（`→ sessions/{file}`）
  6. 写入 MEMORY.md（≤200 行），删除已处理的日志
- `koda-memory/SKILL.md` v2.1：新增 topics/ 说明（只读，Auto Dream 维护；日常对话中可通过 `workspace_read(target=memory_search)` 搜索）

### KC-5902：AutomationSessionService 后处理钩子

**模块**：`KodaClaw.Automation` + `KodaClaw.Runtime`

- `AutomationSessionService` 在 automation run 完成后检测是否为 Memory Consolidation 类型
- 检测逻辑：`definition.Title` 包含 "Memory Consolidation" 或 "记忆整合"（大小写不敏感）
- 命中后调用 `IMemoryConsolidationService.PostConsolidationAsync()`（Iter 58 KC-5806 已实现接口）
- 后处理失败不影响 automation run 状态（try-catch + 诊断事件）

### KC-5903：Topics 目录管理 + workspace_read 支持

**模块**：`KodaClaw.Runtime`

- `workspace_read(target=topics)` 新增 target：列出 `workspace/memory/topics/` 下所有 `.md` 文件标题
- `workspace_read(target=topics, path="soul-store")` 读取指定 topic 文件内容
- `workspace_read(target=memory_search)` 搜索范围确认包含 topics/（Iter 58 KC-5805 已覆盖）
- Topics 文件在 `PostConsolidationAsync` 中同步到 `memory_entries`（topic 字段填充）

### KC-5904：原始会话 TTL 清理扩展

**模块**：`KodaClaw.Gateway`

- 扩展 `SessionRetentionService`：
  - 新增 main-* session 清理规则：有对应摘要 + 创建 > 30 天 → 可删除
  - 匹配逻辑：从 `workspace/memory/sessions/` 文件名中提取 session ID，与 `sessions/` 目录下的文件夹匹配
  - 活跃 session（`ActiveMainSessionId`）永远跳过
  - Channel session：有 SUMMARY.md + 创建 > 30 天 → 可删除（binding 保留）
- 清理后记录诊断事件 `session_retention.cleaned`（含清理数量和释放空间）

### KC-5905：摘要隐私脱敏 + 私密会话标记

**模块**：`KodaClaw.Runtime`

- `SessionSummaryService` 的 LLM prompt 追加隐私指令：
  - 不包含密码、API Key、token、私钥、信用卡号等敏感模式
  - 用户在对话中明确表示"不用记"/"别记这个"/"private" 的内容不纳入
- `SessionSummary` record 新增 `PrivacyLevel` 字段（"normal" | "private"）
- 私密会话的摘要只记录基本信息（日期、类型、时长），不记录具体内容
- 检测逻辑：对话中出现"不要记录"/"这个保密"/"private session" → 标记 private

### KC-5906：前端 Memory 可观测性（Settings Desk 扩展）

**模块**：`kodaclaw-web` + `KodaClaw.Gateway`

- Gateway 端点：
  - `GET /api/memory/stats` → 返回 `MemoryStats`（active/dormant/archived 条目数、最近整合时间、topics 数量、sessions/ 摘要数量）
  - `GET /api/memory/entries?status=active` → 返回 `memory_entries` 列表（分页）
  - `POST /api/memory/entries/{key}/promote` → 手动升级 dormant/archived → active
- Settings Desk "记忆" 分区：
  - 统计卡片：活跃/温/冷记忆条目数
  - 条目列表（按 last_accessed 倒序）：key、priority badge、status badge、last_accessed
  - 降级条目可手动"激活"（调 promote API）
  - 最近整合时间戳

### KC-5907：测试

**模块**：Tests

- `AutoDreamPostProcessingTests`（L1, ~6 个：钩子触发 / 标题匹配 / 失败不阻塞 / topics 同步）
- `TopicsWorkspaceReadTests`（L1, ~4 个：列出 / 读取 / 空目录）
- `SessionRetentionExtensionTests`（L1, ~6 个：main 有摘要+过期→删 / main 无摘要→保留 / 活跃→跳过 / channel 清理）
- `SummaryPrivacyTests`（L1, ~4 个：private 标记检测 / 私密摘要只含基本信息）
- `MemoryStatsEndpointTests`（L2, ~4 个：stats 返回 / entries 列表 / promote 升级）
- `MemoryStatsContractTests`（L3, ~3 个：DTO schema 验证）
- 全量回归通过

---

## 范围外（OUT OF SCOPE）

- **MemoryDesk 独立页面**：本迭代仅在 Settings Desk 加分区，不做独立 Desk
- **topics/ 手动编辑 UI**：topics/ 是 Auto Dream 维护的，用户通过文件系统编辑
- **向量索引 / Embedding 搜索**：不做，关键词匹配 + topics/ 结构化索引足够
- **跨 workspace 记忆同步**：不做
- **Auto Dream 手动触发 UI 按钮**：用户可通过 HEARTBEAT 的手动触发机制触发（已有）

---

## 依赖关系

```
Iter 58（Phase 1）
  ├─ KC-5806 IMemoryConsolidationService ──→ KC-5902 后处理钩子调用
  ├─ KC-5805 workspace_read(memory_search) ──→ KC-5903 topics/ 搜索覆盖
  ├─ KC-5801 memory_entries 表 ──→ KC-5906 前端展示
  └─ KC-5803 SessionSummaryService ──→ KC-5905 隐私脱敏
```

---

## 风险

| 风险 | 影响 | 缓解 |
|------|------|------|
| Agent 整合 prompt 过长导致 token 超限 | 日志+摘要+MEMORY.md 总量可能超出 context | 在 prompt 中限制：只读最近 7 天日志 + 最近 10 条未整合摘要 |
| Topics 文件数量膨胀 | workspace 可读性下降 | Auto Dream prompt 指导：合并相近主题，总数控制在 ~20 个以内 |
| 私密检测误判 | 正常内容被标记为 private | 只检测显式关键词（"不要记录"/"保密"/"private"），不做语义推断 |
| SessionRetentionService 误删 | 删除了仍有价值的 session | 双重保护：必须有摘要文件 + 必须超 30 天；活跃 session 永远跳过 |

---

## 非目标

- 不改变 Auto Dream 的触发机制（仍然走 HEARTBEAT cron 调度）
- 不改变 `workspace_memory_append` 和 `workspace_protocol_update` 的接口
- 不引入新的外部依赖

---

## 修订记录

**2026-03-26 — 后续重构**：Iter 59 引入的 `PostConsolidationAsync` SQLite 同步逻辑（SyncMemoryFileToSqliteAsync、SyncTopicsToSqliteAsync、DemoteStaleEntriesAsync）已在后续优化中移除。PostConsolidation 简化为仅 git commit。降级改由 Nightly Agent Stage 5 语义判断执行。详见 `docs/记忆系统设计方案.md` 附录 A。

**2026-03-26 — 优先级标签语义化**：P0-P3 数字优先级替换为语义标签（permanent/lasting/standard/ephemeral）。HEARTBEAT Stage 5 prompt 已同步更新。

**2026-03-26 — 可靠性优化**：`PostConsolidationAsync` 新增 `LastConsolidationAt` 时间戳记录；`koda-memory/SKILL.md` v3.1 明确日常写入仅用 `workspace_memory_append`，`workspace_protocol_update` 限 Nightly 整合；topics 限制放开。详见 `docs/记忆系统设计方案.md` 附录 B。
