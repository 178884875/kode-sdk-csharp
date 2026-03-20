# Iteration 6 Freeze: Desktop Shell v1

Last updated: 2026-03-19
Status: `Frozen` / `Ready`

这份文档用于冻结 Iteration 6 的产品范围、desktop bridge 边界、验收标准与并行写集，确保后续实现不会把 Desktop、Gateway、Web 和 Hardening 混在同一波里失控膨胀。

## 1. 目标

Iteration 6 的目标不是马上把 KodaClaw 变成“完整原生桌面套件”，而是把现有已经验收的本地 Gateway + Web 产品，升级成一个真正可常驻的桌面外壳：

- 让 `kodaclaw-desktop` 能稳定承载当前 `kodaclaw-web`
- 让桌面壳可以附着或启动本地 Gateway，而不是要求用户手动拼接 URL / token
- 提供最小可用的桌面能力：主窗口、tray/menu bar、通知、桌面内 deep-link 跳转
- 保持 `desktop is shell, gateway is brain`，不把 SDK runtime 或产品逻辑搬进 Electron 主进程
- 为后续开机启动、Keychain、自动更新和更深的 OS 集成留下稳定边界

## 2. 冻结后的首期范围

### 2.1 本期必须做实的能力

- `kodaclaw-desktop` 独立工程化：Electron 主进程、preload、打包 smoke、开发脚本
- BrowserWindow 承载现有 `kodaclaw-web`，而不是再做一套桌面专属 UI
- 运行时桌面配置桥：在 renderer 侧动态获得 `gatewayUrl`、`gatewayToken`、平台信息和初始 launch target
- Gateway 生命周期桥：desktop 能“附着已有 Gateway”或“受控启动一个本地 Gateway”
- tray / menu bar 菜单：显示窗口、打开关键 desk、退出桌面壳
- 单窗口生命周期：关闭窗口优先隐藏、从 tray 恢复
- 原生通知桥：基于现有 Inbox / Approval / Settings 数据做最小通知投递
- 桌面内 deep-link / launch target：能从 tray、通知或启动参数跳到 `chat` / `inbox` / `canvas` / `channels`
- 一条最小全局快捷键，用于唤起或聚焦主窗口
- 至少一个平台的 packaging smoke，优先以 macOS 为主验收平台

### 2.2 本期明确冻结的实现策略

- Iteration 6 v1 采用 `Electron + TypeScript` 作为首个桌面壳实现
- `kodaclaw-desktop` 只负责窗口、系统集成、Gateway 附着/启动、运行时配置与本地通知
- 业务状态、审批、诊断、消息、渠道、自动化等仍全部通过 `KodaClaw.Gateway` 暴露
- renderer 优先复用现有 `kodaclaw-web`，只补 runtime config / desktop launch target 适配，不复制业务组件
- Gateway 鉴权继续复用现有 Bearer token；desktop 不新建第二套桌面专属认证协议
- 桌面通知优先复用现有 Control Plane 面：`/api/inbox`、`/api/approvals`、`/api/settings`
- Iteration 6 v1 的 deep-link 优先冻结为“桌面壳内部 launch target 跳转”；系统级自定义协议注册可以存在，但不是第一波唯一验收门槛
- 打包 smoke 优先验证 shell 能运行和附着产品，而不是一次性解决自更新、签名分发、Keychain、.NET 自包含分发

### 2.3 本期明确非目标

- 重写一套桌面专属前端
- 把 SDK runtime、plugin host 或 channel connector 直接嵌进 Electron 主进程
- 做多窗口工作区、多账户桌面容器
- 实现完整自动更新、签名、公网安装器分发
- 把 Gateway token / model key / channel secret 的最终安全存储问题一并解决
- 做原生小组件、悬浮窗、全局剪贴板 Agent、系统级屏幕录制等更深度 OS 自动化

## 3. 可复用的现有产品 / SDK 基线

现有代码已经具备以下对 Desktop 有价值的基线：

- `KodaClaw.Gateway` 已经是独立 loopback API，可提供 health、bootstrap、inbox、approvals、sessions、models、automations、channels、plugins、canvas 等完整产品面
- Gateway 已具备 Bearer token 鉴权，不需要为桌面首期重新设计授权模型
- `kodaclaw-web` 已经完成八个 desk 的统一壳层，可以直接作为桌面 renderer 承载面
- `ControlPlane` 已具备 `notificationsEnabled`、quiet hours、Inbox / Approval 等与通知相关的基础数据
- `KodaClaw.Runtime`、`ChannelHub`、`Automation`、`PluginHost` 都已经通过 Gateway 收口，桌面壳无需直接管理这些模块的内部状态

