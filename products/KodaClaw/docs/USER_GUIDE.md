# KodaClaw 用户指南

Last updated: 2026-03-20

## 1. 适合谁看

这份文档面向普通使用者，重点是：

- 怎么启动 KodaClaw
- 首次进入后应该做什么
- 各个 desk 是干什么的
- 常见操作从哪里进入

如果你要看运维操作，请看 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/OPS_RUNBOOK.md`。
如果你要参与开发，请看 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEVELOPER_GUIDE.md`。

## 2. 启动方式

最常见的两种用法：

1. 启动 Gateway + `kodaclaw-web`
2. 启动 `kodaclaw-desktop`

如果你使用 Gateway + Web 的方式，推荐先进入产品根目录再启动 Gateway；Gateway 会默认读取当前目录的 `.env` / `.env.local` / `appsettings*.json`。

快速入口请直接参考：

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_MANUAL.md`

## 3. 首次进入

第一次进入时会先走 bootstrap：

1. 读取 bootstrap state
2. 编辑 `IDENTITY.md` 与 `USER.md`
3. 提交 onboarding
4. 进入主工作台

完成后，你会长期使用同一个本地 workspace。

补充：

- Web / Desktop 控制台当前默认显示中文
- 右上角语言切换器可以切到英文
- 语言选择会被记住，下次打开会沿用上次选择

## 4. 主工作台怎么理解

当前主工作台包含 8 个主要 desk：

- `Chat Lane`：和 Koda 的主聊天入口
- `Inbox / Approval`：需要确认的事项、审批与反馈
- `Sessions / Diagnostics`：查看会话、导出诊断包
- `Models / Settings`：模型、设置、风险说明、更新检查
- `Automations`：自动化任务与运行情况
- `Channels`：外部渠道账号与线程
- `Plugins`：插件状态、信任信息与日志
- `Canvas`：生成内容与可视化产物

## 5. 你最常用的功能

### Chat

- 在 `Chat Lane` 发起主会话
- 会话支持恢复
- bootstrap 完成后，主聊天会成为你的主要入口
- 在 `Models / Settings` 中切换 default model 后，新聊天请求会直接使用新配置，不需要重启 Gateway

### Inbox / Approval

- 如果 Koda 需要你确认某个动作，去 `Inbox / Approval`
- 这里可以 approve / reject
- 处理后会同步反馈回控制面

### Update Watch

- 进入 `Models / Settings`
- 手动触发更新检查
- 查看 release notes 与 download handoff

注意：当前产品不会 silent install。

### Diagnostic Bundle

- 进入 `Sessions / Diagnostics`
- 选择 session
- 导出 diagnostic bundle

这个 bundle 默认已经过脱敏，适合支持与排障。

## 6. 你需要知道的限制

- 更新流程是 manual-first，不会自动静默升级
- diagnostic bundle 不会包含完整聊天正文
- 一些高风险外部动作可能需要审批
- 本地 sandbox 边界是 best-effort，不应被理解为强隔离

## 7. 建议阅读顺序

1. `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_MANUAL.md`
2. `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/OPS_RUNBOOK.md`
