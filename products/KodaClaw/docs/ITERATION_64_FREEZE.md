# Iteration 64 FREEZE — 一次性定时提醒（One-shot Timer）

> 冻结日期：2026-03-28
> 状态：FROZEN

---

## 目标

让 Agent 在对话中能够创建"执行一次就自动清除"的定时提醒，解决现有 HEARTBEAT.md cron 机制无法支持一次性任务的缺陷。

**用户路径**：用户说"3小时后提醒我开会" → Agent 调用 `schedule_reminder` tool → 指定时间 scheduler tick 触发 → Agent 执行 prompt → 推送到指定渠道 → 状态自动标记为 Fired，不再重复执行。

---

## 范围（IN SCOPE）

- `OneShotTimerRecord` + `OneShotTimerStatus` + `IOneShotTimerRepository` 数据契约
- `JsonOneShotTimerRepository`：存储于 `{workspaceRoot}/.one-shot-timers.json`（WAL 原子写）
- `IOneShotTimerService` + `OneShotTimerService`：Tick 检查到期任务，复用 `IAutomationSessionService` 执行 Agent
- `AutomationScheduler.TickCoreAsync` 末尾注入 `oneShotTimerService.TickAsync()`（不改 `TickAsync` 签名）
- `schedule_reminder` Agent Tool：参数 title?/prompt/fire_at（ISO 8601）/channels?
- `AutomationDefinitionSource.OneShot` 新枚举值
- L1 单元测试（OneShotTimerService TickAsync 逻辑）+ L3 契约序列化测试

---

## 非目标（OUT OF SCOPE）

- 不修改 `HeartbeatAutomationCompiler`，一次性任务不走 HEARTBEAT.md
- 不在 `AutomationDefinition` 上加 `IsOneShot` 字段
- 不引入独立 BackgroundService（方案 B，后续迭代）
- 不实现 `GET /api/timers` 管理端点（后续迭代）
- 不实现 `SourceSessionId` 上下文传递（后续迭代）
- 不实现事件驱动触发（P1，后续迭代）

---

## 关键设计决策

1. **Hook 点**：`TickCoreAsync`（非 `TickAsync`），确保 `RunOnceAsync` 也能触发 one-shot timers
2. **Session 复用**：`OneShotTimerService` 将 `OneShotTimerRecord` 包装成合成 `AutomationDefinition`（id=`oneshot-{timerId}`，source=`OneShot`）传入 `IAutomationSessionService`，无需修改 session 接口
3. **存储位置**：`{workspaceRoot}/.one-shot-timers.json`（与 `.dedupe-state.json` 同级，使用 `KodaClawWorkspaceOptions.ResolveRootPath()`）
4. **精度**：依赖 scheduler tick 间隔（默认 1 分钟），延迟最多 1 分钟，满足提醒场景
5. **Tool 接口**：`fire_at` 接受 ISO 8601 字符串，Agent 负责计算相对时间（"3小时后" → 具体 UTC 时间）

---

## 验证命令

```bash
dotnet build KodaClaw.sln                          # L0: 0 错 0 警告
dotnet test --filter "OneShotTimer"                # L1/L3: 新增测试
dotnet test KodaClaw.sln -m:1                      # 全量回归
```
