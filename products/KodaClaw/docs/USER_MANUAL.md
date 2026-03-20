# KodaClaw 使用手册

Last updated: 2026-03-20

## 1. 这是什么

KodaClaw 是一个基于 `Kode.Agent SDK` 的 `local-first` Agent 产品，当前已经具备完整的本地产品形态：

- 本地 Gateway
- 共享 Web 控制台
- Electron 桌面壳
- Workspace 文件协议
- Inbox / Approval / Sessions / Diagnostics / Models / Settings / Automations / Channels / Plugins / Canvas
- Hardening 能力：secret migration、startup repair、manual-first update、diagnostic bundle、backup export / import preflight

当前最稳定的使用方式有两种：

1. 直接启动 Gateway + `kodaclaw-web`
2. 启动 `kodaclaw-desktop`，由桌面壳附着已有 Gateway 或以 `ManagedChild` 模式拉起 Gateway

如果你更偏向按角色阅读：

- 用户视角：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_GUIDE.md`
- 运维视角：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/OPS_RUNBOOK.md`
- 开发视角：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEVELOPER_GUIDE.md`

## 2. 目录与核心组件

- Gateway：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway`
- Web 控制台：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web`
- 桌面壳：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop`
- Workspace 默认根目录：`~/.kodaclaw`，也可以通过 `KODACLAW_WORKSPACE_ROOT` 覆盖
- 开发配置约定：`/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEV_CONFIG.md`

## 3. 先决条件

建议在本地具备以下环境：

- `.NET SDK 10`
- `Node.js 20+`
- `npm`
- macOS 开发环境（当前桌面壳和 hardening 验证以 macOS-first 为主）

如果你要跑 Web E2E，还需要 Playwright 依赖；仓库当前已经可直接执行 `npm run test:e2e`。

## 4. 最小启动方式

### 4.1 启动 Gateway

先准备一份本地环境变量：

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw
cp .env.example .env.local
```

Gateway 现在会默认加载当前工作目录下的：

- `.env`
- `.env.local`
- `appsettings.json`
- `appsettings.{Environment}.json`

因此如果你希望直接使用产品根目录里的配置样例，请保持从 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw` 启动 Gateway。

最小可用示例：

```bash
export KODACLAW_WORKSPACE_ROOT="$HOME/.kodaclaw"
export KODACLAW_GATEWAY_URL="http://127.0.0.1:5076"
export KODACLAW_GATEWAY_TOKEN="test-token"

dotnet run \
  --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj \
  --urls "$KODACLAW_GATEWAY_URL"
```

健康检查：

```bash
curl "$KODACLAW_GATEWAY_URL/api/system/health"
```

如果返回 `healthy`，说明 Gateway 已经起来。

当前配置优先级从低到高为：

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. `.env`
4. `.env.local`
5. 真实 shell 环境变量
6. 命令行参数

补充说明：

- `.env` / `.env.local` 改动后需要重启 Gateway
- Runtime Control 里切换 default model / endpoint 后，新聊天请求不需要重启 Gateway

