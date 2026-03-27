# Iteration 63 FREEZE — 存储层去 SQLite：JSON/JSONL 实现 + 接口抽象

**日期**：2026-03-26
**类型**：优化/重构（大改，跨所有后端模块）
**范围**：`KodaClaw.Storage` / 新建 `KodaClaw.Storage.Json` / `KodaClaw.ControlPlane` / `KodaClaw.Automation` / `KodaClaw.ChannelHub` / `KodaClaw.PluginHost` / `KodaClaw.ModelHub` / `KodaClaw.Gateway`

---

## 背景与动机

KodaClaw 当前用 SQLite（`control-plane.db`）存储 12 类业务数据。从产品定位看，这引入了三个根本性矛盾：

**1. 与 local-first / workspace-as-protocol 原则矛盾**
所有 workspace 文件（IDENTITY.md、MEMORY.md、mcp.json…）都是可 git、可 diff、可直接查看的普通文件。但收件箱、审批单、渠道绑定等业务状态却藏在二进制 `.db` 文件里——用户无法直接检查、备份或迁移这些数据。

**2. SQLite 的保护在单用户本机场景下几乎无用**
代码分析显示：12 张表中只有 2 处用了显式事务（`SetDefault` 和一次表重建迁移），没有任何跨表 JOIN，查询全部是简单过滤 + LIMIT。SQLite 提供的并发保护（WAL journal）在本机单用户场景下几乎不会被触发。数据量也极小：inbox 通常几十条，thread_bindings 几十条，configs 几条。

**3. 迁移机制复杂、脆弱**
已有 8+ 次 ALTER TABLE 迁移，使用字符串匹配做幂等判断（`ex.Message.Contains("duplicate column name")`），表重建迁移需要手写 RENAME → INSERT → DROP → recreate-index，每次 schema 变更都是风险。JSON 文件直接加字段即可，无需迁移。

**4. 扩展性：接口层已就绪，实现层需补齐**
`KodaClaw.Contracts` 里 12 个 Repository 接口已全部存在，但只有一套 SQLite 实现。若后续要支持云端（PostgreSQL、Supabase 等），只需新增实现项目，不改任何接口和业务逻辑。

**设计决策：参考 `Kode.Agent.Store.Json` 模式**
SDK 已有成熟的 JSON 文件存储实现（`JsonAgentStore`），核心模式是：
- `WriteWithWalAsync`：先写 `.wal`，再 `File.Move` 原子落地（可变状态）
- `AppendEventAsync`：`FileShare.ReadWrite` + retry（只追加 JSONL 日志）
- 目录扫描 + 内存 LINQ：替代 SQL WHERE 过滤

---

## 文件目录布局（`~/.kodaclaw/`）

```
~/.kodaclaw/
  config/
    settings.json            ← app_settings（单对象 WAL）
    models/
      {id}.json              ← model_endpoints（每条一文件 WAL）
    plugins/
      {id}.json              ← plugin registry（每条一文件 WAL）
  store/
    inbox/
      {id}.json              ← inbox_items
    approvals/
      {id}.json
    canvas/
      {id}.json              ← canvas_artifacts
    automations/
      {id}.json              ← automation_definitions
    channels/
      accounts/
        {id}.json            ← channel_accounts
      bindings/
        {id}.json            ← thread_bindings
  logs/
    runs/
      {automationId}.jsonl   ← automation_runs（仅追加）
    audits/
      {bindingId}.jsonl      ← channel_audits（仅追加）
    plugins/
      {pluginId}.jsonl       ← plugin_logs（仅追加，5000 行触发截断保留 2000）
```

> `config/models/` 和 `config/plugins/` 与 workspace 协议 `config/` 目录对齐，消除现有 SQLite 与 workspace 文件的双写冗余。

---

## 范围（IN SCOPE）

### KC-6301：新建 `KodaClaw.Storage.Json` + 改造 `KodaClaw.Storage` 为基础设施层

**`KodaClaw.Storage` 改造**：去掉 `Microsoft.Data.Sqlite` 依赖，删除 `SqliteStorageDatabase.cs` / `SqliteCanvasArtifactRepository.cs`，保留 `CanvasArtifactValidation.cs`，新增 `JsonStoreBase.cs`：

