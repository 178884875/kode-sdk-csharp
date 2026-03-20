# KodaClaw 运维手册

Last updated: 2026-03-19

## 1. 适合谁看

这份文档面向运维、验收、支持和排障场景，重点是：

- 服务健康检查
- hardening 操作
- backup / restore / diagnostic bundle
- update / startup repair / migration report

## 2. 启动与健康检查

### 启动 Gateway

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw
cp .env.example .env.local
```

Gateway 现在会默认加载当前工作目录下的 `.env` / `.env.local` / `appsettings.json` / `appsettings.{Environment}.json`，因此运维或验收时建议始终从产品根目录启动。

```bash
export KODACLAW_WORKSPACE_ROOT="$HOME/.kodaclaw"
export KODACLAW_GATEWAY_URL="http://127.0.0.1:5076"
export KODACLAW_GATEWAY_TOKEN="test-token"

dotnet run \
  --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj \
  --urls "$KODACLAW_GATEWAY_URL"
```

配置优先级从低到高：

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. `.env`
4. `.env.local`
5. 真实 shell 环境变量
6. 命令行参数

### 健康检查

```bash
curl "$KODACLAW_GATEWAY_URL/api/system/health"
```

期望：

- 返回 `healthy`

### Browser / Desktop CORS smoke

```bash
curl -i -X OPTIONS \
  -H "Origin: http://127.0.0.1:4173" \
  -H "Access-Control-Request-Method: GET" \
  -H "Access-Control-Request-Headers: authorization" \
  "$KODACLAW_GATEWAY_URL/api/system/bootstrap-state"
```

期望：

- `Access-Control-Allow-Origin` 回显 loopback origin
- `Access-Control-Allow-Headers` 包含 `authorization`
- 打包桌面壳可把 `Origin` 改成 `null` 复核 Electron `file://` renderer
- 额外非 loopback origin 需要通过 `KODACLAW_CORS_ALLOWED_ORIGINS` 或 `Gateway:CorsAllowedOrigins` 白名单显式放行

## 3. 核心运维操作

### Secret migration report

```bash
curl \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "$KODACLAW_GATEWAY_URL/api/system/secret-migration-report"
```

作用：

- 检查 Gateway token / model endpoint / channel / plugin secret 迁移状态
- 当前实现已分页扫描全部 channel / plugin，不会被默认 50 条 limit 静默截断

产物：

- `config/secret-migration-report.json`

### Startup repair report

```bash
curl \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "$KODACLAW_GATEWAY_URL/api/system/startup-repair-report"
```

作用：

- 查看 Gateway 启动时的 repair checklist
- 用于审计 stale approval / automation / plugin runtime residue 的保守修复结果

### Update Watch

```bash
export KODACLAW_UPDATE_MANIFEST_PATH=/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/Updates/update-manifest.fixture.json
export KODACLAW_UPDATE_RELEASE_CHANNEL=Stable

curl \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"desktopCurrentVersion":"0.1.0","desktopReleaseChannel":"Stable"}' \
  "$KODACLAW_GATEWAY_URL/api/system/update-check"
```

注意：

- 只允许绝对 `http/https` 外链
- 非法 URL 会被忽略并写入 `OperatorNotes`
- 这是 manual-first update，不做 silent install

### Runtime Control 动态激活 smoke

该场景用于确认“设 default model 后无需重启 Gateway”这条主链路：

1. 在 `Models / Settings` 中创建 model endpoint，`ApiKeyEnvironmentVariable` 指向当前目录 `.env` / `.env.local` 中已存在的变量名
2. 把该 endpoint 设为 default
3. 直接重新发起 chat 请求

```bash
curl -sN \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"message":"runtime activation smoke"}' \
  "$KODACLAW_GATEWAY_URL/api/chat/stream"
```

期望：

- 未配置前返回 SSE `error`
- Runtime Control 设置 default 后直接返回 `text_chunk` / `done`
- 不需要重启 Gateway

### Diagnostic bundle export

```bash
curl \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"sessionId":"main-001","timelineLimit":120}' \
  "$KODACLAW_GATEWAY_URL/api/diagnostics/bundle-export"
```

当前保证：

- 默认脱敏
- JSON / quoted secret 片段也会被 redaction
- raw secrets、`messages.json`、`tool-calls.json`、原始日志正文不会进入 bundle

### Backup export / preflight / import

导出：

```bash
curl \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"archivePath":"config/backups/manual-smoke.zip"}' \
  "$KODACLAW_GATEWAY_URL/api/system/backup-export"
```

预检：

```bash
curl \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"archivePath":"config/backups/manual-smoke.zip"}' \
  "$KODACLAW_GATEWAY_URL/api/system/backup-import/preflight"
```

恢复：

```bash
curl \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"archivePath":"config/backups/manual-smoke.zip"}' \
  "$KODACLAW_GATEWAY_URL/api/system/backup-import"
```

当前保证：

- export 的 `archivePath` 必须位于 workspace 内
- import 的相对 `archivePath` 会解析到目标 workspace root
- preflight 会在解压前检查 manifest、危险路径、重复项、大小写碰撞与 checksum

## 4. 常见错误码

- `validation.archive_path_invalid`
  - `archivePath` 解析到 workspace 外
- `backup.import.invalid_archive`
  - zip 缺失 manifest、存在危险 entry、大小写碰撞、或其他不安全结构
- `backup.import.preflight_failed`
  - preflight 通过前置检查后，仍存在阻塞级 repair item

## 5. 验收命令

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

## 6. 推荐联读

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEV_CONFIG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/RUNTIME_CONFIG_ACTIVATION_PLAN.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_ACCEPTANCE_PACK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_MANUAL.md`