Gateway 现已默认允许来自 `http/https` loopback 的 Web 控制台（例如 [http://127.0.0.1:4173](http://127.0.0.1:4173)）以及打包桌面壳 `file://` renderer 的 `Origin: null`。如果你把控制台挂到其他 origin，需要额外设置 `KODACLAW_CORS_ALLOWED_ORIGINS`。

### 4.2 启动 Web 控制台

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web
cp .env.example .env.local
```

在 `.env.local` 中配置：

```bash
VITE_KODACLAW_GATEWAY_URL=http://127.0.0.1:5076
VITE_KODACLAW_GATEWAY_TOKEN=test-token
```

然后启动：

```bash
npm install
npm run dev
```

默认访问地址：

- [http://127.0.0.1:4173](http://127.0.0.1:4173)

如果你保持默认开发端口，不需要额外处理跨域；Gateway 已经对白名单内的 loopback origin 自动返回 CORS 响应头。

前端当前默认以中文启动；如需英文，可在右上角语言切换器中切到 `English`。该选择会在浏览器本地持久化，下次打开仍会沿用。

### 4.3 启动桌面壳

如果你已经有一个正在运行的 Gateway，可用附着模式：

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop
export KODACLAW_DESKTOP_GATEWAY_URL="http://127.0.0.1:5076"
export KODACLAW_DESKTOP_GATEWAY_TOKEN="test-token"
export KODACLAW_DESKTOP_GATEWAY_LIFECYCLE_MODE="AttachOnly"
export KODACLAW_DESKTOP_WEB_DEV_SERVER_URL="http://127.0.0.1:4173"

npm install
npm run dev
```

如果你希望桌面壳在开发态自动拉起 Gateway，可改用：

```bash
export KODACLAW_DESKTOP_GATEWAY_LIFECYCLE_MODE="ManagedChild"
npm run dev
```

## 5. 首次使用

首次进入时，KodaClaw 会先走 bootstrap 流程：

1. 读取 `bootstrap-state`
2. 在 bootstrap 模式下编辑 `IDENTITY.md` / `USER.md`
3. 提交 `/api/system/bootstrap-complete`
4. 进入主工作台

完成 bootstrap 后，workspace 中会开始形成稳定协议文件与控制面数据。

## 6. 主工作台说明

当前 `kodaclaw-web` 与 `kodaclaw-desktop` 共享同一套 desk：

- 默认 shell 文案为中文
- 右上角语言切换器可在中文 / 英文之间切换
- 切换语言不会刷新页面，也不会中断当前工作台状态

### 6.1 Chat Lane

- 用于 bootstrap 与主会话聊天
- 通过 `/api/chat/stream` 接收 SSE 事件
- 支持 reopened / resumed main session

### 6.2 Inbox / Approval

- 查看 Inbox 项
- 执行审批通过 / 拒绝
- 观察审批与 Inbox 的闭环反馈

### 6.3 Sessions / Diagnostics

- 查看 session 列表与详情
- 浏览 diagnostics timeline
- 导出 diagnostic bundle

### 6.4 Models / Settings

- 管理模型端点
- 设置默认模型
- Runtime Control 中切换 default endpoint 后，新 chat / new session 会即时使用新配置
- 查看 `Sandbox & Risk Briefing`
- 使用 `Update Watch` 做 manual-first 更新检查

### 6.5 Automations

- 查看自动化定义
- 启停自动化
- 查看 run 历史与状态

### 6.6 Channels

- 管理 Telegram / Generic Webhook 账号
- 查看 thread binding 与线程详情
- 观察 connector / delivery 相关状态

### 6.7 Plugins

- 查看插件清单
- 查看 trust evidence、signer、verification state
- 启停插件并查看日志

### 6.8 Canvas

- 浏览 canvas artifact
- 预览当前 artifact 或空态 fallback

## 7. 关键运维与 hardening 能力

### 7.1 Secret migration report

用于检查 Gateway token、模型端点、Channel credential、Plugin runtime secret 是否已经迁移到 `SecretRef`。

```bash
curl \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "$KODACLAW_GATEWAY_URL/api/system/secret-migration-report"
```

产物会写到：

- `config/secret-migration-report.json`

### 7.2 Startup repair report

Gateway 启动时会自动做保守 repair inspection，并暴露最新报告：

```bash
curl \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "$KODACLAW_GATEWAY_URL/api/system/startup-repair-report"
```

产物会写到：

- `config/startup-repair-report.json`

### 7.3 Update Watch

KodaClaw 当前坚持 manual-first update：

- 只做版本检查、release notes、download handoff
- 不做后台下载
- 不做 silent install

推荐先准备本地 fixture manifest：

```bash
export KODACLAW_UPDATE_MANIFEST_PATH=/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/Updates/update-manifest.fixture.json
export KODACLAW_UPDATE_RELEASE_CHANNEL=Stable
```

然后手动触发：

```bash
curl \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"desktopCurrentVersion":"0.1.0","desktopReleaseChannel":"Stable"}' \
  "$KODACLAW_GATEWAY_URL/api/system/update-check"
```

注意：

- manifest 中的 `downloadUrl` / `releaseNotesUrl` 只允许绝对 `http/https`
- 非法 scheme 会被 Gateway 丢弃，并写入 `OperatorNotes`
- Web / Desktop 端都不会打开非法外链

### 7.4 Diagnostic bundle export

导出默认脱敏的 support bundle：

```bash
curl \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"sessionId":"main-001","timelineLimit":120}' \
  "$KODACLAW_GATEWAY_URL/api/diagnostics/bundle-export"
```

默认产物：

- `cache/diagnostics/kodaclaw-diagnostic-bundle-<timestamp>.zip`

bundle 特性：

- 包含 `manifest.json` 与 `redaction-summary.json`
- 包含 settings snapshot、diagnostics recent/timeline、session `meta.json`、repair/update evidence、log summary
- 排除 raw secrets、`messages.json`、`tool-calls.json`、raw log bodies
- JSON / quoted secret 片段也会被 redaction

如果显式传 `archivePath`：

- 必须解析到 workspace 内
- workspace 外路径会返回 `validation.archive_path_invalid`

### 7.5 Backup export / import preflight / import

导出 backup：

```bash
curl \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"archivePath":"config/backups/manual-smoke.zip"}' \
  "$KODACLAW_GATEWAY_URL/api/system/backup-export"
```

预检 import：

```bash
curl \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"archivePath":"config/backups/manual-smoke.zip"}' \
  "$KODACLAW_GATEWAY_URL/api/system/backup-import/preflight"
```

正式 import：

```bash
curl \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"archivePath":"config/backups/manual-smoke.zip"}' \
  "$KODACLAW_GATEWAY_URL/api/system/backup-import"
```

注意事项：

- export 的自定义 `archivePath` 必须落在当前 workspace 内
- import 的相对 `archivePath` 会解析到目标 workspace root
- preflight 会先检查 manifest、entry 路径、重复项、大小写碰撞与 checksum，再进入解压/恢复
- 缺失 manifest、危险路径、大小写碰撞或其他 malformed zip 会返回 `backup.import.invalid_archive`

### 7.6 Runtime / Chat 配置排障

如果你在 `Chat Lane` 看到：

- `KodaClaw chat is not configured. Set KODACLAW_DEFAULT_MODEL and one provider API key.`

请按这个顺序检查：

1. Gateway 是否从产品根目录启动，或者当前 `cwd` 下是否真的存在目标 `.env` / `appsettings*.json`
2. `Models / Settings` 中是否已有 `Enabled = true` 的 default endpoint
3. endpoint 上的 `ApiKeyEnvironmentVariable` 是否与 `.env` / `.env.local` / shell env 中的变量名一致
4. 如果刚改的是 `.env` / `.env.local` 文件，请重启 Gateway
5. 如果刚改的是 Runtime Control 中的 default endpoint，本轮修复后不需要重启；直接发起新 chat 即可验证

## 8. 推荐环境变量

最常用的一组变量如下：

- `KODACLAW_WORKSPACE_ROOT`
- `KODACLAW_GATEWAY_URL`
- `KODACLAW_GATEWAY_TOKEN_SECRET_REF`
- `KODACLAW_GATEWAY_TOKEN`
- `KODACLAW_UPDATE_MANIFEST_PATH`
- `KODACLAW_CORS_ALLOWED_ORIGINS`
- `KODACLAW_UPDATE_RELEASE_CHANNEL`
- `KODACLAW_DESKTOP_RELEASE_CHANNEL`
- `OPENAI_API_KEY_SECRET_REF`
- `OPENAI_API_KEY`
- `ANTHROPIC_API_KEY_SECRET_REF`
- `ANTHROPIC_API_KEY`

桌面壳常用变量：

- `KODACLAW_DESKTOP_WEB_DEV_SERVER_URL`
- `KODACLAW_DESKTOP_GATEWAY_URL`
- `KODACLAW_DESKTOP_GATEWAY_TOKEN`
- `KODACLAW_DESKTOP_GATEWAY_LIFECYCLE_MODE`
- `KODACLAW_DESKTOP_INITIAL_TARGET_JSON`

完整说明请看：

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEV_CONFIG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/RUNTIME_CONFIG_ACTIVATION_PLAN.md`

## 9. 常见验证命令

### 后端

```bash
dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1
```

### Web

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web
npm run build
npm run test
npm run test:e2e
```

### Desktop

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop
npm run test
npm run smoke:wave3
npm run smoke:managed-gateway
npm run package:smoke
```

## 10. 常见问题

### Q1：为什么 Update Watch 不会自动升级？

因为当前产品策略是 manual-first update。KodaClaw 只提供版本检查、release notes 与下载交接，不做 silent updater。

### Q2：为什么 backup import 之前还要 preflight？

因为 preflight 是 hardening 的一部分。它会在真正解压之前检查 manifest、checksum、workspace 恢复风险与 secret/device repair checklist，避免不透明恢复。

### Q3：为什么 diagnostic bundle 里没有完整聊天正文？

因为 bundle 默认面向支持与运维，必须优先脱敏，所以只保留 `meta.json`、timeline、settings、repair/update evidence 与 log summary。

### Q4：桌面壳是否已经支持托盘、通知和 deep-link？

支持。当前桌面壳已交付 tray / menu / shortcut、通知轮询、`kodaclaw://...` deep-link 与 launch target bridge。

## 11. 推荐阅读顺序

1. `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/README.md`
2. `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEV_CONFIG.md`
3. `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ARCHITECTURE.md`
4. `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_ACCEPTANCE_PACK.md`

如果你是开发者，建议再看：

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ENGINEERING_PLAYBOOK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`
