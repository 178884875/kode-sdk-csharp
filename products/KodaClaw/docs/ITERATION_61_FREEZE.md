# Iteration 61 FREEZE — Diagnostics 可感知、可关联、可自诊

> 冻结日期：2026-03-26
> 迭代类型：新功能（完整 Capability Slice）
> 前置：Iter 60 完成（FileDiagnosticsService、DiagnosticsDesk、SSE 流）

## 目标

在 Iter 60 建立的诊断基础设施之上，完成三个闭环：

1. **Session 健康指示器** — 用户无需主动打开 DiagnosticsDesk 就能感知近期错误
2. **Koda 自查工具** — Agent 可以主动读取自身诊断数据，实现"自我感知"
3. **CorrelationId 全链路前端展示** — 后端基础设施已有，补全剩余空洞并在 UI 上串联追踪

## 背景：CorrelationId 现状

后端基础设施**已大量完成**，不需要从头建：

| 组件 | 状态 |
|------|------|
| `ICorrelationContextAccessor` / `AsyncLocalCorrelationContextAccessor` | ✅ 已有 |
| `DiagnosticEvent.CorrelationId` 字段 | ✅ 已有 |
| `DiagnosticsQuery.CorrelationId` 过滤 | ✅ 已有 |
| Gateway 请求中间件：读 header 或生成 UUID，写入 AsyncLocal | ✅ 已有 |
| `MainSessionService`：全程透传 correlationId 到诊断事件 | ✅ 已有 |
| `ChannelHub`：多处已接入 | ✅ 已有 |

**本迭代补全的空洞：**

- `AutomationScheduler`：每次自动化运行生成独立 correlationId
- `InboxCreateTool` / `GenerateImageTool` / `CanvasUpsertTool`：从 accessor 读取，不再硬编码 null
- `DiagnosticsLoggerProvider`：ILogger 桥接事件注入 correlationId
- 前端：展开行追踪、toolbar chip、stats 联动

## 范围

### 后端

#### KC-6101 后端：stats since 参数

`IDiagnosticsService.GetStats()` 新增可选参数 `since`：

```csharp
// 修改前
DiagnosticsStatsResponse GetStats();

// 修改后（向后兼容）
DiagnosticsStatsResponse GetStats(DateTimeOffset? since = null);
```

- `FileDiagnosticsService` / `InMemoryDiagnosticsService` 实现：`since != null` 时只统计 `Timestamp >= since` 的事件
- Gateway `GET /api/diagnostics/stats` 接受可选 query param `?since=ISO8601`
- 用途：Session 健康 badge 用 `since = UtcNow - 1h`

#### KC-6102 后端：DiagnosticsQueryTool

新建 `KodaClaw.Runtime/DiagnosticsQueryTool.cs`：

- Tool 名称：`diagnostics_query`
- 参数：

```json
{
  "level":         "string?, 过滤级别 info/warning/error",
  "source":        "string?, 过滤来源",
  "correlationId": "string?, 过滤关联 ID（精确匹配）",
  "sinceMinutes":  "int?, 往前多少分钟，默认 60，最大 1440",
  "limit":         "int?, 最多返回条数，默认 20，最大 50"
}
```

- 返回字段（**不含 attributes**，避免敏感数据进入上下文）：

```json
[{
  "ts":            "HH:mm:ss",
  "level":         "info|warning|error",
  "source":        "gateway|runtime|...",
  "eventType":     "xxx.yyy",
  "message":       "...",
  "correlationId": "abc123...|null",
  "sessionId":     "...|null"
}]
```

- DI 注册：`ServiceCollectionExtensions.cs`（Runtime）加 `sp.GetRequiredService<IDiagnosticsService>()`
- `koda-workspace/SKILL.md` `allowed-tools` 追加 `diagnostics_query`

#### KC-6103 后端：CorrelationId 空洞补全

1. **`DiagnosticsLoggerProvider`**
   - 构造函数新增 `ICorrelationContextAccessor? correlationContextAccessor`
   - 创建 `DiagnosticEvent` 时读取 `correlationContextAccessor?.CorrelationId`
   - Gateway Composition 改为：
     ```csharp
     var correlationContextAccessor = app.Services.GetRequiredService<ICorrelationContextAccessor>();
     loggerFactory.AddProvider(new DiagnosticsLoggerProvider(diagnosticsService, correlationContextAccessor));
     ```