```csharp
// KodaClaw.Storage/JsonStoreBase.cs
public abstract class JsonStoreBase
{
    protected readonly JsonSerializerOptions JsonOptions;

    // 可变对象：写 .wal → File.Move（原子落地）
    protected Task WriteEntityAsync<T>(string path, T value, CancellationToken ct);

    // 读取：先检查 .wal 恢复，再读主文件
    protected Task<T?> ReadEntityAsync<T>(string path, CancellationToken ct);

    // 目录扫描：读所有 {id}.json → 内存 LINQ 过滤
    protected Task<List<T>> ScanDirectoryAsync<T>(
        string dir, Func<T, bool>? predicate, CancellationToken ct);

    // 只追加 JSONL：FileShare.ReadWrite + 3 次 retry（50/100/150ms backoff）
    protected Task AppendLineAsync<T>(string path, T value, CancellationToken ct);

    // 读最近 N 行 JSONL（File.ReadLinesAsync 倒序取）
    protected Task<List<T>> ReadLastLinesAsync<T>(string path, int limit, CancellationToken ct);

    // 日志文件行数超阈值时截断（保留最新 keepLines 行）
    protected Task TrimLogFileAsync(string path, int maxLines, int keepLines, CancellationToken ct);
}
```

**新建 `KodaClaw.Storage.Json` 项目**：
- 仅依赖 `KodaClaw.Contracts`（接口和 DTO）+ `KodaClaw.Storage`（JsonStoreBase）
- 不依赖任何业务模块（ControlPlane / ChannelHub 等）
- 包含 12 个 `IXxxRepository` 实现 + `ServiceCollectionExtensions`
- `.csproj` 无 `Microsoft.Data.Sqlite` 引用

---

### KC-6302：A 类配置型 Repository（4 个）

**`JsonSettingsRepository`**（实现 `ISettingsRepository`）
- `GetAsync` → `ReadEntityAsync("config/settings.json")`
- `SaveAsync` → `WriteEntityAsync("config/settings.json")`
- 文件不存在时返回 `AppSettings.Default`

**`JsonModelRegistryRepository`**（实现 `IModelRegistryRepository`）
- `ListAsync` → `ScanDirectoryAsync("config/models/")`，按 `IsDefault DESC, CreatedAt ASC` 排序
- `GetByIdAsync` → `ReadEntityAsync("config/models/{id}.json")`
- `AddAsync` / `UpdateAsync` → `WriteEntityAsync`
- `DeleteAsync` → `File.Delete`
- `SetDefaultAsync` → 全量 scan → LINQ 修改 `IsDefault` → 全量 WriteEntity（遍历写每个文件）
- `ResolveDefaultForAsync` → scan → LINQ 位操作 `(m.Capabilities & required) == required`，`IsDefault DESC` 优先

**`JsonPluginRegistryRepository`**（实现 `IPluginRegistryRepository`）
- CRUD 同 model：每条一文件 WAL
- `ListAsync(PluginQuery)` → scan + LINQ（types 包含过滤、trust_state、enabled、runtime_state）

**`JsonAutomationDefinitionRepository`**（实现 `IAutomationDefinitionRepository`）
- CRUD：每条一文件 WAL
- `ListAsync` → scan + LINQ（enabled、source 过滤）

---

### KC-6303：C 类日志追加型 Repository（3 个）

**`JsonAutomationRunRepository`**（实现 `IAutomationRunRepository`）
- `AddAsync` → `AppendLineAsync("logs/runs/{automationId}.jsonl")`
- `UpdateAsync` → 读全量 JSONL → LINQ 替换对应 runId → 覆盖写（`WriteEntityAsync` 整文件，非 JSONL 模式）

  > 说明：run record 有 `UpdateAsync`（更新状态），不适合纯追加。采用：读全量 → 内存修改 → WAL 写回整文件。run 文件行数极少（单个 automationId 通常 < 100 行），全量读写无问题。

- `GetByIdAsync` / `ListAsync` → 按 automationId 分文件或全量扫 `logs/runs/`

**`JsonChannelAuditRepository`**（实现 `IChannelAuditRepository`）
- `AppendAsync` → `AppendLineAsync("logs/audits/{bindingId}.jsonl")`
- `ListByBindingIdAsync(bindingId, limit)` → `ReadLastLinesAsync("logs/audits/{bindingId}.jsonl", limit)`

**`JsonPluginLogRepository`**（实现 `IPluginLogRepository`）
- `AppendAsync` → `AppendLineAsync("logs/plugins/{pluginId}.jsonl")`；追加后调 `TrimLogFileAsync(maxLines: 5000, keepLines: 2000)`
- `ListAsync(pluginId, limit)` → `ReadLastLinesAsync("logs/plugins/{pluginId}.jsonl", limit)`
- 幂等去重（原 SQLite `ON CONFLICT DO NOTHING`）：AppendAsync 前读最后 50 行检查 `entry_id` 是否重复

