# Iteration 55 FREEZE — HEARTBEAT.md Cron 调度重构

**日期**：2026-03-25
**类型**：优化/重构（大改，跨 Workspace / Automation / Contracts / Web）
**范围**：`KodaClaw.Workspace` / `KodaClaw.Automation` / `KodaClaw.Contracts` / `KodaClaw.Storage` / `kodaclaw-web`

---

## 背景与动机

HEARTBEAT.md 当前使用自定义 DSL 调度表达式（`daily 09:00` / `weekdays 09:00` / `every 15m` 等），
存在三个根本性问题：

**1. 表达力严重受限**
只支持 5 种固定模式，无法表达"每月 1 日"、"每天 9 点和 18 点"、"工作日每半小时"等真实需求。
用户每次提出超出 5 种模式的需求，Agent 都无法完成。

**2. SKILL.md 文档与编译器格式不一致（直接 bug）**
`koda-automation/SKILL.md` 当前教 Agent 写的是 YAML front-matter 格式（`cron: "0 9 * * 1-5"`），
而 `HeartbeatAutomationCompiler` 实际解析的是 Markdown bullet 格式（`- schedule: daily 09:00`）。
Agent 按文档写的内容会导致编译器抛异常，automation 静默失效。

**3. Timezone 处理错误（UTC bug）**
`ComputeNextDailyRun` 注释明确写："LocalTime='09:00' is interpreted as 09:00 UTC regardless of server timezone"。
对于北京时间用户，`daily 09:00` 实际在 UTC+8 17:00 触发。KodaClaw 是本地个人 Agent OS，
应当使用系统本地时区，而非 UTC。

### 设计决策：保留 Markdown section 结构，只换调度字段

经评估，曾考虑将整个文件换成 YAML 多文档格式（`---` blocks），但该方案会导致：
- `WorkspaceProtocolUpdateTool.ApplySectionPatch` 完全失效（Agent 主写入路径断掉）
- `section` 参数语义必须从 heading title 改为 document id（工具接口破坏性变更）
- prompt 内容含 `---` 时 YAML 分割损坏

**最终决策（方向 B）**：
- 保留 `## Title` + bullet-list 的文件结构，`ApplySectionPatch` 零改动
- 新增 `- cron: "..."` 字段，兼容旧 `- schedule: ...`（deprecated but supported）
- 内部统一用 cron string 表示，引入 Cronos 库做 NextRunAt 计算，修 timezone bug

---

## 范围（IN SCOPE）

### KC-5501：编译器 — 新增 `cron:` 字段，保留 `schedule:` 兼容

**改动文件**：`HeartbeatAutomationCompiler.cs`

在 `ParseSections` 的 bullet 解析中，新增对 `cron:` 字段的识别：

```csharp
if (TryReadFieldValue(bulletContent, "cron", out var cronExpr))
{
    // 用 Cronos 验证格式合法性，非法时抛 HeartbeatCompilationException
    if (!IsValidCron(cronExpr))
        throw new HeartbeatCompilationException($"Invalid cron expression '{cronExpr}'.", lineNumber);
    currentSection.CronExpression = EnsureSingleAssignment(...);
    lineIndex++;
    continue;
}

if (TryReadFieldValue(bulletContent, "schedule", out var schedule))
{
    // 旧格式：转换为等价 cron string，保持向后兼容
    currentSection.CronExpression = EnsureSingleAssignment(
        currentSection.CronExpression, "schedule",
        LegacyScheduleToCron(schedule, lineNumber),
        lineNumber);
    lineIndex++;
    continue;
}
```

`SectionDraft` 中 `ScheduleExpression` 字段改为 `CronExpression: string?`。

**`LegacyScheduleToCron` 转换规则**（机械转换，无信息损失）：

| 旧表达式 | 等价 cron |
|---------|----------|
| `every Nm` | `*/N * * * *` |
| `hourly Nh` | `0 */N * * *` |
| `daily HH:mm` | `mm HH * * *` |
| `weekdays HH:mm` | `mm HH * * 1-5` |
| `weekly mon,fri HH:mm` | `mm HH * * 1,5` |

转换失败（无法识别的旧格式）：抛 `HeartbeatCompilationException`，与现有行为一致。

**`BuildDefinitions` 更新**：
- `SectionDraft.CronExpression` → `AutomationDefinition.CronExpression`
- 删除 `ParseSchedule()` 内的 5 个 Regex 定义（被 `LegacyScheduleToCron` 替代）
- `AutomationSchedule schedule` 构造参数删除，不再构建

