# Iteration 17 FREEZE：前端追平三件套

**冻结日期：** 2026-03-21
**目标迭代：** Iter 17
**范围状态：** 已冻结，不得事后扩展

---

## 背景

Iter 12–16 打通了大量后端能力，但有三个功能对用户完全不可见或操作不便：
1. `canvas_upsert` 工具已通，但 Canvas Desk 无产物渲染 UI
2. Automations 引擎正常调度，但前端无运行历史面板
3. Channels 发送模式（Delivery Mode）只能通过 API 或 SQLite 修改

本迭代目标：**纯前端追平 + 一个小 API**，全部关闭以上缺口。

---

## 范围（KC 条目）

| ID | 描述 | 规模 |
|----|------|------|
| KC-1701 | Canvas Desk 产物列表 + 内容渲染面板 | M |
| KC-1702 | Automations 运行历史面板 | S |
| KC-1703 | Channels Delivery Mode per-binding toggle（前端 + PATCH API） | XS |

---

## KC-1701：Canvas Desk 产物列表 + 渲染面板

### 当前状态
- 后端：`GET /api/canvas`（列表）、`GET /api/canvas/{id}`（含 `contentText` 字段）完整实现
- 前端：`CanvasDesk` 组件存在占位符，无实际内容

### 目标 UX
```
CanvasDesk
├── 左侧：产物列表（title + kind badge + 日期）
│   └── 点击选中
└── 右侧：内容预览
    ├── kind=report/tasklist/dashboard/board → Markdown 渲染（使用 marked 或 react-markdown）
    └── kind=html → 沙盒 iframe（sandbox="allow-scripts"）
```

### 需要的 API 字段
`GET /api/canvas/{id}` 已返回 `contentText`，无需后端改动。

### 改动文件
- `apps/kodaclaw-web/src/components/CanvasDesk.tsx` — 主体实现
- `apps/kodaclaw-web/src/__tests__/canvas-desk.spec.tsx` — 单元测试（列表渲染、内容切换、空态）

### 非目标
- 不做编辑功能
- 不做 Canvas artifact 删除
- 不做实时刷新（手动 reload 即可）

---

## KC-1702：Automations 运行历史面板

### 当前状态
- 后端：`GET /api/automations/definitions`、`GET /api/automations/runs`、`PATCH /api/automations/definitions/{id}` 完整实现
- 前端：`AutomationsDesk` 有定义列表，但无运行历史展示

### 目标 UX
```
AutomationsDesk
├── 顶部：引擎状态 Banner（automationsEnabled=false 时显示警告，已有）
├── 定义列表（已有）
│   └── 每条：名称 + cron + enabled toggle（已有）+ 最近运行状态 chip（新增）
└── 底部展开区：选中定义后显示运行历史
    └── 每条运行记录：触发时间 + 耗时 + 状态（Success/Failed）+ 摘要
```

### 需要的 API 字段
`GET /api/automations/runs?definitionId={id}&limit=20` 已实现，无需后端改动。

### 改动文件
- `apps/kodaclaw-web/src/components/AutomationsDesk.tsx` — 增加运行历史展开区
- `apps/kodaclaw-web/src/__tests__/automations-desk.spec.tsx` — 补运行历史渲染测试

### 非目标
- 不做手动触发自动化（需要独立迭代设计）
- 不做运行日志详情（摘要够用）

---

## KC-1703：Channels Delivery Mode per-binding toggle

### 当前状态
- Delivery Mode 由 `CreateDefaultDeliveryRule` 基于 thread type 硬编码，无法从 UI 修改
- 绑定的 `delivery_rule_id` 存于 `thread_bindings` 表，目前只有两种内置 ID

### 设计方案

**后端（XS）：**

新增 `PATCH /api/channels/threads/{bindingId}/settings`：
```json
{ "deliveryMode": "AutoSend" | "DraftApproval" | "RequireApproval" }
```
实现：在 `thread_bindings` 表新增 `delivery_mode_override TEXT NULL` 列（migration）。
`ChannelEventIngestionService.CreateDefaultDeliveryRule` 读取 override 优先于 thread type 默认值。

**前端（XS）：**
- `ChannelsDesk` 线程详情区增加 Delivery Mode 下拉/单选（三选一）
- 选中后 PATCH，即时更新本地状态

### 改动文件

后端：
- `src/KodaClaw.ChannelHub/SqliteChannelHubDatabase.cs` — migration 加列
- `src/KodaClaw.ChannelHub/SqliteThreadBindingRepository.cs` — 读写 override
- `src/KodaClaw.Contracts/ThreadBindingDetail.cs` 或 DTO — 加 `deliveryMode` 字段
- `src/KodaClaw.ChannelHub/ChannelEventIngestionService.cs` — 读取 override
- `src/KodaClaw.Gateway/Endpoints/GatewayApp.ChannelEndpoints.cs` — 新增 PATCH 端点

前端：
- `apps/kodaclaw-web/src/components/ChannelsDesk.tsx` — 线程详情加 Delivery Mode 控件
- `apps/kodaclaw-web/src/types/contracts.ts` — 加 PATCH request 类型

### 非目标
- 不做 Delivery Mode 的细粒度规则（quiet hours 等）
- 不做批量修改

---

## 验证命令

```bash
# L0
dotnet build KodaClaw.sln
cd apps/kodaclaw-web && npm run typecheck

# L1
dotnet test tests/KodaClaw.UnitTests --filter "CanvasDesk|AutomationsDesk|ChannelDelivery"
cd apps/kodaclaw-web && npm run test -- --reporter=verbose

# L2
dotnet test tests/KodaClaw.IntegrationTests --filter "ChannelThread|DeliveryMode"

# L3
dotnet test tests/KodaClaw.ContractTests

# 全量
make test-solution && cd apps/kodaclaw-web && npm run test
```

---

## 验收门槛

1. Canvas Desk：能列出 `canvas_upsert` 生成的产物，点击能看到内容（markdown 渲染）
2. Automations：选中一条定义能看到最近运行记录（至少显示时间和状态）
3. Channels：能从 UI 改 Delivery Mode，下次消息进来按新 mode 执行
4. 全量测试通过，无编译警告

---

## Wave 计划

| Wave | 内容 |
|------|------|
| Wave 1 | KC-1703 后端（migration + override 逻辑 + PATCH 端点）+ 集成测试 |
| Wave 2 | KC-1703 前端（Channels Desk toggle）+ KC-1702（Automations 历史面板）|
| Wave 3 | KC-1701（Canvas Desk 渲染面板）+ 前端全量测试 |
