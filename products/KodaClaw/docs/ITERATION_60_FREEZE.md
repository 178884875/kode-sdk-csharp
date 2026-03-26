# Iteration 60 FREEZE — Diagnostics 可观测、可视化、可清理

> 冻结日期：2026-03-26
> 迭代类型：新功能（完整 Capability Slice）

## 目标

把 Diagnostics 从"只有内存+bundle export"升级为三层系统：
- **持久化**：重启后仍可查询历史事件
- **可观测**：DiagnosticsDesk 实时查看、过滤、清理
- **可视化**：bundle export 附带 report.html（Chart.js 静态报告）

额外：Serilog → DiagnosticsService 桥，统一两套日志系统。

## 范围

### 后端

1. **`IDiagnosticsService` 接口扩展**
   - `ClearAsync(DateTimeOffset? before, CancellationToken)` — 按时间清理
   - `IAsyncEnumerable<DiagnosticEvent> SubscribeAsync(CancellationToken)` — SSE 实时推送
   - `DiagnosticsQuery` 新增 `DateFrom`/`DateTo` 范围过滤

2. **`FileDiagnosticsService`**（替换 `InMemoryDiagnosticsService`）
   - 写入：每次 `Record()` 同步追加到 `~/.kodaclaw/logs/diagnostics.jsonl`
   - 读取：内存热层（最近 500 条）+ 启动时从文件回填
   - 清理：`ClearAsync(before)` 重写文件，保留 before 之后的行；全清删除文件
   - 文件滚动：单文件，无按日期分片，由 `ClearAsync` 手动维护
   - 订阅：内部 `Channel<DiagnosticEvent>` 广播，`SubscribeAsync` 读取

3. **新 Gateway 端点**
   - `GET /api/diagnostics/stream` — SSE 实时流
   - `DELETE /api/diagnostics` — 清理（query: `?before=ISO8601`，无参数=全清）
   - `GET /api/diagnostics/stats` — 统计摘要（各 source 计数、level 分布、最近错误）

4. **Serilog → DiagnosticsService 桥**
   - 新增 `DiagnosticsSerilogSink`（Warning 及以上级别）
   - Gateway 启动时注册，不影响现有 Serilog 文件/控制台输出

5. **report.html 生成**（注入到 `DiagnosticBundleService.ExportAsync`）
   - 内嵌 Chart.js（CDN 引用，无本地文件依赖）
   - 三张图：事件时间线（按分钟堆叠面积）、source 分布（横柱）、level 分布（饼图）
   - 底部附完整事件列表（可按 level/source 过滤，纯 JS 无框架）
   - 数据内嵌为 `<script>const BUNDLE_DATA = {...}</script>`，离线可用

### 前端

6. **DiagnosticsDesk**（新 Desk 页面）
   - 事件列表：时间戳 / source / level badge / message，点击展开 Attributes
   - 过滤栏：level 下拉 + source 下拉 + sessionId 输入 + 关键词搜索
   - 实时：通过 SSE 接收新事件，从顶部插入，闪烁动画
   - 清理下拉：清理 7 天前 / 清理 1 天前 / 全部清理
   - 统计头部：info/warning/error 计数 pills + 实时连接状态指示

7. **Sidebar 接入**
   - 在 Sidebar"系统"区新增"诊断"入口（`Activity` 图标）

## 非目标（本迭代不做）

- CorrelationId 全链路追踪（需要调用方批量改造）
- Session 健康指示器（下一迭代）
- Koda 读取自身诊断数据的工具（长期）
- report.html 的 CorrelationId 甘特图（依赖上一项）
- SQLite 存储 Diagnostics（文件足够，不引入新依赖）

## 契约约定

```csharp
// IDiagnosticsService 新增
Task ClearAsync(DateTimeOffset? before = null, CancellationToken ct = default);
IAsyncEnumerable<DiagnosticEvent> SubscribeAsync(CancellationToken ct);

// DiagnosticsQuery 新增字段
DateTimeOffset? DateFrom
DateTimeOffset? DateTo

// 新 contracts
DiagnosticsStatsResponse(
    int TotalEvents,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<DiagnosticsSourceStats> BySource,
    DateTimeOffset? OldestEvent,
    DateTimeOffset? NewestEvent)

DiagnosticsSourceStats(string Source, int Count, int ErrorCount, int WarningCount)

DiagnosticsClearRequest(DateTimeOffset? Before)
```

## 验证命令

```bash
dotnet build products/KodaClaw/KodaClaw.sln
dotnet test products/KodaClaw/KodaClaw.sln -m:1 --filter "Diagnostics"
npm run typecheck --prefix products/KodaClaw/apps/kodaclaw-web
```
