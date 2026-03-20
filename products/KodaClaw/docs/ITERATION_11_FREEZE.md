# Iteration 11 Freeze — HEARTBEAT.md → SQLite 热更链路

冻结日期：2026-03-20
前置迭代：Iteration 10（Workspace Protocol Write）

---

## 1. 一句话目标

打通 `HeartbeatAutomationCompiler` 到 `AutomationScheduler` 之间缺失的同步链路：用户手动编辑或 Agent 写入 `HEARTBEAT.md` 后，变更须自动反映到 SQLite，使 `AutomationScheduler.TickAsync()` 能够正确调度新规则。

---

## 2. 范围冻结

### 2.1 纳入（In Scope）

**KC-1101 — HeartbeatSyncService：编译 + 差量同步逻辑**

新增 `IHeartbeatSyncService` 接口与 `HeartbeatSyncResult` record，实现以下同步算法：

1. 读取 `{RootPath}/workspace/HEARTBEAT.md`
2. 文件不存在 → 返回空结果，不清除已有定义（保守策略）
3. `compiler.Compile(markdown)` → 捕获 `HeartbeatCompilationException`：log warning + 保留现有定义不动，返回 `CompilationFailed=true`
4. `definitionRepository.ListAsync(Source: Heartbeat)` 查已有定义
5. **Upsert**：对每个编译出的 definition，若已存在则 merge 保留调度状态（`CreatedAt / LastRunAt / NextRunAt / LastRunStatus / LastError`），然后 `UpsertAsync`
6. **Delete**：对 SQLite 里有、编译结果里没有的 id，调用 `DeleteAsync`

关键不变量：保留 `NextRunAt` 与 `LastRunAt`，避免修改 HEARTBEAT.md 后意外重置已计划任务的调度时钟。

**KC-1102 — HeartbeatFileWatcherHostedService：启动同步 + 文件变更监听**

新增 `HeartbeatFileWatcherHostedService`（`BackgroundService`）：

1. 启动时立即执行一次 `SyncAsync()`
2. `FileSystemWatcher` 监听 `{RootPath}/workspace/HEARTBEAT.md`，过滤器 `NotifyFilter = LastWrite | FileName`
3. `Changed / Created` 事件写入 `BoundedChannel(1, DropOldest)`
4. consumer loop：读信号 → `Task.Delay(500ms)` debounce → drain channel → `SyncAsync()`
5. workspace 目录不存在时安全退出，不抛异常

### 2.2 排除（Out of Scope）

- **HEARTBEAT.md 的 UI 编辑界面**：不在本迭代，用户直接编辑文件
- **Agent 写入 HEARTBEAT.md 的工具**：`workspace_protocol_update` 已覆盖（不含 HEARTBEAT），专用 tool 留后续
- **删除 automations 时级联清理 run history**：`automation_runs` 保留历史记录，DeleteAsync 只删 definition
- **watcher 跨平台测试**：`FileSystemWatcher` 行为依赖 OS，watcher 逻辑以 L1 单元测试为主，不做 L4 跨平台 E2E
- **Gateway endpoint 暴露 sync 触发**：本迭代不提供手动触发 API，由 watcher + 启动时自动触发

---

## 3. 架构冻结

### 3.1 模块放置

两个新类放在 `KodaClaw.Workspace`：
- 该模块已持有 `IHeartbeatAutomationCompiler` 和 `IWorkspaceService`
- 已引用 `KodaClaw.Contracts`（`AutomationDefinition`、`IAutomationDefinitionRepository` 均在其中）
- `IAutomationDefinitionRepository` 由 `AddKodaClawAutomation()` 注册，DI 延迟解析，无顺序冲突

```
KodaClaw.Workspace/
  HeartbeatSyncService.cs               ← 新文件（KC-1101）
  HeartbeatFileWatcherHostedService.cs  ← 新文件（KC-1102）
  ServiceCollectionExtensions.cs        ← 修改：注册两个新服务
  KodaClaw.Workspace.csproj             ← 修改：添加 Hosting/Logging.Abstractions
```

### 3.2 服务注册

```csharp
services.TryAddSingleton<IHeartbeatSyncService>(provider => new HeartbeatSyncService(
    provider.GetRequiredService<IWorkspaceService>(),
    provider.GetRequiredService<IHeartbeatAutomationCompiler>(),
    provider.GetRequiredService<IAutomationDefinitionRepository>(),
    provider.GetService<ILogger<HeartbeatSyncService>>()));

services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IHostedService, HeartbeatFileWatcherHostedService>());
```

`GatewayApp.Composition.cs` 无需修改，已调用 `AddKodaClawWorkspace()`。

### 3.3 调度状态保留算法

```csharp
var merged = existingById.TryGetValue(definition.Id, out var existingDef)
    ? definition with
      {
          CreatedAt     = existingDef.CreatedAt,
          UpdatedAt     = DateTimeOffset.UtcNow,
          LastRunAt     = existingDef.LastRunAt,
          NextRunAt     = existingDef.NextRunAt,
          LastRunStatus = existingDef.LastRunStatus,
          LastError     = existingDef.LastError,
      }
    : definition with { CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
```

---

## 4. 非目标确认

- `HeartbeatSyncService` 不清除 `AutomationDefinitionSource.Manual` 来源的定义
- 编译失败时完全保守：不 upsert 任何内容，不 delete 任何已有定义
- watcher 只监听 workspace 目录下的 `HEARTBEAT.md`，不递归监听子目录
- `SyncAsync` 不加分布式锁（本地单进程 Gateway，无并发竞争）

---

## 5. 验收命令（Verification）

```bash
# L0
dotnet build KodaClaw.sln

# L1
dotnet test tests/KodaClaw.UnitTests --filter "HeartbeatSync"

# L2
dotnet test tests/KodaClaw.IntegrationTests --filter "HeartbeatSync"

# 全量
dotnet test KodaClaw.sln -m:1
```

---

## 6. 分波计划

| Wave | 内容 | KC |
|------|------|----|
| Wave 0 | 本文档 + BACKLOG 条目 | — |
| Wave 1 | KC-1101：HeartbeatSyncService 实现 + L1/L2 测试 | KC-1101 |
| Wave 2 | KC-1102：HeartbeatFileWatcherHostedService 实现 + 服务注册 | KC-1102 |
