# KodaClaw 全量任务分解

这份文档把 KodaClaw 从迭代 0 到迭代 7 的任务拆成可以独立开发、独立验证、独立合并的工作项。

使用规则：

- 一个任务尽量对应一个 PR
- 一个任务必须有明确输出和独立验证方式
- 一个任务默认只归属于一个主模块，避免多人同时改同一写集
- 需要跨模块时，优先先落 contract，再做实现
- 若任务依赖前置 contract，则不得提前并行开工

任务编号规则：

- `KC-00xx`：迭代 0
- `KC-01xx`：迭代 1
- `KC-02xx`：迭代 2
- `KC-03xx`：迭代 3
- `KC-04xx`：迭代 4
- `KC-05xx`：迭代 5
- `KC-06xx`：迭代 6
- `KC-07xx`：迭代 7

表字段说明：

- `输出`：任务完成后可交付的最小结果
- `验证`：该任务最小独立验证方式
- `依赖`：必须先完成的前置任务
- `并行泳道`：建议归属的实现泳道，便于后续 subagent 并行

## 迭代 0：产品底座

| ID | 任务 | 输出 | 验证 | 依赖 | 并行泳道 |
| --- | --- | --- | --- | --- | --- |
| KC-0001 | 建立 `KodaClaw.sln` 与目录级构建约定 | 独立 solution、共享 props、基本引用结构 | `dotnet build` smoke | 无 | Foundation |
| KC-0002 | 创建 `KodaClaw.Contracts` 并定义基础领域枚举 | `SessionKind`、`AppMode`、通用错误模型草案 | 单测 + 编译 | KC-0001 | Foundation |
| KC-0003 | 创建 `tests/` 分层工程骨架 | Unit/Integration/Contract/E2E 测试项目占位 | 测试项目可还原可编译 | KC-0001 | QA |
| KC-0004 | 创建 `kodaclaw-web` 工程骨架与 API client 外壳 | 前端应用可启动，API adapter 占位 | `npm run build` / `pnpm build` | KC-0001 | Web |
| KC-0005 | 创建 `kodaclaw-desktop` 工程骨架 | Electron/Tauri 壳占位、运行脚本占位 | shell smoke | KC-0001 | Desktop |
| KC-0006 | 建立本地开发配置约定 | `.env.example`、dev config、端口/token 规则文档 | 文档审查 + config parse smoke | KC-0001 | Foundation |
| KC-0007 | 建立 ADR / Slice / PR 模板与工作流 | 模板文件与文档入口 | 文档审查 | KC-0001 | Foundation |
| KC-0008 | 建立最小 CI smoke 方案 | build/test/typecheck 占位 pipeline | 本地/CI smoke | KC-0001, KC-0003, KC-0004 | DevEx |

## 迭代 1：Core Assistant Alpha

| ID | 任务 | 输出 | 验证 | 依赖 | 并行泳道 |
| --- | --- | --- | --- | --- | --- |
| KC-0101 | 实现 Workspace 初始化器 | `~/.kodaclaw` 目录与默认文件生成 | 临时目录集成测试 + golden tree | KC-0002 | Workspace |
| KC-0102 | Workspace fixture 与 golden tests | fixture workspace、golden outputs | Contract tests | KC-0101, KC-0003 | QA |
| KC-0103 | Gateway 最小宿主 | loopback ASP.NET Core host | `dotnet run` + health smoke | KC-0001, KC-0002 | Gateway |
| KC-0104 | Gateway token 与 bootstrap-state API | token auth、health/bootstrap-state 契约 | HTTP integration + contract tests | KC-0103, KC-0101 | Gateway |
| KC-0105 | Main session runtime 组合层 | `SessionKind.main`、Agent factory、store 绑定 | Runtime integration tests | KC-0002, KC-0101 | Runtime |
| KC-0106 | Chat SSE contract 与 API | `/api/chat/stream`、chunk/done/error 事件 | SSE replay/contract tests | KC-0103, KC-0105 | Gateway |
| KC-0107 | Bootstrap runtime flow | 首次启动识别、bootstrap mode、落盘逻辑 | Integration + golden file tests | KC-0101, KC-0105 | Workspace |
| KC-0108 | 主聊天 Web 壳 | 聊天页、输入框、流式消息渲染 | Frontend component + Playwright smoke | KC-0004, KC-0106 | Web |
| KC-0109 | Bootstrap Web 流程 | 首次启动时的 bootstrap UI 路由与交互 | Playwright bootstrap flow | KC-0104, KC-0107, KC-0108 | Web |
| KC-0110 | 主会话恢复 | 最近主会话恢复与 resume fallback | create-close-resume integration | KC-0105 | Runtime |
| KC-0111 | 最小 diagnostics 基线 | correlation id、关键生命周期日志/查询入口 | Contract test + manual failure drill | KC-0103, KC-0105 | ControlPlane |
| KC-0112 | 迭代 1 端到端验收包 | bootstrap -> chat -> close -> resume 全链路验证 | E2E + dogfood | KC-0104~KC-0111 | QA |