---

### KC-6304：B 类业务状态型 Repository（4 个）

统一模式：每条实体单独存为 `{dir}/{id}.json`，互相隔离，并发写无冲突。

**`JsonInboxRepository`**（实现 `IInboxRepository`）
- `UpsertAsync` → `WriteEntityAsync("store/inbox/{id}.json")`
- `GetByIdAsync` → `ReadEntityAsync("store/inbox/{id}.json")`
- `ListAsync(InboxQuery)` → `ScanDirectoryAsync` + LINQ（status、kind、requiresAction、sessionId 过滤，`UpdatedAt DESC` 排序）
- `UpdateStatusAsync` → 读 → 修改 status/updatedAt/resolvedAt → `WriteEntityAsync`

**`JsonApprovalRepository`**（实现 `IApprovalRepository`）
- `UpsertAsync` / `GetByIdAsync` / `ListAsync` → 同 Inbox 模式
- `TransitionAsync` → 读 → 检查当前 status（非 Pending 则 false）→ 修改 status + decidedAt + decidedBy + decisionNote → WriteEntity

**`JsonCanvasArtifactRepository`**（实现 `ICanvasArtifactRepository`）
- CRUD：每条一文件 WAL
- `ListAsync(CanvasArtifactQuery)` → scan + LINQ（kind、source、sessionId 过滤）
- `DeleteAsync` → `File.Delete`

**`JsonChannelAccountRepository`**（实现 `IChannelAccountRepository`）
- CRUD：每条一文件 WAL
- `ListAsync(ChannelAccountQuery)` → scan + LINQ（connectorKind、state 过滤）+ `.Skip(offset).Take(limit)`

---

### KC-6305：`JsonThreadBindingRepository`（内存字典热路径）

`thread_bindings` 是 KodaClaw 最热的查询路径：每条外部渠道消息进来都要通过 `GetByExternalThreadAsync(connectorKind, accountId, externalThreadId)` 找到对应 session。

**设计**：启动时全量加载到 `ConcurrentDictionary`，写穿透到磁盘。

```csharp
public sealed class JsonThreadBindingRepository : JsonStoreBase, IThreadBindingRepository
{
    // (connectorKind, accountId, externalThreadId) → bindingId
    private readonly ConcurrentDictionary<(string, string, string), string> _index = new();
    private volatile bool _loaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    // 双检查懒加载：首次访问时扫目录填充字典
    private async Task EnsureLoadedAsync(CancellationToken ct);

    // 热路径：O(1) 字典查找 → ReadEntity 读文件
    public async Task<ThreadBinding?> GetByExternalThreadAsync(...);

    // 写入：WriteEntityAsync + 更新字典（原子：字典更新在文件写成功之后）
    public async Task UpsertAsync(ThreadBinding binding, CancellationToken ct);

    // 删除：File.Delete + 从字典移除
    public async Task<bool> DeleteAsync(string id, CancellationToken ct);
}
```

唯一约束保证：`_index` 的 key 天然唯一，`UpsertAsync` 时若 key 已存在则覆盖（覆盖即更新），与 SQLite `INSERT OR REPLACE` 语义一致。

---

### KC-6306：`KodaClaw.Storage` 模块清理

**删除**：
- `SqliteStorageDatabase.cs`
- `SqliteCanvasArtifactRepository.cs`
- `ServiceCollectionExtensions.cs`（SQLite 注册删除，接口注册移入 `KodaClaw.Storage.Json`）

**保留**：
- `CanvasArtifactValidation.cs`（纯业务逻辑，无 SQLite）
- `StorageMarker.cs`

**`.csproj` 变更**：移除 `<PackageReference Include="Microsoft.Data.Sqlite" />`，所有模块的 `.csproj` 同步清理。

需要移除 SQLite 引用的模块：
- `KodaClaw.Storage`
- `KodaClaw.ControlPlane`
- `KodaClaw.Automation`
- `KodaClaw.ChannelHub`
- `KodaClaw.PluginHost`
- `KodaClaw.ModelHub`

---

### KC-6307：Gateway DI 切换

**`KodaClaw.Gateway.csproj`**：新增 `<ProjectReference Include="..\KodaClaw.Storage.Json\..." />`