2. **`AutomationScheduler`**
   - 构造函数注入 `ICorrelationContextAccessor? correlationContextAccessor`
   - 每次自动化运行开始时生成 `runCorrelationId = Guid.NewGuid().ToString("N")`，设置 `correlationContextAccessor.CorrelationId = runCorrelationId`
   - 运行结束（finally）清空 `correlationContextAccessor.CorrelationId = null`
   - `BuildInboxItemForRunAsync` 中 `CorrelationId: runCorrelationId`（替换 null）
   - `Automation.ServiceCollectionExtensions` DI factory 加 `provider.GetRequiredService<ICorrelationContextAccessor>()`

3. **`InboxCreateTool`**
   - 注入 `ICorrelationContextAccessor?`，`CorrelationId: _correlationContextAccessor?.CorrelationId`

4. **`GenerateImageTool`**
   - 同上

5. **`CanvasUpsertTool`**
   - 同上

6. Runtime `ServiceCollectionExtensions` 对应三个工具注册时传入 `sp.GetService<ICorrelationContextAccessor>()`

### 前端

#### KC-6104 前端：Session 健康指示器

新建 `hooks/useDiagnosticsHealth.ts`：
- 每 30s 调用 `fetchDiagnosticsStats({ since: new Date(Date.now() - 3600_000).toISOString() })`
- 返回 `{ errorCount, warningCount }`

**Sidebar**：
- "诊断"条目加 badge（同 Inbox badge 样式），显示 `errorCount`（> 0 才显示）
- `Sidebar.tsx`：`const { errorCount } = useDiagnosticsHealth()`，badge 红色

**状态栏**：
- 有错误时：`运行正常` → `N 个近期错误`（红色文字，点击跳转 diagnostics desk）
- 无错误但有警告时：追加 `· N 警告`（amber）
- `Sidebar.tsx` 底部状态栏区域改造

`api.ts`：`fetchDiagnosticsStats(params?: { since?: string })` 加 since query param

#### KC-6105 前端：DiagnosticsDesk CorrelationId 追踪

**EventRow 展开详情**：
- 显示 `correlationId`（若有），右侧加"追踪"按钮（`Workflow` 图标，12px）
- 点击"追踪"：调用父组件回调，设置 active correlationId

**Toolbar chip**：
- 当 active correlationId 非空时，在 toolbar 插入一个 chip：
  - 样式：amber 描边，显示前 12 位 `abc123def456…`
  - 右侧 X 按钮清除
- chip 激活时 `filtered` 计算同时按 correlationId 过滤

**Stats pills 联动**：
- 当有任何过滤条件激活时（包括 correlationId chip），stats pills 从 API 全量数据改为**前端从 `filtered` 数组实时计算**（total / errors / warnings）
- 无过滤时仍显示 API 返回的全局 stats

**DiagnosticsDesk.css** 补充 chip 样式：

```css
.kc-diag-chip { ... }          /* amber 描边 chip */
.kc-diag-chip__close { ... }   /* X 按钮 */
```

#### KC-6106 前端：report.html CorrelationId

`DiagnosticBundleService.BuildReportHtml`：
- `bundleData.events` 新增 `correlationId` 字段序列化
- 事件表格新增 `CorrelationId` 列（截取前 8 位显示，鼠标悬停 tooltip 全文，无数据显示 `—`）
- 过滤栏新增 `<input id="f-correlation" placeholder="Correlation ID...">` + `applyFilter()` 联动
- 列宽调整：`col-ts 70px | col-source 160px | col-correlation 90px | col-level 70px | col-msg flex`

## 非目标（本迭代不做）

- CorrelationId 跨进程传播（Telegram/Feishu/WeChat 入站请求尚未生成 correlationId）
- 甘特图 / 瀑布图视图（trace 时序可视化，需要更多时序元数据）
- 诊断告警规则（N 个连续错误触发 Inbox 通知）
- `channel_send` / `channel_list` 工具补 correlationId（ChannelHub 侧已覆盖，工具层不需要）

## 契约变更

```csharp
// IDiagnosticsService
DiagnosticsStatsResponse GetStats(DateTimeOffset? since = null);  // 新增 since 参数

// 无新增 contracts，仅实现层变更
```

```typescript
// api.ts
fetchDiagnosticsStats(params?: { since?: string }): Promise<DiagnosticsStatsResponse>
```

## 验证命令

```bash
# L0
dotnet build products/KodaClaw/KodaClaw.sln
npm run typecheck --prefix products/KodaClaw/apps/kodaclaw-web

# L1
dotnet test products/KodaClaw/KodaClaw.sln -m:1 --filter "DiagnosticsStats|DiagnosticsQuery|CorrelationId|SessionHealth"

# L2
dotnet test products/KodaClaw/KodaClaw.sln -m:1 --filter "CorrelationIdPropagation|DiagnosticsStatsEndpoint"

# 全量
dotnet test products/KodaClaw/KodaClaw.sln -m:1
```