## 迭代 2：Control Plane Beta

| ID | 任务 | 输出 | 验证 | 依赖 | 并行泳道 |
| --- | --- | --- | --- | --- | --- |
| KC-0201 | Inbox 领域模型与存储 | `InboxItem` schema、SQLite repo | Repo unit + integration | KC-0002 | ControlPlane |
| KC-0202 | Approval 领域模型与存储 | `Approval` schema、状态流转仓储 | Repo unit + integration | KC-0002 | ControlPlane |
| KC-0203 | Runtime 接入审批流 | 高风险动作挂起与 approval linkage | Runtime integration tests | KC-0202, KC-0105 | Runtime |
| KC-0204 | Inbox API | 列表、详情、状态更新 API | HTTP integration + contract tests | KC-0201, KC-0103 | Gateway |
| KC-0205 | Approval API | 待批列表、批准、拒绝 API | HTTP integration + contract tests | KC-0202, KC-0203, KC-0103 | Gateway |
| KC-0206 | Sessions 索引与详情 API | session 列表、详情、状态摘要 | HTTP integration | KC-0110, KC-0103 | Gateway |
| KC-0207 | Diagnostics 查询 API | diagnostics/timeline 查询接口 | Contract tests + integration | KC-0111, KC-0103 | Gateway |
| KC-0208 | Model registry 基础版 | endpoint CRUD、默认模型设置 | Repo tests + API integration | KC-0002, KC-0103 | ModelHub |
| KC-0209 | Settings 基础版 | 应用级设置 schema/API | Integration tests | KC-0002, KC-0103 | ControlPlane |
| KC-0210 | Inbox/Approval Web 页面 | inbox 列表、审批处理页 | Component + Playwright | KC-0204, KC-0205, KC-0108 | Web |
| KC-0211 | Sessions/Diagnostics Web 页面 | sessions 列表、详情、timeline 页面 | Component + Playwright | KC-0206, KC-0207, KC-0108 | Web |
| KC-0212 | Models/Settings Web 页面 | endpoint 管理与默认模型设置页 | Component + Playwright | KC-0208, KC-0209, KC-0108 | Web |
| KC-0213 | 迭代 2 场景验收包 | 审批暂停、处理、回流 inbox 全链路 | E2E + dogfood | KC-0201~KC-0212 | QA |

## 迭代 3：Automation + Canvas