**DI 注册替换**：删除各模块的 SQLite 注册调用，统一换为：

```csharp
// GatewayApp.cs 或 Infrastructure/ServiceRegistration.cs
services.AddKodaClawJsonStore(
    storeRoot: Path.Combine(workspaceRoot, ".koda"));
```

**`AddKodaClawJsonStore` 注册所有 12 个接口**（`Singleton`，因 JsonStoreBase 是无状态的，`JsonThreadBindingRepository` 内存字典需要 Singleton 保证单例）。

---

### KC-6308：测试全量更新与回归

**删除**：所有 `Sqlite*Repository*Tests` / `Sqlite*Database*Tests`

**新增（L1 单元测试）**：
- `JsonStoreBaseTests`（WAL 写入 + 崩溃恢复 + JSONL append + 目录扫描，~10 个）
- `JsonSettingsRepositoryTests`（~4 个）
- `JsonModelRegistryRepositoryTests`（SetDefault 原子性、ResolveDefaultFor 能力过滤，~8 个）
- `JsonThreadBindingRepositoryTests`（内存字典一致性、并发安全，~6 个）
- `JsonApprovalRepositoryTests`（TransitionAsync 状态机，~5 个）
- `JsonPluginLogRepositoryTests`（日志截断、幂等去重，~4 个）

**更新（L2 集成测试）**：
- `IntegrationTests` 中凡是依赖 SQLite `WebApplicationFactory` 的，改用 `AddKodaClawJsonStore` 并指向临时目录（`Path.GetTempPath() + Guid`，测试后 `Directory.Delete`）

**新增（L3 契约测试）**：
- `JsonStoreContractTests`：验证 `{id}.json` WAL 文件落地格式、JSONL 行格式

**L0 目标**：`dotnet build KodaClaw.sln` 0 错 0 警告；`npm run typecheck` 通过（前端无改动）

---

## 非目标（OUT OF SCOPE）

- 不做数据迁移工具——KodaClaw 是本机单用户产品，旧 `.koda/control-plane.db` 保留，新 store 以空库启动
- 不改动任何接口定义（`KodaClaw.Contracts` 接口全部保持原样）
- 不改动前端（接口不变，Gateway 行为不变）
- 不改动 Workspace 协议文件（`IDENTITY.md`、`MEMORY.md` 等）
- 不实现 PostgreSQL 实现（`KodaClaw.Storage.Pgsql` 留给后续迭代）
- 不修改 Agent Store（`Kode.Agent.Store.Json`，已是文件方案，不受影响）
- 不改动 `ISecretStore` / `IMediaStore`（与 SQLite 无关）

---

## 关键契约

### `KodaClaw.Storage.Json` DI 扩展签名

```csharp
namespace KodaClaw.Storage.Json;

public static class ServiceCollectionExtensions
{
    /// <param name="storeRoot">存储根目录，通常为 {workspaceRoot}/.koda</param>
    public static IServiceCollection AddKodaClawJsonStore(
        this IServiceCollection services,
        string storeRoot);
}
```

### WAL 原子写语义

`WriteEntityAsync` 保证：若进程在写入中途崩溃，下次读取时自动从 `.wal` 恢复，不会出现空文件或截断文件。与 `JsonAgentStore.WriteWithWalAsync` 语义完全一致。

### JSONL 追加语义

每行一个完整 JSON 对象，`AppendLineAsync` 使用 `FileShare.ReadWrite` 允许并发读，3 次指数退避 retry（50/100/150ms），与 `JsonAgentStore.AppendEventAsync` 语义一致。

### 目录结构契约（相对于 storeRoot）

```
config/settings.json
config/models/{id}.json
config/plugins/{id}.json
store/inbox/{id}.json
store/approvals/{id}.json
store/canvas/{id}.json
store/automations/{id}.json
store/channels/accounts/{id}.json
store/channels/bindings/{id}.json
logs/runs/{automationId}.jsonl
logs/audits/{bindingId}.jsonl
logs/plugins/{pluginId}.jsonl
```

---

## 受影响模块