---

### KC-5502：内部契约 + Cronos 调度引擎

**新增 NuGet 依赖**：`Cronos`（HangfireIO，2.1 MB，零依赖，MIT）

**`KodaClaw.Contracts` 变更**：

```csharp
// 删除以下三个文件（概念消失）：
// AutomationSchedule.cs
// AutomationScheduleKind.cs
// AutomationScheduleDay.cs

// AutomationDefinition.cs — Schedule 字段替换
public sealed record AutomationDefinition(
    string Id,
    string Title,
    string Prompt,
    AutomationDefinitionSource Source,
    string? SourcePath,
    string CronExpression,          // ← 替换原 AutomationSchedule Schedule
    bool Enabled,
    IReadOnlyList<string>? InputPaths,
    string? ModelId,
    IReadOnlyList<string>? NotificationChannels,
    AutomationNotifyMode NotifyMode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    AutomationRunStatus? LastRunStatus,
    string? LastError
);
```

**`AutomationScheduler.cs` — NextRunAt 计算**：

```csharp
// 删除 ComputeNextRunAt / ComputeNextDailyRun / ComputeNextWeeklyRun / ParseLocalTime / ToScheduleDay

private static DateTimeOffset ComputeNextRunAt(string cronExpression, DateTimeOffset after)
{
    var cron = Cronos.CronExpression.Parse(cronExpression);
    // 使用系统本地时区（修 UTC bug）
    var next = cron.GetNextOccurrence(after.UtcDateTime, TimeZoneInfo.Local);
    return next.HasValue
        ? new DateTimeOffset(next.Value, TimeZoneInfo.Local.GetUtcOffset(next.Value))
        : after.AddHours(1); // 防御性 fallback（理论上不触发）
}
```

**`AutomationValidation.cs` — schedule 验证简化**：

```csharp
// 删除 ValidateSchedule / ValidateLocalTime / LocalTimeFormat

// ValidateDefinition 中改为：
if (string.IsNullOrWhiteSpace(definition.CronExpression))
    throw new ArgumentException("CronExpression is required.", nameof(definition.CronExpression));
// Cronos 格式合法性已在编译器侧验证，此处仅做非空断言
```

---

### KC-5503：SQLite 迁移 — 4 列 → 1 列

**迁移脚本**（幂等，事务包裹）：

```sql
-- 添加新列
ALTER TABLE automation_definitions ADD COLUMN cron TEXT;

-- 从旧 4 列机械转换（与 LegacyScheduleToCron 逻辑一致）
UPDATE automation_definitions
SET cron = CASE schedule_kind
    WHEN 'Minutes' THEN '*/' || schedule_interval || ' * * * *'
    WHEN 'Hourly'  THEN '0 */' || schedule_interval || ' * * *'
    WHEN 'Daily'   THEN
        substr(schedule_local_time, 4, 2) || ' ' ||
        substr(schedule_local_time, 1, 2) || ' * * *'
    WHEN 'Weekly'  THEN
        -- DaysOfWeek JSON array → cron day-of-week 数字列表
        -- 由 C# 迁移代码处理（SQL 无法方便解析 JSON 数组）
        NULL  -- 占位，由迁移 helper 填充
    ELSE '0 * * * *'
END
WHERE cron IS NULL;

-- Weekly 行由 C# SqliteAutomationMigrationHelper 补填后，验证 cron NOT NULL
-- 最后删除旧列（SQLite 需 recreate table 方式，迁移代码处理）
```

> Weekly 的 DaysOfWeek 存为 JSON 数组（`["Monday","Friday"]`），需 C# 代码解析后转为
> 数字（`1,5`），SQL 层无法直接处理，由迁移 helper 辅助完成。

**`SqliteAutomationDefinitionRepository.cs` 变更**：
- 读取：`schedule_kind / schedule_interval / schedule_local_time / schedule_days_of_week` 映射逻辑
  → 直接读 `cron` 列赋给 `definition.CronExpression`
- 写入：4 列写入 → 写 `cron` 列
- 查询：无调度相关过滤，无改动

**ID 稳定性保证**：
`HeartbeatSyncService` 用 `id` 作为 key 保留运行历史（`LastRunAt / NextRunAt / LastRunStatus`）。
迁移后 SQLite 中的 `id` 列不变，只要 HEARTBEAT.md 文件的 `## Title` 不变，
编译出的 slug ID 不变，运行历史完整保留。

