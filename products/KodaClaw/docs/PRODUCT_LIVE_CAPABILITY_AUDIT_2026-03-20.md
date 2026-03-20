# PRODUCT Live Capability Audit (2026-03-20)

Last updated: 2026-03-20
Test target:
- Web: `http://127.0.0.1:4173`
- Gateway: `http://127.0.0.1:5076`
- Workspace: `/Users/vanzheng/.kodaclaw`
- Auth: Bearer token `test-token`

## 1. Executive Verdict

### 2026-03-20 审计后修复进度

- 本次审计识别出的两个 P0（Canvas 默认预览鉴权 / session `StreamingModel` 残留）已在代码中完成修复。
- 修复证据已通过定向 backend integration、frontend typecheck / unit / build 验证。
- 这份文档仍保留审计当时的产品现状快照；后续 live 复测应以最新构建重新确认。

结论先说：

- **平台能力层**：已经明显进入“可用产品内核”阶段，主对话、审批、收件、会话、诊断、模型控制、渠道/插件/自动化/Canvas 的产品入口都存在，很多底层能力也确实能跑通。
- **完整产品形态层**：**还没有达到** `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PRODUCT.md` 里期待的“PoorClaw 完整产品形态”。
- **最大差距不在后端 capability 是否存在，而在产品壳层是否像一个真正的个人 Agent OS**：当前更接近“可操作的控制台 / 运维工作台”，而不是“开屏即进入可持续对话、外部入口、主动任务、画布承载”的完整产品。

一句话判断：

> KodaClaw 现在已经有了 70%~80% 的产品内核与控制面，但离 PRODUCT 期待的完整产品体验，仍差一轮以会话为中心的 UI/UX 重构，以及一轮把 Plugins / Channels / Automations / Canvas 变成真正可首轮上手的工作流整合。

## 2. 本轮 live dogfood 已验证的事实

### 2.1 已直接跑通

- Gateway 根入口、健康检查、bootstrap-state 可访问
- Web 工作台可正常加载并读取 gateway 目标、健康态、workspace 根路径
- 主对话可发送消息并获得回答
- 通过主对话触发 `bash_run` 工具审批能够成功创建 `Approval` 和 `InboxItem`
- Inbox / Approval desk 可看到待审批项，并可在 UI 中完成批准
- 批准后 chat 请求继续执行，Gateway diagnostics 记录 `gateway.chat.completed`
- Sessions / Diagnostics / Models / Settings / Channels / Plugins / Automations / Canvas desks 均有独立入口并能成功加载数据或空状态

### 2.2 已明确暴露的问题 / 缺口

- Canvas 默认预览走 `/api/canvas/fs/...` 时仍然触发未授权，导致默认预览空白
- 会话在 chat `completed` 后仍残留 `breakpointState = StreamingModel`
- 首页是 dashboard-first，不是 conversation-first
- 非聊天域（Plugins / Channels / Automations / Canvas）仍缺少首轮上手动作，离“不依赖命令行即可完成主要使用流程”还有距离

## 3. Capability Matrix (vs PRODUCT)

