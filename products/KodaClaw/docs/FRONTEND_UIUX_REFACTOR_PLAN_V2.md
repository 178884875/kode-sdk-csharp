# Frontend UI/UX Refactor Plan V2

Last updated: 2026-03-20
Status: Approved, Slice A completed, Slice B/C in progress, Slice E first calibration completed

## 1. 目标

前置状态更新（2026-03-20）：

- Web V2 的两个前置 P0 已完成：Canvas preview auth / asset loading 已修复，session `StreamingModel` 残留已修复。
- `shell-v2` 骨架也已落地：现已具备 `GlobalRail` / `ContextRail` / `MainStage` 三段式结构，并已成为同一个 `kodaclaw-web` 内的默认壳层；`?shell=v1` 仅作为兼容 override 保留，不需要再等待 Phase 1 bugfix。

基于当前 live dogfood 结论，下一轮前端重构不再做“dashboard polish”，而是把 `kodaclaw-web` 重新组织成更接近 PoorClaw / 参考图的 **conversation-first personal agent shell**。

目标不是照抄图片，而是：

- 参考它的三段式布局和聊天优先层级
- 结合 KodaClaw 已有 API / contracts / desks
- 做出更像“个人 Agent 操作系统”的产品壳，而不是控制台总览页

## 2. 设计方向

### 2.1 总体结构

新壳层采用三栏式：

1. **Global Rail（最左窄栏）**
   - Chat
   - Inbox
   - Sessions
   - Models
   - Automations
   - Channels
   - Plugins
   - Canvas
   - Settings / locale / desktop status

2. **Context Rail（左侧宽栏）**
   - 根据当前域切换为对应 list pane
   - Chat: 主线程 / 历史线程 / 渠道线程入口
   - Inbox: inbox items / approvals list
   - Sessions: session list
   - Channels: account + thread list
   - Plugins: plugin catalog / installed list
   - Canvas: artifact list
   - Automations: automation definitions / runs list

3. **Main Stage（主内容区）**
   - 当前选中对象的 detail / conversation / approval / canvas viewer
   - chat 模式下必须默认显示消息时间线和输入器
   - operator 信息不再抢占首屏，而是变成 secondary surfaces

### 2.2 视觉语言

从当前“暖色 dashboard”过渡到“macOS utility shell + local agent workstation”：

- 更紧凑、更应用化，而不是网页 landing page
- 更弱的 hero、更强的 pane hierarchy
- 消息区、列表区、详情区的分工清楚
- 把现在大量 summary cards 收敛为 sidebar badges / inline pills / compact cards
- 让输入器固定在聊天主舞台底部，永远在主工作流里

### 2.3 参考图对 KodaClaw 的正确映射

参考图中的：
- 左图标栏 -> 对应 KodaClaw 的产品域导航
- 第二栏 assistant/thread list -> 对应 KodaClaw 的 context rail
- 中间聊天主区 -> 对应 Chat / Approval / Canvas / Session detail 主舞台
- 顶部轻量工具栏 -> 对应 model badge、gateway health、locale、update watch、desktop attach state

但需要做一层 KodaClaw 语义适配：
- 我们不是多助手 marketplace，因此第二栏不能直接照搬“助手列表”
- 应改成“对象列表”：主线程、收件事项、会话、渠道线程、插件、画布、自动化

## 3. 与现有接口的映射

### 3.1 Chat mode

使用现有：
- `POST /api/chat/stream`
- `GET /api/sessions`
- `GET /api/sessions/{id}`
- `GET /api/diagnostics/recent`

UI 组织：
- 左 rail 选中 Chat
- context rail 显示主线程 + 最近 session
- main stage 显示 timeline + composer
- 右上角 compact status 显示当前 model / gateway health / pending approvals

### 3.2 Inbox mode

使用现有：
- `GET /api/inbox`
- `GET /api/approvals`
- `POST /api/approvals/{id}/approve`
- `POST /api/approvals/{id}/reject`

UI 组织：
- context rail 显示收件与审批列表
- main stage 显示详情与决策表单
- 不再把 filters 和 summary 拆成多块大卡片

### 3.3 Sessions / Diagnostics mode

使用现有：
- `GET /api/sessions`
- `GET /api/sessions/{id}`
- `GET /api/diagnostics/recent`
- `GET /api/diagnostics/timeline`
- `POST /api/diagnostics/bundle-export`

UI 组织：
- context rail 为 session list
- main stage 为 session detail + diagnostics timeline
- export 成为 detail toolbar action

### 3.4 Models / Settings mode

使用现有：
- `GET /api/models`
- `POST /api/models/{id}/default`
- `GET /api/settings`
- `PUT /api/settings`
- `GET /api/system/update-state`
- `POST /api/system/update-check`

UI 组织：
- context rail 为 model endpoints / settings sections
- main stage 为 form/detail editor

### 3.5 Channels / Plugins / Automations / Canvas modes

维持现有 API，不先改 contract：
- Channels: `/api/channels/*`
- Plugins: `/api/plugins*`
- Automations: `/api/automations*`
- Canvas: `/api/canvas*`

