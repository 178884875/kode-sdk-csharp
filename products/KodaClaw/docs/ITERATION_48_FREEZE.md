# Iteration 48 FREEZE — Session 保留策略与存储管理

**日期**：2026-03-24
**类型**：新功能
**范围**：Contracts / Gateway / kodaclaw-web（Settings Desk）

---

## 背景与动机

KodaClaw 的三类 session 文件夹（`main-*`、`auto-*`、`channel-*`）会无限累积在
`~/.kodaclaw/sessions/` 下，无任何清理机制。

- `auto-*`：每次自动化执行产生一个，高频 cron 下数月可达数千个，磁盘持续增长
- `main-*`：用户主对话历史，不应自动删除，但用户需要能手动清理旧会话
- `channel-*`：渠道对话连续性载体，永不自动删除

---

## 范围（IN SCOPE）

### 轨道一：`auto-*` 自动保留策略

- 新建 `SessionRetentionService`（**Gateway 层**，类比 `WorkspaceRepairService`，依赖 `IWorkspaceService` + `WorkspaceAppConfig`）
- 保留规则：每个自动化任务（按文件夹名中的 taskId 分组）保留最近 20 次，且不超过 30 天
- 触发时机：Gateway 启动时执行一次；`BackgroundService` 每天凌晨 3 点执行一次
- 配置项：`AutoSessionRetentionDays`（默认 30）/ `AutoSessionRetentionMaxPerTask`（默认 20），写入 `WorkspaceAppConfig`
- 删除条件：folder 已存在 `meta.json`（说明执行已完成）；不删除没有 `meta.json` 的（可能正在执行中）

### 轨道二：存储用量 API

- `GET /api/system/storage-usage`：返回三类 session 的数量和磁盘占用字节数

### 轨道三：主会话手动删除 API

- `DELETE /api/sessions/main/{id}`：删除指定主会话文件夹
- 守护：禁止删除 `ActiveMainSessionId`（当前活跃 session）
- 守护：只允许删除 `main-*` 前缀的 session

### 轨道四：Settings Desk 存储管理面板

- 在 Settings Desk 新增"存储"分区（`StorageSection`）
- 展示三类 session 数量 + 磁盘用量
- `auto-*` 显示自动清理策略说明（"保留 30 天 / 每任务最近 20 次，每天凌晨 3 点自动清理"）
- `main-*` 列出可删除的历史会话列表：每条显示 `CreatedAt`（格式化）+ `MessageCount`；活跃 session 加"当前活跃"标签并置灰删除按钮；其余支持单条删除（需二次确认）
- `channel-*` 展示数量，标注"不自动清理，与渠道绑定"
- 数据来源：`GET /api/system/storage-usage` + `GET /api/sessions`（已有端点）

---

## 非目标（OUT OF SCOPE）

- 不做 `channel-*` 自动清理（渠道历史是产品承诺）
- 不做 session 文件夹的目录结构重组（独立风险，留后续迭代）
- 不做批量删除 / 全部清空按钮（防误操作）
- 不做 `auto-*` 手动单条删除（Inbox 已承接结果，不需要）
- 不新增 Toast 组件（用现有 inline 提示）
- 不做保留策略的 UI 编辑（默认值足够，高级用户可改 `WorkspaceAppConfig`）

---

## 关键契约

### `WorkspaceAppConfig` 新增字段

```csharp
public int AutoSessionRetentionDays { get; init; } = 30;
public int AutoSessionRetentionMaxPerTask { get; init; } = 20;
```

### `StorageUsageResponse` DTO

```json
{
  "sessions": {
    "main":    { "count": 12, "sizeBytes": 4194304 },
    "auto":    { "count": 847, "sizeBytes": 52428800 },
    "channel": { "count": 6,  "sizeBytes": 1048576 }
  },
  "totalSizeBytes": 57671680
}
```

### `DELETE /api/sessions/main/{id}` 响应

- `200 OK`：删除成功
- `400 Bad Request`：session 非 `main-*` 前缀，或为当前活跃 session
- `404 Not Found`：文件夹不存在

---

## 验证矩阵

| 层级 | 验证内容 | 工具 |
|------|---------|------|
| L0 | `dotnet build` 0 错 0 警告；`npm run typecheck` 通过 | dotnet / tsc |
| L1 | `SessionRetentionServiceTests`：保留 N 条逻辑、按任务分组、不删无 meta.json 的、0 条边界 | xUnit + Moq |
| L2 | `StorageUsageEndpointTests`：返回结构正确；`DeleteMainSessionTests`：活跃 session 返回 400、非 main 前缀返回 400、正常删除返回 200 | WebApplicationFactory |
| L3 | `StorageUsageContractTests`：DTO 序列化结构 golden 验证 | FluentAssertions snapshot |
| L5 | Dogfood：手动造 50 个 auto- 文件夹，重启 Gateway，确认多余的被清理；Settings Desk 存储面板数字准确 | 真实 Gateway + Web |