同时也有几个必须在 Iteration 6 正视的缺口：

- 当前 `kodaclaw-web` 通过 Vite 编译时环境变量读取 `gatewayUrl` / `gatewayToken`，这不适合 packaged desktop 的运行时附着模型
- 当前没有 desktop preload / IPC / launch target bridge
- 当前没有 Gateway 受控启动与健康等待的桌面侧进程管理
- 当前没有通知轮询/去重/点击回跳能力
- 当前没有桌面 packaging smoke 工程

## 4. 冻结的 contract 决策

### 4.1 Shell 技术路线

- 首期桌面壳固定为 Electron
- `kodaclaw-desktop` 由三部分组成：
  - main process：窗口、tray、通知、Gateway 生命周期
  - preload：最小安全桥
  - renderer：现有 `kodaclaw-web`
- 不在 Iteration 6 v1 同时维护 Tauri/Electron 双壳

### 4.2 Renderer 复用策略

- 桌面壳必须复用现有 `kodaclaw-web`
- dev 模式允许 BrowserWindow 指向本地 Vite dev server
- packaged 模式允许 BrowserWindow 加载构建后的静态前端入口
- 不允许在 Iteration 6 首期新增第二套 React 状态树来复制现有 desks

### 4.3 运行时配置桥

桌面壳必须向 renderer 暴露一份冻结的运行时配置，而不是继续要求 web 只读构建时环境变量。

建议冻结以下 bridge shape：

| 类型 | 建议名称 | 说明 |
| --- | --- | --- |
| Record | `DesktopRuntimeConfig` | `gatewayUrl`、`gatewayToken`、`platform`、`desktopMode`、`initialTarget` |
| Record | `DesktopLaunchTarget` | `desk` + 可选 `entityId` / `route` / `reason` |
| Enum | `DesktopGatewayLifecycleMode` | `AttachOnly` / `ManagedChild` |
| Interface | `DesktopShellBridge` | `getRuntimeConfig()`、`openTarget()`、`onLaunchTarget()`、可选 `showWindow()` |

行为边界冻结为：

- renderer 读取配置的优先级为：desktop preload bridge > Vite env > browser fallback
- Gateway token 由 desktop 在运行时注入；不要求打包时把 token 烧进前端构建产物
- Desktop launch target 是 web 桌面适配的唯一入口，不直接让 renderer 读 Electron 原始 API

### 4.4 Gateway 生命周期策略

- desktop v1 同时支持两种模式：
  - `AttachOnly`：附着到一个已运行的本地 Gateway
  - `ManagedChild`：由 desktop 启动并监督一个本地 Gateway 子进程
- desktop 不重新实现 Gateway 逻辑，只做进程管理、健康等待、退出协调和重启入口
- Gateway 仍然只监听 loopback
- dev 模式可以通过 `dotnet run --project ...` 驱动 Gateway；后续打包时再演进到更稳定的可执行物路径

### 4.5 Auth 复用策略

- 继续复用现有 Bearer token
- desktop 不引入新的桌面 session cookie、命名管道认证或专属 nonce 握手
- web renderer 对 Gateway 的请求头策略保持不变，只是 token 来源从纯编译时变量扩展为运行时 bridge

### 4.6 Tray、窗口与快捷键

- 首期固定为单主窗口模型
- 关闭窗口默认优先隐藏，不直接退出进程
- tray / menu bar 至少提供：
  - Show KodaClaw
  - Open Chat
  - Open Inbox
  - Open Canvas
  - Open Channels
  - Restart Gateway
  - Quit
- 至少冻结一条全局快捷键用于显示或聚焦窗口；快捷键属于 shell 行为，不进入产品业务层

### 4.7 通知策略

- 桌面通知来源优先复用现有 Control Plane 数据，而不是新增独立通知数据库
- 最小通知关注对象：
  - Open inbox items
  - Pending approvals
  - automation / channel / plugin 的关键可见结果
- 通知必须复用 `notificationsEnabled` 与 quiet hours 设置
- 通知点击后进入桌面 launch target，而不是在通知体里实现完整业务交互

### 4.8 Deep-link / Launch Target 策略

- Iteration 6 v1 先冻结“桌面壳内部 deep-link”
- launch target 至少支持：
  - `chat`
  - `inbox`
  - `sessions`
  - `models`
  - `automations`
  - `channels`
  - `plugins`
  - `canvas`
- target 可带可选附加信息，如 `entityId`、`route`、`reason`
- 系统协议注册可作为实现手段之一，但第一波验收重点是 tray / 通知 / 启动参数能稳定跳到正确桌面视图