第一轮重点不是扩接口，而是：
- 重组现有列表 / 详情 / 状态区域
- 补首轮 CTA
- 补“空状态 -> 下一步行动”路径

## 4. 关键体验改动

### 4.1 首屏即线程

打开应用默认直接进入 Chat 主舞台：
- 历史线程或当前 main session 可见
- 时间线可见
- 输入器可见
- 不需要先滚过 hero 和 overview cards

### 4.2 Summary cards 降级

当前的：
- 当前工作台
- gateway 目标
- 健康状态
- workspace
- 引导已归档

都改成 compact strips / status chips / metadata rows，不再占据首屏主要面积。

### 4.3 Desk from pages -> panes

现在每个 desk 像一整页“专题页”；重构后每个 desk 更像一个 pane mode：
- 有列表
- 有当前选中对象
- 有主 detail 区
- 有可持续操作入口

### 4.4 首轮 CTA 补齐

每个空状态都必须给明确动作：
- Channels: 连接 Telegram / 创建 webhook account
- Plugins: 安装本地插件 / 发现插件
- Automations: 从 `HEARTBEAT.md` 导入 / 新建自动化
- Canvas: 打开默认 workspace 画布 / 查看最近 artifact

## 5. 实施切片

### Slice A - Shell skeleton

- 重写 `App.tsx` 的整体布局
- 建立 `GlobalRail` / `ContextRail` / `MainStage`
- 把 locale、health、update 状态移到顶部 compact toolbar
- 当前状态：`Completed`。`shell-shared/`、`shell-v1/LegacyShell.tsx`、`shell-v2/*` 与默认 `shell-v2` / `?shell=v1` seam 已落地，且保留既有 `desk-tab-*` 自动化选择器。

### Slice B - Chat-first stage

- 重写 chat 模式布局
- timeline 与 composer 固定为核心舞台
- 侧栏显示 session / thread list
- 当前状态：`In Progress`。`ContextRail` 已开始显示 session pulse（当前主会话 + recent sessions），`MainStage` 也已切到更紧凑的 chat-specific stage header + denser operator canvas；recent sessions 现在还能直接跳到 `Sessions / Diagnostics` 并聚焦目标会话。最近又补上了 chat-first 视觉收口：chat 模式下左侧上下文卡片不再重复放大 desk 标题，而改用更轻的 `会话脉络 / Session pulse` 辅助文案，避免和主舞台抢层级。下一步会继续把 chat workbench 从“复用旧页面容器”进一步收拢为真正的 conversation-first operator stage。

### Slice C - Object-list patterns

- 为 Inbox / Sessions / Channels / Plugins / Canvas / Automations 统一 list-detail 模式
- 把现有 desk 卡片改为左列表 + 右详情
- 当前状态：`In Progress`。`Sessions / Diagnostics`、`Inbox / Approval` 与 `Models / Settings` 都已落下第一轮 pane 化：前者已有 session rail、focused detail stage、timeline/export sibling panels；中间这块已切到 stacked inbox/approval queues + linked detail stage；`Models / Settings` 则已有左侧模型列表 + 右侧 focused model composer，并补了 stage index 指向 runtime/update/risk panels。下一步继续把同类模式推广到 `Channels`、`Plugins`、`Automations` 与 `Canvas`，同时决定 `Models / Settings` 第二刀是否要把 supporting panels 进一步做成更强的 focus flow。

### Slice D - Empty-state CTA system

- 为所有空状态定义主动作、次动作、帮助文案
- 减少“只有说明文字、没有下一步”的页面

### Slice E - Visual refinement

- 调整字体层级、pane 分割、hover / selected 状态
- 引入更强的桌面应用感
- 收敛当前过重的 landing-page hero 感
- 当前状态：`In Progress`（首轮校准已完成）。`shell-v2.css` 已完成第一轮密度校准：缩小 chat/context 双标题层级差异、收紧卡片 padding/radius/shadow、减弱左侧辅助区存在感，并补上窄屏时 `MainStage` 优先于 `ContextRail` 的顺序规则。后续 visual refinement 主要保留给剩余 desk 的 hover/selected states 与更细的 pane 交互质感。

## 6. 验收标准

重构完成后，应满足：

1. 打开应用首屏即可进入主线程
2. 不滚动即可看到 timeline 和 composer
3. 所有产品域都通过左 rail 进入
4. 所有对象域都采用 list-detail 模式
5. 空状态都有可执行 CTA
6. 不改动现有 gateway contract 也能完成第一轮布局升级
7. `npm run build`、`npm run test`、`npm run test:e2e` 必须继续通过

## 7. 建议执行顺序

1. 先修 P0：Canvas auth / session state finalize
2. 再做 Slice A + B，把产品重心拉回主线程
3. 然后做 Slice C + D，补齐 Plugins / Channels / Automations / Canvas 的首轮上手路径
4. 最后做视觉精修和文档同步

## 8. 本轮结论

当前并不是“前端没有能力做参考图那种布局”，而是“现有 capability 已经足够，应该开始做”。

也就是说：

- **后端能力已经能支撑这一轮壳层重构**
- **下一轮价值最大的工作就是重构壳层，而不是继续堆 summary cards**
