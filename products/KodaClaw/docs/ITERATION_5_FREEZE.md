# Iteration 5 Freeze: Channels v1

Last updated: 2026-03-19
Status: `Frozen` / `Ready`

这份文档用于冻结 Iteration 5 的产品范围、contract surface、验收标准与并行写集，确保后续实现可以按清晰边界推进，而不会把 Channels、Plugin、Desktop 三条线重新缠在一起。

## 1. 目标

Iteration 5 的目标不是把所有外部渠道一次性做成“完整消息中台”，而是把 KodaClaw 的第一条受控外部会话入口做实：

- 让外部消息可以进入 KodaClaw，并稳定映射到隔离的 channel session
- 建立 `ThreadBinding`、`ChannelPolicy`、`DeliveryRule` 三个核心产品对象
- 复用现有 Control Plane 的审批、Inbox、diagnostics，而不是新起一套渠道控制面
- 提供最小可用的 Channels Web desk，让用户能看见绑定、线程、策略与发送状态
- 为后续插件化渠道与 Desktop 通知接入留下稳定边界

## 2. 冻结后的首期范围

### 2.1 本期必须做实的能力

- `ChannelConnector` 统一抽象与标准化事件模型
- `ThreadBinding` 持久化：外部 thread -> 内部 session 映射
- `ChannelPolicy`：私聊 / 群聊的记忆边界与回复约束
- `DeliveryRule`：自动发送 / 草稿待批 / 全部待批
- `ApprovalKind.ChannelDelivery` + `InboxItemKind.ChannelUpdate` 复用链路
- `SessionKind.ChannelDirectMessage` / `SessionKind.ChannelGroup` 运行时组合层
- 一个真实 connector：`telegram`
- 一个合成 / 测试 connector：`generic-webhook`
- Channels Web 页面：账号、线程、策略、待发送 / 审批状态、审计摘要
- channel inbound / outbound diagnostics 与审计查询
- DM acceptance 与 group safety acceptance

### 2.2 本期明确冻结的实现策略

- Iteration 5 v1 的 connector 由 `KodaClaw.ChannelHub` 直接托管，不要求走 plugin runtime execution path
- `telegram` 是第一条真实渠道路径，但首期只做单 bot token、单 account 基线
- `telegram` v1 采用 bot token + long polling 作为首条可运行接入路径，不要求 webhook 注册链路
- `generic-webhook` 主要服务于标准化测试、内部集成和 acceptance，不把它扩展成大规模 fan-out 平台
- 渠道会话永远不复用 `main` session；必须进入专属 channel session

### 2.3 本期明确非目标

- QQ / WhatsApp / WeCom / DingTalk 等更多 connector
- 多账号复杂路由与租户隔离
- Telegram webhook 注册、Webhook 签名编排、Bot 市场化配置流程
- 富媒体编辑器、消息模版系统、批量群发 / 广播
- OS Keychain 正式接管渠道密钥
- Desktop 通知联动与系统级渠道入口
- 把 `channel` plugin type 跑成与 `tool` plugin 同等级的插件执行链

## 3. 可复用的现有产品 / SDK 基线

现有代码已经具备以下可复用能力：

- `KodaClaw.Contracts` 已预留：`SessionKind.ChannelDirectMessage`、`SessionKind.ChannelGroup`、`ApprovalKind.ChannelDelivery`、`InboxItemKind.ChannelUpdate`、`PluginType.Channel`
- `KodaClaw.Gateway` 架构文档已保留 `/api/channels/*` 领域
- `KodaClaw.Runtime` 已具备按 `SessionKind` 创建独立 session 的基础能力
- Control Plane 已具备 approvals / inbox / diagnostics / settings / shared SQLite baseline，可直接承载渠道审批、更新与审计
- Iteration 4 已完成 Plugin Platform v1，但 `channel` plugin type 只做到 manifest 可识别与展示，没有冻结真实运行链路

因此，Iteration 5 不需要再发明新的审批系统、日志系统或 session store；重点是把渠道入口、线程绑定、策略、回复交付和 Web 管理面串起来。