| ID | 任务 | 输出 | 验证 | 依赖 | 并行泳道 |
| --- | --- | --- | --- | --- | --- |
| KC-0301 | Automation 领域模型与存储 | `AutomationDefinition`、run history schema | Repo tests | KC-0002 | Automation |
| KC-0302 | 可控时钟与 durable scheduler 基础 | fake clock、scheduler host、持久化恢复 | Integration tests | KC-0301 | Automation |
| KC-0303 | `HEARTBEAT.md` 解析器 | Markdown -> automation definitions 编译逻辑 | Parser unit + golden tests | KC-0101, KC-0301 | Workspace |
| KC-0304 | Automation 运行时上下文 | `SessionKind.automation` 与最小上下文加载 | Runtime integration | KC-0301, KC-0105 | Runtime |
| KC-0305 | Automation 结果投递到 Inbox | 成功/失败结果写入 inbox | Integration tests | KC-0201, KC-0302, KC-0304 | Automation |
| KC-0306 | Canvas artifact 领域模型 | `CanvasArtifact` schema 与索引 | Repo tests | KC-0002 | Canvas |
| KC-0307 | Canvas 服务与 API | artifact 写入、查询、默认入口 | API integration + contract tests | KC-0306, KC-0103 | Gateway |
| KC-0308 | Automations Web 页面 | automation 列表、执行历史、启停 | Component + Playwright | KC-0301, KC-0302, KC-0305, KC-0108 | Web |
| KC-0309 | Canvas Web 页面 | canvas 列表、artifact 打开与渲染 | Component + Playwright | KC-0307, KC-0108 | Web |
| KC-0310 | 迭代 3 场景验收包 | heartbeat -> job run -> inbox/canvas 全链路 | E2E + dogfood | KC-0301~KC-0309 | QA |

## 迭代 4：Plugin Platform

| ID | 任务 | 输出 | 验证 | 依赖 | 并行泳道 |
| --- | --- | --- | --- | --- | --- |
| KC-0401 | Plugin manifest schema/validator | `plugin.json` schema 与校验器 | Unit + contract tests | KC-0002 | Plugin |
| KC-0402 | Plugin registry 与存储 | 插件注册、状态、版本、路径信息 | Repo tests | KC-0401 | Plugin |
| KC-0403 | Plugin lifecycle host | stdio/http MCP 插件启动、停止、重连 | Fixture plugin integration | KC-0402, KC-0103 | Plugin |
| KC-0404 | Plugin 权限模型 | network/filesystem/background/secrets 权限声明 | Unit + contract tests | KC-0401 | Plugin |
| KC-0405 | Runtime 插件工具注入 | namespaced MCP tools 注入到 session | Runtime integration | KC-0403, KC-0105 | Runtime |
| KC-0406 | Plugin 日志与健康检查 | per-plugin logs、health state、degraded 标记 | Integration tests | KC-0403 | Plugin |
| KC-0407 | Plugin manager Web 页面 | 插件列表、启停、权限、日志入口 | Component + Playwright | KC-0402, KC-0406, KC-0108 | Web |
| KC-0408 | 内置示例插件与验收包 | 至少一个可安装的 fixture plugin | E2E + dogfood | KC-0401~KC-0407 | QA |

## 迭代 5：Channels

| ID | 任务 | 输出 | 验证 | 依赖 | 并行泳道 |
| --- | --- | --- | --- | --- | --- |
| KC-0501 | Channel connector 抽象 | 统一 connector 接口与事件规范 | Unit tests | KC-0002 | Channel |
| KC-0502 | Thread binding 领域模型与存储 | 外部 thread -> session 映射 | Repo tests | KC-0501 | Channel |
| KC-0503 | Channel policy 引擎 | 私聊/群聊记忆加载与回复策略 | Unit + policy tests | KC-0501, KC-0101 | Channel |
| KC-0504 | DeliveryRule 与审批联动 | 自动发/草稿待批/全部待批 | Integration tests | KC-0202, KC-0503 | ControlPlane |
| KC-0505 | Telegram connector | inbound/outbound、account binding | Fixture connector integration | KC-0501, KC-0403 | Channel |
| KC-0506 | Generic webhook connector | 通用 webhook 渠道接入 | Integration tests | KC-0501, KC-0103 | Channel |
| KC-0507 | Channel session runtime | `channel_dm` / `channel_group` session 组合层 | Runtime integration | KC-0502, KC-0503, KC-0105 | Runtime |
| KC-0508 | Channels Web 页面 | 账号绑定、thread 列表、策略状态 | Component + Playwright | KC-0502, KC-0505, KC-0506, KC-0108 | Web |
| KC-0509 | Channel diagnostics 与审计 | inbound/outbound event 追踪 | Contract + integration tests | KC-0505, KC-0506, KC-0111 | Gateway |
| KC-0510 | 私聊场景验收包 | Telegram DM -> 独立 session -> 回复草稿/发送 | E2E + dogfood | KC-0501~KC-0509 | QA |
| KC-0511 | 群聊安全场景验收包 | 群聊不读取主记忆、受 delivery rule 约束 | E2E + dogfood | KC-0503, KC-0504, KC-0507, KC-0509 | QA |