| PRODUCT 能力 | Live 结果 | 结论 | 证据 |
| --- | --- | --- | --- |
| 主对话与会话连续性 | 对话可发送、会话可恢复、审批后可续跑 | `Partial` | chat live 交互 + `/api/sessions` |
| Inbox 与审批 | 待审批项可产生、可在 UI 批准 | `Pass` | `/api/inbox`、`/api/approvals`、`inbox-pending-approval.png` |
| Workspace 与长期记忆 | workspace 已初始化、主 session 持久存在 | `Partial` | `/api/system/bootstrap-state`、`/Users/vanzheng/.kodaclaw` |
| Model Hub | 默认模型已配置，Models / Settings desk 可读写基础配置 | `Partial` | `/api/models`、`/api/settings`、`desk-models.png` |
| Plugins | desk / API 在，但 live 实例无安装中插件，首轮安装体验不足 | `Partial` | `/api/plugins`、`desk-plugins.png` |
| Channels | connector 能力存在，但当前实例未形成账号/线程闭环 | `Partial` | `/api/channels/connectors`、`desk-channels.png` |
| Automation | desk 在、引擎在，但 live 实例没有可直接操作的自动化对象 | `Partial` | `/api/automations`、`desk-automations.png` |
| Canvas | desk 在，但默认预览空白，存在 auth 链路问题 | `Blocked` | `desk-canvas.png` + diagnostics `gateway.auth.failed` |
| Desktop Shell | 本轮未对 Electron 壳做 live 验证 | `Not Assessed` | 本次只测 web + gateway |
| 不依赖命令行完成主要流程 | 仅 chat / inbox 较接近，其他域仍偏控制台化 | `Fail` | live dogfood 总结 |
| 用户能清楚看到 Agent 做了什么 / 为何暂停 | 诊断、审批、收件基本可见，但会话状态存在残留误导 | `Partial` | `desk-sessions.png`、diagnostics timeline |

## 4. 为什么现在还不能算“完整复刻 PRODUCT”

和 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/PRODUCT.md` 对照，核心差距有四类：

### 4.1 壳层形态不对

现在的 web 更像“控制台首页”，不是“个人 Agent 操作系统”的主工作区。

PRODUCT 里最重要的一等对象和主入口是：
- 主线程
- Inbox
- 渠道线程
- 自动化
- 画布

而当前 UI 首屏优先级是：
- 品牌 hero
- summary cards
- 当前 desk 概览
- 底部才是聊天时间线和输入器

这会直接削弱主线程的产品中心性。

### 4.2 可见 ≠ 可首轮使用

Channels / Plugins / Automations / Canvas 的产品入口虽然已经存在，但它们更像“状态面板”，而不是“马上可开始工作”的真实工作流入口。

这说明平台 capability 已经到了，但 first-run UX 还没补齐。

### 4.3 控制面状态与真实 runtime 状态仍有不一致

`gateway.chat.completed` 已发生后，会话仍显示 `StreamingModel`。这类状态残留会伤害 KodaClaw 的核心承诺：

- 可审计
- 可解释
- 知道 Agent 卡在什么阶段

### 4.4 Canvas 还没有进入“可靠可用”状态

Canvas 在 PRODUCT 里不是附属 feature，而是与普通聊天应用拉开差异的核心产品面。当前默认入口空白，说明它还不能被视为“完成态能力”。

## 5. 建议的优先级

### P0 - 必须先修

1. 修复 Canvas iframe / fallback entry 的鉴权链路 `Completed in code on 2026-03-20`
2. 修复 session breakpoint state 在 chat completed 后的收尾状态 `Completed in code on 2026-03-20`

### P1 - 决定产品是否像 PoorClaw 的关键轮次

1. 进行一轮 **conversation-first** 的 UI/UX 重构
2. 让首页回到“线程 / 输入 / 上下文”三段式结构，而不是 dashboard-first
3. 给 Plugins / Channels / Automations / Canvas 增加首轮 CTA / onboarding flow

### P2 - 第二层完善

1. 把 Models / Settings / Diagnostics 等 operator 功能收进次级面板或右侧上下文区
2. 做更完整的 Desktop shell live dogfood
3. 为 Workspace / Memory / HEARTBEAT 的真实用户故事补一轮端到端操作测试

## 6. 对下一步前端重构的直接启发

这轮 live 测试给出的结论非常明确：

- **后端和控制面能力已经足够支撑一次大前端重构**
- 不需要再等新的大块 backend capability 才能做壳层升级
- 下一轮应该重点改“产品组织方式”和“首轮上手工作流”，而不是继续堆更多 card

所以接下来最值钱的动作，不是再加一个新 desk，而是把现有 desk 重新组织成更接近参考图的三栏式 Agent OS。