## 4. 冻结的 contract 决策

## 4.1 Connector 架构策略

- 长期方向仍然是“多数渠道通过 plugin capability 接入”
- 但 Iteration 5 v1 不要求把渠道首先做成 PluginHost 内运行的 `channel` 插件
- 首期 connector 由 `KodaClaw.ChannelHub` 直接拥有生命周期与事件标准化逻辑
- 后续若把 Telegram / webhook 迁回 plugin model，也必须兼容本期冻结的 normalized event、thread binding 与 delivery rule 语义

这条决策的意义是：先把产品级渠道语义冻结，再决定后续 connector 的托管形式。

## 4.2 Telegram v1 策略

- 首期只支持 bot token 绑定
- 首期只要求文本消息、基本 sender/chat metadata、简单 reply path
- 首期 inbound 采用 long polling，可接受一个固定 polling worker
- 首期 outbound 只要求最小文本发送链路，不做富媒体与 message edit orchestration
- token 不直接明文落库；在 Keychain 能力到位前，只保存 token 引用名或 dev-config 引用

## 4.3 Generic Webhook v1 策略

- 作为第二条 connector，用于标准化事件回放、集成测试与内部系统接入
- 接收最小事件子集：`message.received`、`message.edited`、`message.deleted`、`reaction.received`
- 不以公网大规模接入为目标；Iteration 5 只要求 loopback / integration 可运行
- 首期只要求最小 shared-secret / account-bound inbound 校验，不扩展成复杂认证平台

## 4.4 Thread binding 模型

每个 binding 的键语义冻结为：

- `connectorKind`
- `accountId`
- `externalThreadId`

每个 binding 至少持有：

- `bindingId`
- `sessionId`
- `sessionKind`
- `channelIdentity`
- `policyId`
- `deliveryRuleId`
- `lastInboundAt`
- `lastOutboundAt`
- `lastMessagePreview`

行为边界冻结为：

- 一个外部 thread 只能绑定到一个 channel session
- channel thread 不允许绑定到 `SessionKind.Main`
- 群聊与私聊必须分开建 session
- 后续 inbound 命中已存在 binding 时必须优先复用原 session

## 4.5 Channel policy 与记忆边界

### `ChannelDirectMessage`

- 可读取：`AGENTS.md`、`IDENTITY.md`、`SOUL.md`、`USER.md`
- 可读取：thread-local / channel-local 摘要（若存在）
- 不默认读取：`MEMORY.md`
- 不默认读取：主会话日记式 memory 文件

### `ChannelGroup`

- 可读取：最小身份提示、群聊策略、thread-local 摘要（若存在）
- 不读取：`USER.md`
- 不读取：`MEMORY.md`
- 不读取：主会话 / 私聊长期记忆

这意味着 Iteration 5 的首要目标不是“让渠道会话尽可能聪明”，而是先把隐私和上下文边界做对。

## 4.6 DeliveryRule 与审批联动

Iteration 5 冻结三种 delivery mode：

- `AutoSend`
- `DraftApproval`
- `RequireApproval`

行为冻结为：

- `AutoSend`：允许在策略通过、静默限制未触发时直接发送回复
- `DraftApproval`：生成草稿，进入统一 approval / inbox 链路，待用户确认后送出
- `RequireApproval`：任何 outbound delivery 尝试都先进入审批，不允许直接投递

并且：

- 审批类型统一复用 `ApprovalKind.ChannelDelivery`
- Inbox 展示统一复用 `InboxItemKind.ChannelUpdate`
- 审批通过后，由 ChannelHub 使用冻结的 outbound envelope 再次投递
- 审批拒绝后，必须留下审计记录，但不得触发真实发送

## 4.7 审计与 diagnostics

Iteration 5 不新建“渠道专属日志子系统”，而是冻结为：

- 关键 inbound / outbound / approval / delivery failure 事件进入统一 diagnostics
- 频道 / thread 维度的活动查询通过 channel audit API 暴露
- 审计记录至少保留 `connectorKind`、`accountId`、`bindingId`、`sessionId`、`eventType`、`approvalId`、`deliveryMode`、`createdAt`