---

### KC-5504：前端 + Skill 文档

**新增 npm 依赖**：`cronstrue`（40 KB gzip，MIT，支持 `zh_CN`）

**`AutomationsDesk.tsx` — `formatSchedule` 函数替换**：

```typescript
// 删除原 formatSchedule 中的 Minutes/Hourly/Daily/Weekly 分支

import cronstrue from 'cronstrue/i18n';

function formatSchedule(cronExpression: string, locale: string): string {
  try {
    return cronstrue.toString(cronExpression, {
      locale: locale === 'zh' ? 'zh_CN' : 'en',
      use24HourTimeFormat: true,
    });
  } catch {
    return cronExpression; // fallback：直接显示 cron 表达式
  }
}
```

**`contracts.ts` 变更**：

```typescript
export interface AutomationDefinition {
  id: string;
  title: string;
  prompt: string;
  source: string;
  cronExpression: string;     // ← 替换 schedule: AutomationSchedule
  enabled: boolean;
  // ... 其余字段不变
}

// 删除 AutomationSchedule / AutomationScheduleKind / AutomationScheduleDay 接口
```

**`koda-automation/SKILL.md` 完整改写**：

```yaml
---
name: koda-automation
description: HEARTBEAT.md 自动化规则编写指南——cron 调度语法、渠道推送、delivery-mode 配置
license: built-in
compatibility: KodaClaw 1.x
allowed-tools: workspace_protocol_update workspace_read
metadata:
  kind: builtin-core
  version: "2.0"
  tags: "automation, heartbeat, scheduling, cron"
---
```

正文教 Agent 写的格式与编译器完全一致（消除当前文档-编译器冲突）：

```markdown
## 每日晨报
- cron: "0 9 * * 1-5"
- prompt: 基于 MEMORY.md 生成今日工作重点
- channels:
  - telegram-personal
- delivery-mode: auto
- enabled: true
```

内含 cron 速查表、常用模板、旧 `schedule:` 字段说明（标注 deprecated）。

**`DefaultWorkspaceTemplates.Heartbeat()` 更新**：
3 个示例规则全部改为 `- cron:` 格式，cron 表达式语义与旧 schedule 等价。

---

## 非目标（OUT OF SCOPE）

- 不改动 `WorkspaceProtocolUpdateTool.ApplySectionPatch`（核心决策：保留 Markdown section 结构）
- 不支持 6 位 cron（含秒，如 `0 0 9 * * 1-5`）——只支持标准 5 位
- 不支持 cron 的 `L`（月末）、`W`（工作日最近）、`#`（第 N 个周 X）等扩展语法（Cronos 支持的范围内即可）
- 不提供旧格式自动迁移工具（旧格式直接兼容，无需迁移）
- 不在 AutomationsDesk 添加 cron 可视化编辑器（只做 cronstrue 文字展示）
- 不修改 `AutomationSchedulerOptions.PollInterval`（1 分钟，cron 最小粒度与之对齐）
- 不修改 `HeartbeatFileWatcherHostedService`（零改动）
- 不修改 `HeartbeatSyncService`（零改动）

---

## 关键契约

### HEARTBEAT.md 新格式（向后兼容旧格式）

```markdown
## <automation-title>
- cron: "<5-field-cron-expression>"   ← 新字段（推荐）
- schedule: <legacy-expr>              ← 旧字段（deprecated，继续支持）
- prompt: <text> | >
    <multi-line-text>
- enabled: true|false                  (可选，缺省 true)
- model: <model-id>                    (可选)
- inputs:
  - <workspace-relative-path>          (可选，嵌套 bullet)
- channels:
  - <bindingId>                        (可选，嵌套 bullet)
- delivery-mode: auto|approval|none    (可选，缺省 none)
```

约束：
- `cron:` 和 `schedule:` 互斥，同一 section 只能有其一
- cron 表达式必须是合法 5 位标准 cron（Cronos 验证）
- NextRunAt 使用系统本地时区（`TimeZoneInfo.Local`）计算

### `AutomationDefinition` 公共契约

```csharp
string CronExpression  // 5-field standard cron, always non-null after compile
```

### Legacy 转换保证