## 迭代 6：Desktop Shell

| ID | 任务 | 输出 | 验证 | 依赖 | 并行泳道 |
| --- | --- | --- | --- | --- | --- |
| KC-0601 | Desktop 壳工程化 | Electron/Tauri 可运行壳工程 | Packaging smoke | KC-0005 | Desktop |
| KC-0602 | Gateway 启动/附着桥接 | desktop 能启动或连接本地 gateway | Integration smoke | KC-0601, KC-0103 | Desktop |
| KC-0603 | Token/认证桥接 | desktop 与 gateway 安全握手 | Bridge tests | KC-0602, KC-0104 | Desktop |
| KC-0604 | Tray 与窗口管理 | 托盘菜单、主窗口、隐藏/唤起 | Manual smoke + shell tests | KC-0601 | Desktop |
| KC-0605 | 系统通知桥 | inbox/chat/canvas 通知跳转 | Integration smoke | KC-0602, KC-0204, KC-0307 | Desktop |
| KC-0606 | Deep link 与路由定位 | 直接打开 chat/inbox/canvas 目标页 | Contract + manual tests | KC-0602, KC-0108 | Desktop |
| KC-0607 | 桌面分发 smoke | macOS/Windows 至少一端打包 smoke | Packaging test | KC-0601~KC-0606 | Desktop |
| KC-0608 | 桌面 dogfood 验收包 | 常驻运行、托盘、通知、恢复整体验证 | Dogfood | KC-0601~KC-0607 | QA |

## 迭代 7：Hardening

| ID | 任务 | 输出 | 验证 | 依赖 | 并行泳道 |
| --- | --- | --- | --- | --- | --- |
| KC-0701 | OS Keychain 集成 | secrets 安全存储抽象与实现 | Integration tests | KC-0002 | Security |
| KC-0702 | Secret 注入迁移 | 模型、渠道、插件凭据从文件/env 迁移 | Manual migration + tests | KC-0701, KC-0208, KC-0505 | Security |
| KC-0703 | Device identity 完整化 | device fingerprint、rotation、metadata | Unit + integration tests | KC-0101 | Security |
| KC-0704 | Backup / export / import | workspace + control plane 导出导入 | Integration + disaster drill | KC-0201, KC-0202, KC-0301 | Storage |
| KC-0705 | Crash recovery / session repair | 异常退出后 store/session 修复策略 | Integration tests + manual drill | KC-0110 | Runtime |
| KC-0706 | Plugin trust model | trusted/untrusted/signed 占位模型 | Unit + integration tests | KC-0402, KC-0404 | Plugin |
| KC-0707 | Update mechanism 脚手架 | 版本检查、更新策略占位 | Smoke + manual test | KC-0601 | Desktop |
| KC-0708 | Sandbox policy UI 与风险提示 | local/docker 边界说明、权限展示 | Component + manual review | KC-0404, KC-0108 | Web |
| KC-0709 | Diagnostic bundle 导出 | 一键导出日志、timeline、session metadata | Integration tests | KC-0111, KC-0207 | ControlPlane |
| KC-0710 | Hardening 最终验收包 | 迁移、恢复、权限、更新、导出等综合验证 | E2E + dogfood + drills | KC-0701~KC-0709 | QA |

## 推荐的跨迭代并行泳道

建议后续长期按 7 条泳道拆开发：

- `Foundation / DevEx`
- `Workspace / Bootstrap`
- `Gateway / ControlPlane`
- `Runtime / ModelHub`
- `Web / Desktop`
- `Plugin / Channel`
- `QA / Contract / E2E`

## 任务拆解落地规则

后续开始写代码时，建议采用：

1. 先从这份清单里选任务
2. 为该任务复制一份 `docs/templates/CAPABILITY_SLICE_TEMPLATE.md`
3. 若涉及架构决策，再补一份 ADR
4. 实现完成后，在 PR 中引用任务 ID
5. 合并前必须写明本任务的独立验证证据