### 4.9 平台与打包策略

- 首个强验证平台冻结为 macOS
- 代码组织尽量保持跨平台中立，不硬编码仅 macOS 才能编译的核心路径
- 首期 packaging smoke 以“应用可启动、可附着/启动 Gateway、可显示桌面壳”为准
- 安装器签名、自更新、.NET 自包含分发优化留到后续波次或 Iteration 7

## 5. Gateway / Web 复用面

Iteration 6 首期应优先复用以下现有 API 面，而不是追加新的桌面专属后端：

- `GET /api/system/health`
- `GET /api/system/bootstrap-state`
- `GET /api/inbox`
- `GET /api/approvals`
- `GET /api/settings`
- 现有各 desk 依赖的 `/api/*` 领域

这意味着 Iteration 6 首期更像是“连接已存在的产品中枢”，而不是“再发明一个桌面专属 backend”。

## 6. Web 侧必须适配的边界

虽然 `kodaclaw-web` 被复用，但 Iteration 6 首期至少要补齐：

- runtime config 读取能力，而不是只依赖 `VITE_KODACLAW_GATEWAY_URL` / `VITE_KODACLAW_GATEWAY_TOKEN`
- launch target / desk target 入口，允许 desktop 指定初始 desk 或通知回跳 desk
- 保持 browser-only 模式继续可运行，不让桌面适配破坏现有 web 开发体验

## 7. 推荐并行波次

### Wave 0：planning freeze

- 主线程：`Completed`
- 交付：
  - `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_6_FREEZE.md`
  - `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/adr/ADR-0010-desktop-shell-v1.md`
  - backlog / status / README / parallel-plan 同步

### Wave 1：shell foundation

- Worker A：`KC-0601`
  - 写集：`apps/kodaclaw-desktop`
  - 交付：Electron 工程骨架、BrowserWindow、preload、开发/打包 smoke 脚本
- Worker B：`KC-0603`
  - 写集：`apps/kodaclaw-desktop`、`apps/kodaclaw-web/src/lib`、`apps/kodaclaw-web/src/App.tsx`
  - 交付：runtime config bridge、auth bridge、desktop launch target 基础 contract
- 主线程：冻结 desktop bridge types、收口 dev/prod renderer 加载与基础验证

### Wave 2：gateway bridge + shell lifecycle

- Worker A：`KC-0602`
  - 写集：`apps/kodaclaw-desktop`
  - 交付：Gateway attach/start bridge、健康等待、restart action
- Worker B：`KC-0604`
  - 写集：`apps/kodaclaw-desktop`
  - 交付：tray / menu bar、窗口 show-hide 语义、快捷键
- 主线程：完成 desktop shell integration 与 bridge contract 收口

### Wave 3：desktop operator surfaces

- Worker A：`KC-0605`
  - 写集：`apps/kodaclaw-desktop`、必要时 `kodaclaw-web`
  - 交付：通知轮询 / 去重 / quiet-hours 复用
- Worker B：`KC-0606`
  - 写集：`apps/kodaclaw-desktop`、`apps/kodaclaw-web`
  - 交付：launch target / deep-link 到 chat / inbox / canvas / channels
- 主线程：做 web 适配与桌面端整合验证

### Wave 4：acceptance

- Worker A：`KC-0607`
  - 写集：`apps/kodaclaw-desktop`、CI/脚本相关文件
  - 交付：至少一个平台的 packaging smoke
- Worker B：`KC-0608`
  - 写集：`docs/ITERATION_6_ACCEPTANCE_PACK.md`、`tests` / `apps` 的 acceptance harness
  - 交付：桌面 dogfood 验收包
- 主线程：执行全量回归、文档同步、关闭 worker

## 8. 退出标准

Iteration 6 退出标准冻结为：

1. `kodaclaw-desktop` 可作为独立桌面壳运行
2. desktop 能附着或启动本地 Gateway，而不要求用户手动维护 renderer token 配置
3. renderer 通过 runtime desktop bridge 获得 `gatewayUrl` / `gatewayToken`
4. tray / menu bar 可以恢复窗口并跳转到关键 desk
5. 桌面通知可以基于现有 Inbox / Approval / Settings 工作，并尊重 quiet hours
6. 桌面内 deep-link / launch target 可以打开正确 desk
7. 至少一个平台的 packaging smoke 可通过
8. `desktop is shell, gateway is brain` 仍成立：业务事实源仍在 Gateway / Control Plane
9. Iteration 5 已冻结的 Channels / Plugin / Automation / Control Plane 回归不能被破坏