## 4.8 Secrets 与配置

- Iteration 5 允许渠道凭据继续通过环境变量或 dev config 引用注入
- SQLite 只保存非敏感 connector/account 配置与 token reference
- OS Keychain 集成继续留在 Iteration 7 hardening

## 5. 建议冻结的 shared contract surface

下列 contract 建议在实现开始前先落到 `KodaClaw.Contracts`：

| 类型 | 建议名称 | 说明 |
| --- | --- | --- |
| Enum | `ChannelConnectorKind` | `Telegram` / `GenericWebhook` |
| Enum | `ChannelAccountState` | `Disconnected` / `Connecting` / `Connected` / `Degraded` |
| Enum | `ChannelThreadType` | `DirectMessage` / `Group` |
| Enum | `ChannelEventType` | `MessageReceived` / `MessageEdited` / `MessageDeleted` / `ReactionReceived` / `AccountConnected` / `AccountDisconnected` / `DeliveryFailed` |
| Enum | `DeliveryMode` | `AutoSend` / `DraftApproval` / `RequireApproval` |
| Record | `ChannelAccount` | connector account 持久化与列表 DTO |
| Record | `ThreadBinding` | 外部 thread -> session 绑定实体 |
| Record | `ChannelIdentity` | 外部用户 / 群组身份 |
| Record | `ChannelPolicy` | 记忆加载、回复限制、mute、allowed chat types |
| Record | `DeliveryRule` | outbound delivery 策略 |
| Record | `ChannelEventEnvelope` | 统一 inbound / outbound 事件模型 |
| Record | `ChannelOutboundDraft` | 待发送草稿 / 审批 payload |
| Record | `ChannelThreadSummary` | Channels desk 列表摘要 |
| Record | `ChannelThreadDetail` | 线程详情页 / 审计详情 DTO |
| Record | `ChannelAuditEntry` | 线程级审计记录 |
| Record | `ChannelQuery` | `/api/channels/threads` 查询条件 |
| Record | `ChannelsQueryResponse` | Channels 列表响应 |
| Request | `UpsertChannelAccountRequest` | 创建 / 更新 connector account 请求 |
| Request | `UpdateChannelPolicyRequest` | 更新策略请求 |
| Request | `UpdateDeliveryRuleRequest` | 更新 delivery rule 请求 |
| Interface | `IChannelAccountRepository` | account 持久化接口 |
| Interface | `IThreadBindingRepository` | binding 持久化接口 |
| Interface | `IChannelAuditRepository` | audit 持久化接口 |
| Interface | `IChannelConnector` | connector 生命周期与收发抽象 |

实现注意：

- `ThreadBinding` 必须显式承载 `SessionKind`，避免 Runtime 再按 connector 猜类型
- `ChannelEventEnvelope` 要作为 connector 与 ChannelHub 之间唯一标准化边界
- Web `src/types/contracts.ts` 必须同步新的 channel contracts，不能再写前端私有 shape

## 6. Gateway API v1 冻结面

Iteration 5 建议冻结以下最小 API 面：

- `GET /api/channels/connectors`
- `GET /api/channels/accounts`
- `POST /api/channels/accounts`
- `GET /api/channels/threads`
- `GET /api/channels/threads/{bindingId}`
- `PATCH /api/channels/threads/{bindingId}/policy`
- `PATCH /api/channels/threads/{bindingId}/delivery-rule`
- `GET /api/channels/threads/{bindingId}/audit`
- `POST /api/channels/webhook/{accountId}/events`

API 约束：

- `threads` 列表必须带出 connector、chat type、delivery mode、last activity、approval status 摘要
- `detail` 必须带出 binding、policy、delivery rule、最近审计记录、当前 session 摘要
- Telegram inbound 不要求直接暴露公开 webhook API；其生命周期由 ChannelHub 后台 worker 管理
- outbound 审批不新增专用 approve API，继续复用现有 `/api/approvals/*`