旧 `- schedule:` 表达式机械转换为 cron string，**语义等价，无信息损失**。
转换后使用本地时区（修复 UTC bug），行为与旧 UTC 语义不同——对用户是正向修复。

---

## 受影响模块

| 模块 | 变更类型 |
|------|---------|
| `KodaClaw.Workspace` | `HeartbeatAutomationCompiler`：新增 `cron:` 解析 + `LegacyScheduleToCron`；5 个 Regex 删除 |
| `KodaClaw.Contracts` | `AutomationDefinition`：`Schedule` → `CronExpression`；删除 3 个 schedule 枚举/record 文件 |
| `KodaClaw.Automation` | `AutomationScheduler`：`ComputeNextRunAt` 换 Cronos；`AutomationValidation`：schedule 校验简化 |
| `KodaClaw.Storage` | SQLite 迁移：4 列 → 1 列 `cron`；`SqliteAutomationDefinitionRepository` 映射更新 |
| `KodaClaw.Gateway/skills` | `koda-automation/SKILL.md` 完整改写（版本升至 2.0）|
| `kodaclaw-web` | `contracts.ts`：删 schedule 类型，加 `cronExpression`；`AutomationsDesk.tsx`：`formatSchedule` 换 cronstrue |
| `DefaultWorkspaceTemplates` | `Heartbeat()` 示例改为 `- cron:` 格式 |
| 测试 | 见下方验证矩阵 |

**零改动模块（稳定锚点）**：
`HeartbeatSyncService`、`HeartbeatFileWatcherHostedService`、`AutomationSchedulerHostedService`、
`AutomationSessionService`、`ChannelHub`、`ControlPlane`、所有 Gateway 端点、`WorkspaceProtocolUpdateTool`

---

## 验证矩阵

| 层级 | 验证内容 | 涉及测试文件 |
|------|---------|------------|
| L0 | `dotnet build` 0 错 0 警告；`npm run typecheck` 通过 | — |
| L1 | 编译器：`cron:` bullet 解析 + Cronos 验证；`schedule:` 所有旧表达式转换正确；两者不能同时出现；空文件/空 section 行为不变 | `HeartbeatAutomationCompilerContractTests`（全量重写，~20 个）|
| L1 | 调度器：`ComputeNextRunAt` 用 cron + 本地时区；Daily/Weekly/Minutes/Hourly 等价 cron 计算结果正确 | `AutomationSchedulerTests`（更新，~5 个）|
| L1 | Repository：`cron` 列读写；旧 4 列数据迁移后可读 | `SqliteAutomationDefinitionRepositoryTests`（更新，~7 个）|
| L2 | 端到端同步：`koda-automation` SKILL.md 格式（含 `cron:`）→ 编译 → SQLite → 调度正确触发 | `HeartbeatSyncIntegrationTests`（更新，~3 个）|
| L2 | 旧格式兼容：含 `- schedule: daily 09:00` 的 HEARTBEAT.md 正常编译、同步、调度 | 新增 `HeartbeatLegacyCompatibilityTests`（~3 个）|
| L2 | 调度器集成：cron 格式定义 IsDue/NextRunAt 计算；manual trigger 不受影响 | `AutomationSchedulerIntegrationTests`（更新，~10 个）|
| L3 | `AutomationDefinition` DTO：`cronExpression` 字段 JSON round-trip；删除 schedule 嵌套对象 | `AutomationContractsTests`（更新，~6 个）|
| L5 | Dogfood：通过对话让 Agent 写入含 `cron:` 的 automation → AutomationsDesk 展示 cronstrue 人类可读描述 → 调度按本地时区触发 | 真实 Gateway + Web |

---

## 依赖与风险备注

**新增依赖**：
- `Cronos`（NuGet，`KodaClaw.Workspace` + `KodaClaw.Automation`）
- `cronstrue`（npm，`kodaclaw-web`）

**ID 稳定性**：`HeartbeatSyncService` 以 `id`（slug）为 key 保留运行历史。
只要 `## Title` 不变，迁移后 ID 不变，run history 完整保留。

**Timezone 行为变更（预期 breaking）**：
旧 `- schedule: daily 09:00` 实际触发时间是 UTC 09:00（对 UTC+8 用户是 17:00）。
迁移后等价 cron `0 9 * * *` 用本地时区，对 UTC+8 用户触发时间变为 09:00 本地时间。
这是 **bug 修复，对用户是正向变化**，在 Dogfood 时需观察首次触发行为。
