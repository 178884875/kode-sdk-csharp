# KodaClaw Workspace 协议

## 1. 设计目标

`~/.kodaclaw` 不是普通配置目录，而是 Koda 的本地生存空间。它需要同时承载：

- 人格
- 用户画像
- 长期记忆
- 日常记忆
- 任务与知识
- 自动化规则
- Canvas 资产
- Session 恢复所需的状态

核心原则：

- 人格、边界、记忆尽量文件化
- Secrets 不写入 Workspace
- 主会话、渠道会话、自动化会话共享同一 Workspace，但加载规则不同

## 2. 顶层目录结构

```text
~/.kodaclaw/
  config/
    app.json
    gateway.json
    models.json
    plugins.json

  identity/
    device.json
    profile.json

  workspace/
    AGENTS.md
    IDENTITY.md
    SOUL.md
    USER.md
    MEMORY.md
    HEARTBEAT.md
    BOOTSTRAP.md
    TOOLS.md
    mcp.json

    memory/
      YYYY-MM-DD.md
      facts/
      conversations/

    knowledge/
    tasks/
    inbox/
    canvas/
      index.html
      state.json
    channels/
    plugins/

  sessions/
    <sessionId>/
      meta.json
      runtime/
      events/
      sandbox/

  logs/
  cache/
```

## 3. 文件语义

| 文件 | 作用 | 是否用户可编辑 | 是否默认加载 |
| --- | --- | --- | --- |
| `AGENTS.md` | Workspace 行为约定与安全边界 | 是 | 是 |
| `IDENTITY.md` | Koda 的名字、身份、风格、签名 | 是 | 是 |
| `SOUL.md` | 更抽象的人格与长期行为准则 | 是 | 是 |
| `USER.md` | 用户画像、偏好、边界 | 是 | 是 |
| `MEMORY.md` | 长期记忆、稳定事实、关系与偏好 | 是 | 仅主会话默认加载 |
| `HEARTBEAT.md` | 后台主动检查任务与提醒规则 | 是 | 仅自动化上下文加载 |
| `BOOTSTRAP.md` | 首次启动引导说明 | 首次阶段可编辑 | 首次启动加载 |
| `TOOLS.md` | 用户环境与工具约定备忘 | 是 | 按需 |
| `mcp.json` | 本地 MCP 服务器声明 | 是 | 启动时读取 |

## 4. Session 加载规则

不同 session 类型应加载不同上下文，避免隐私污染和上下文膨胀。

### 4.1 `main`

默认加载：

- `AGENTS.md`
- `IDENTITY.md`
- `SOUL.md`
- `USER.md`
- `MEMORY.md`
- `memory/<today>.md`
- `memory/<yesterday>.md`
- 与当前 thread 关联的 tasks / knowledge 摘要

### 4.2 `channel_dm`

默认加载：

- `AGENTS.md`
- `IDENTITY.md`
- `SOUL.md`
- `USER.md` 的公开子集
- 最近相关日记摘要
- 与当前渠道绑定的 channel profile

默认不加载：

- `MEMORY.md` 全量内容
- 主会话专属长期记忆

### 4.3 `channel_group`

默认加载：

- `AGENTS.md`
- `IDENTITY.md`
- 当前群组绑定规则
- 最小必要渠道配置

默认不加载：

- `USER.md` 私人细节
- `MEMORY.md`
- 无关的长期记忆

### 4.4 `automation`

默认加载：

- `AGENTS.md`
- `IDENTITY.md`
- `SOUL.md`
- `USER.md` 的任务相关子集
- `HEARTBEAT.md`
- 该 automation 绑定的任务文件或知识文件

### 4.5 `plugin`

插件上下文不直接读取整个 Workspace，而是通过 Gateway 提供最小授权读取。

## 5. 记忆策略

### 5.1 长期记忆

放在：

- `MEMORY.md`
- `memory/facts/*.jsonl`

适合长期记忆的内容：

- 用户稳定偏好
- 重要关系与项目背景
- 长期决策
- 稳定工作方式

### 5.2 日常记忆

放在：

- `memory/YYYY-MM-DD.md`

适合日常记忆的内容：

- 当天发生的关键对话
- 临时进展
- 待后续归档到长期记忆的上下文

### 5.3 知识

放在：

- `knowledge/`

适合：

- 项目文档
- 用户自己维护的资料
- 参考材料
- 工作规范

### 5.4 任务

放在：

- `tasks/`

适合：

- 正在推进的任务
- 自动化相关任务文件
- 需要 Inbox 回流的行动项

## 6. 首次启动协议

第一次启动时，KodaClaw 需要：

1. 创建目录结构
2. 生成 `device.json`
3. 写入 `BOOTSTRAP.md`
4. 初始化默认 `AGENTS.md` / `IDENTITY.md` / `SOUL.md`
5. 打开 bootstrap 对话
6. 把 bootstrap 结果写回 `IDENTITY.md` 与 `USER.md`
7. 在首次完成 onboarding 后，将 `BOOTSTRAP.md` 归档或删除

## 7. `AGENTS.md` 的角色

`AGENTS.md` 是整个 Workspace 的行为规则入口，建议包含：

- 启动时应该读哪些文件
- 什么场景下要写记忆
- 对外动作必须先审批
- 群聊和外部渠道的安全边界
- 文件删除、系统操作等高风险规则

## 8. `HEARTBEAT.md` 的角色

`HEARTBEAT.md` 用来描述“在后台定期关心什么”。它不是机器可执行配置文件本身，而是高层任务声明。

例如：

- 每天 9:00 汇总某类信息
- 每 2 小时巡检某个 workspace 目录变动
- 每天晚上把未关闭任务整理到 Inbox

产品层应将其解析为 automation definitions，但保留 Markdown 作为用户可编辑源。

## 9. `mcp.json` 的角色

`mcp.json` 负责声明用户级 MCP 服务：

- 本地 stdio MCP
- HTTP MCP
- 插件带来的 MCP endpoint

Gateway 在启动或重载时读取它，并将可用工具纳入 PluginHost / Runtime。

## 10. Canvas 目录

`workspace/canvas/` 既是用户资产目录，也是插件和 Agent 的展示输出目录。

建议约定：

- `index.html`：当前默认画布入口
- `state.json`：当前画布状态
- `artifacts/`：按任务或 thread 生成的子目录

## 11. Secrets 规则

以下内容不允许放入 Workspace：

- 模型 API Key
- bot token
- webhook secret
- 邮箱密码
- 插件私钥

它们必须进入 OS Keychain 或平台安全存储。

## 12. 版本与迁移

建议在 `config/app.json` 中维护 `workspaceVersion`，用于未来：

- 升级目录结构
- 迁移记忆存储格式
- 迁移 Canvas 与 plugin 数据布局

## 13. 结论

KodaClaw 的连续性不是存在数据库里的一堆黑盒字段，而是尽可能以 Workspace 协议存在于文件系统中；数据库与 store 只负责运行时状态和控制面状态。