| 模块 | 变更类型 |
|------|---------|
| `KodaClaw.Storage` | 删除 SQLite 类，保留 CanvasArtifactValidation，新增 `JsonStoreBase` |
| `KodaClaw.Storage.Json` | **新建项目**，12 个 Repository 实现 + DI 注册 |
| `KodaClaw.ControlPlane` | 删除 `SqliteInboxRepository` / `SqliteApprovalRepository` / `SqliteSettingsRepository` / 3 个 Database 类 |
| `KodaClaw.Automation` | 删除 `SqliteAutomationDefinitionRepository` / `SqliteAutomationRunRepository` / `SqliteAutomationDatabase` |
| `KodaClaw.ChannelHub` | 删除 `SqliteChannelAccountRepository` / `SqliteThreadBindingRepository` / `SqliteChannelAuditRepository` / `SqliteChannelHubDatabase` |
| `KodaClaw.PluginHost` | 删除 `SqlitePluginRegistryRepository` / `SqlitePluginLogRepository` / 2 个 Database 类 |
| `KodaClaw.ModelHub` | 删除 `SqliteModelRegistryRepository` |
| `KodaClaw.Gateway` | DI 切换：`AddKodaClawJsonStore` 替换各模块 SQLite 注册 |
| `KodaClaw.*.Tests` | 删除 SQLite 测试，新增 JSON 测试，IntegrationTests 改用临时目录 |

**零改动模块（稳定锚点）**：
`KodaClaw.Contracts`（所有接口不变）、`KodaClaw.Runtime`、`KodaClaw.McpHub`、`KodaClaw.ChannelHub`（业务逻辑层，只依赖接口）、`KodaClaw.Automation`（调度逻辑层）、`KodaClaw.ControlPlane`（业务逻辑层）、`KodaClaw.Workspace`、`KodaClaw.ModelHub`（服务层）、`kodaclaw-web`（前端完全不受影响）

---

## 验证矩阵

| 层级 | 验证内容 | 测试文件 |
|------|---------|---------|
| L0 | `dotnet build KodaClaw.sln` 0 错 0 警告 | — |
| L1 | `JsonStoreBase`：WAL 崩溃恢复、JSONL append retry、目录扫描过滤 | `JsonStoreBaseTests`（~10 个）|
| L1 | `JsonModelRegistryRepository`：`SetDefault` 原子性、`ResolveDefaultFor` 能力位过滤 | `JsonModelRegistryRepositoryTests`（~8 个）|
| L1 | `JsonThreadBindingRepository`：懒加载、O(1) 热路径、写穿透一致性 | `JsonThreadBindingRepositoryTests`（~6 个）|
| L1 | `JsonApprovalRepository`：`TransitionAsync` 状态机（Pending→Approved/Rejected/Canceled，非 Pending 返回 false）| `JsonApprovalRepositoryTests`（~5 个）|
| L1 | `JsonPluginLogRepository`：5000 行触发截断保留 2000、entry_id 幂等去重 | `JsonPluginLogRepositoryTests`（~4 个）|
| L2 | `WebApplicationFactory` 启动完整 Gateway（临时目录），主要 API 端点行为不变 | 更新 `KodaClaw.IntegrationTests`（核心 CRUD 路径） |
| L3 | 文件落地格式：`{id}.json` WAL 格式、JSONL 单行完整性 | `JsonStoreContractTests`（~6 个）|
| L5 | Dogfood：真实 Gateway 启动，主会话、渠道消息、自动化任务全流程正常；`.koda/store/` 目录下文件可直接 cat 查看 | 真实 Gateway + Web |

---

## 依赖与风险备注

**新建项目**：`KodaClaw.Storage.Json`，需要加入 `KodaClaw.sln`

**数据不迁移**：旧 `control-plane.db` 保留，新 store 以空库启动。对现有用户意味着历史 Inbox、审批、渠道绑定丢失——**KodaClaw 当前是开发阶段，这是可接受的代价**。正式发布前如需迁移，可作为独立工具实现。

**`thread_bindings` 内存字典**：进程重启时重新扫目录加载，不持久化字典本身。启动时 bindings 量极小（< 100 条），扫目录 < 10ms，无性能风险。

**`JsonAutomationRunRepository.UpdateAsync`**：run 文件按 `automationId` 组织，UpdateAsync 需全量读该文件 → 内存修改 → WAL 写回。单个 automationId 历史 run 数量通常 < 50 条，全量读写 < 1ms，可接受。

**去除 SQLite 影响的模块测试**：`KodaClaw.IntegrationTests` 大量测试通过 `WebApplicationFactory` 启动整个 Gateway，改为 JSON store 后需要用临时目录代替 SQLite 内存数据库。集成测试的隔离性由 `Path.GetTempPath()/KodaTest-{Guid}` 目录保证，测试后清理。