## 7. Channels Web desk 冻结面

Iteration 5 Web 页面只冻结到以下范围：

- connector account 列表与连接状态
- channel thread 列表与筛选
- thread 详情：policy、delivery rule、session kind、最近审计记录
- pending approval / draft state 摘要
- Telegram / webhook 的最小绑定入口

本期不做：

- 全量消息时间线重放 UI
- 复杂富媒体编辑器
- 渠道设置的多页流程向导
- Desktop notification 联动入口

## 8. 建议分波与并行写集

### Wave 0：主线程冻结（已完成）

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_5_FREEZE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/adr/ADR-0009-channels-v1.md`

### Wave 1：foundation

前提：主线程先落 shared channel contracts。

- Worker A：`KC-0501` + `KC-0502`
  - 写集：`src/KodaClaw.Contracts`、`src/KodaClaw.ChannelHub`、`tests/KodaClaw.UnitTests/ChannelHub`
- Worker B：`KC-0503`
  - 写集：`src/KodaClaw.ChannelHub/Policy`、`tests/KodaClaw.UnitTests/ChannelHub`
- Worker C：`KC-0504`
  - 写集：`src/KodaClaw.ControlPlane`、`src/KodaClaw.Gateway`、`tests/KodaClaw.IntegrationTests/Gateway`
- 主线程：contract 收口、SQLite schema 审查、最终验证与文档同步

### Wave 2：connectors + diagnostics

- Worker A：`KC-0505`
  - 写集：`src/KodaClaw.ChannelHub/Connectors/Telegram`、对应 integration fixtures/tests
- Worker B：`KC-0506`
  - 写集：`src/KodaClaw.ChannelHub/Connectors/Webhook`、`tests/KodaClaw.IntegrationTests/Gateway`
- Worker C：`KC-0509`
  - 写集：`src/KodaClaw.ChannelHub/Audit`、`src/KodaClaw.Gateway`、`tests/KodaClaw.ContractTests`、`tests/KodaClaw.IntegrationTests/Gateway`
- 主线程：connector contract 收口、Gateway 组装、交叉依赖整合

### Wave 3：runtime + web

- Worker A：`KC-0507`
  - 写集：`src/KodaClaw.Runtime`、`tests/KodaClaw.IntegrationTests/Runtime`
- Worker B：`KC-0508`
  - 写集：`apps/kodaclaw-web/src/components/ChannelsDesk.tsx`、对应 component/Playwright tests
- 主线程：shared DTO / API client / App desk integration、回归验证

### Wave 4：acceptance

- Worker A：`KC-0510`
  - 写集：`tests/KodaClaw.IntegrationTests/Smoke`
- Worker B：`KC-0511`
  - 写集：`tests/KodaClaw.IntegrationTests/Smoke`、`docs/ITERATION_5_ACCEPTANCE_PACK.md`
- 主线程：执行定向 + 全量回归、同步 backlog/status/README、关闭所有 worker

## 9. 验收门槛

Iteration 5 退出标准冻结为：

1. 用户可以创建一个 Telegram account 与一个 generic webhook account
2. 外部 DM inbound 可以创建或恢复独立 `SessionKind.ChannelDirectMessage`
3. 外部 group inbound 可以创建或恢复独立 `SessionKind.ChannelGroup`
4. 同一 external thread 的后续消息会命中同一个 `ThreadBinding`
5. `ChannelGroup` 默认不读取主记忆，且不会落到 `main` session
6. `DraftApproval` / `RequireApproval` 会稳定生成 `ApprovalKind.ChannelDelivery` + `InboxItemKind.ChannelUpdate`
7. 审批通过后可以成功发送；审批拒绝后不会真实发送，但会留下审计记录
8. Channels Web desk 能看到账号、线程、策略、最近活动与审批状态
9. 至少一个真实 Telegram DM 场景与一个群聊安全场景通过 acceptance pack
10. Iteration 5 完成后，Iteration 4 plugin baseline 仍保持回归通过

