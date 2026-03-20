# KodaClaw 本地开发配置约定

Last updated: 2026-03-19

本文件定义 KodaClaw 当前实现阶段的本地开发配置约定，供 Gateway、Runtime、Web/Desktop 壳和手工 smoke 测试共用。

## 1. Gateway 当前目录配置引导

KodaClaw Gateway 现在会在启动时自动引导当前工作目录下的本地配置：

1. 先把当前目录的 `.env` 加载进进程环境变量
2. 再把当前目录的 `.env.local` 加载进进程环境变量
3. 然后把当前目录的 `appsettings.json` 与 `appsettings.{Environment}.json` 插入到 .NET `IConfiguration` 链路里

重要说明：

- “当前目录”指的是你执行 `dotnet run --project ...` 时所在的 shell `cwd`，不是 `.csproj` 所在目录本身。
- 如果你希望直接使用 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw` 下的 `.env` / `appsettings*.json`，请先 `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw` 再启动 Gateway。
- `.env.local` 会覆盖同目录 `.env` 中同名键。
- shell 中已经存在的真实环境变量优先级更高，不会被 `.env` 覆盖。
- 命令行参数优先级最高，仍然覆盖 `.env` / `.env.local` / `appsettings*.json`。
- `.env` / `.env.local` 是启动时一次性注入到进程环境；修改这两个文件后需要重启 Gateway。
- `appsettings*.json` 以 `reloadOnChange` 加入配置链，但为了获得最稳定的一致性，涉及基础设施配置时仍建议重启 Gateway；唯一已经明确补齐并验证“无需重启”的链路，是 Runtime Control 中切换 default model / model endpoint 之后的新 chat/session 请求。

当前实际优先级从低到高如下：

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. `.env`
4. `.env.local`
5. 真实 shell 环境变量
6. 命令行参数

## 2. 推荐的本地文件布局

推荐把配置分成三层：

- `appsettings.json`：产品内共享的结构化默认值，已纳入仓库
- `appsettings.Development.json`：开发环境结构化样例，已纳入仓库
- `.env` / `.env.local`：本地 token、provider key、开发覆盖值；通常 `.env` 放通用默认值，`.env.local` 放个人机器秘密信息

推荐启动方式：

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw
cp .env.example .env.local

dotnet run \
  --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj \
  --urls http://127.0.0.1:5076
```

## 3. 支持的配置键

### 3.1 Gateway / Runtime / Update 核心键

下表列出当前这轮修复后推荐直接使用的配置键。`JSON Key` 既可以放进 `appsettings*.json`，也可以通过 .NET 环境变量映射写成 `Section__Key` 形式。

| 目的 | Env Key | JSON Key | 说明 |
| --- | --- | --- | --- |
| Workspace 根目录 | `KODACLAW_WORKSPACE_ROOT` | `Workspace:RootPath` | 为空时回退到 `~/.kodaclaw` |
| Gateway Bearer token | `KODACLAW_GATEWAY_TOKEN` | `Gateway:Token` | bootstrap / 本地测试常用 |
| Gateway token secret ref | `KODACLAW_GATEWAY_TOKEN_SECRET_REF` | `Gateway:TokenSecretRef` | 优先于原始 token 解析 |
| 额外 CORS 白名单 | `KODACLAW_CORS_ALLOWED_ORIGINS` | `Gateway:CorsAllowedOrigins` | 逗号/分号/换行或 JSON array |
| 启动修复开关 | `KODACLAW_STARTUP_REPAIR_ENABLED` | `Gateway:StartupRepairEnabled` | 设为 `false` 可禁用 |
| Runtime 默认模型 | `KODACLAW_DEFAULT_MODEL` | `Runtime:DefaultModel` | 也可由 Model Registry 的 default endpoint 动态提供 |
| OpenAI API key | `OPENAI_API_KEY` | `Runtime:OpenAIApiKey` | direct value fallback |
| OpenAI API key secret ref | `OPENAI_API_KEY_SECRET_REF` | `Runtime:OpenAIApiKeySecretRef` | 优先于 direct value |
| OpenAI Base URL | `Runtime__OpenAIBaseUrl` | `Runtime:OpenAIBaseUrl` | 兼容 proxy / OpenAI-compatible |
| Anthropic API key | `ANTHROPIC_API_KEY` | `Runtime:AnthropicApiKey` | direct value fallback |
| Anthropic API key secret ref | `ANTHROPIC_API_KEY_SECRET_REF` | `Runtime:AnthropicApiKeySecretRef` | 优先于 direct value |
| Anthropic Base URL | `Runtime__AnthropicBaseUrl` | `Runtime:AnthropicBaseUrl` | 兼容 proxy / compatible endpoint |
| update manifest 路径 | `KODACLAW_UPDATE_MANIFEST_PATH` | `Update:ManifestPath` | manual-first update fixture |
| release channel | `KODACLAW_UPDATE_RELEASE_CHANNEL` | `Update:ReleaseChannel` | `Stable` / `Preview` / `Nightly` / `Custom` |
| Gateway 当前版本 | `KODACLAW_GATEWAY_CURRENT_VERSION` | `Update:GatewayCurrentVersion` | 未配置时回退到程序集版本 |

补充说明：

- Web / Desktop 侧还会使用 `KODACLAW_GATEWAY_URL`、`KODACLAW_DESKTOP_RELEASE_CHANNEL` 等客户端变量；这些不是 Gateway 进程内部配置键，但仍保留在 `.env.example` 中方便本地联调。
- `Gateway:Token` / `Runtime:*` 这类 JSON 键，如果要写进 `.env`，请使用 .NET 双下划线写法，例如 `Gateway__StartupRepairEnabled=true`、`Runtime__OpenAIBaseUrl=https://proxy.example.com/v1`。
- `KODACLAW_CORS_ALLOWED_ORIGINS` 与 `Gateway:CorsAllowedOrigins` 都只是在默认 loopback / `Origin: null` 之外加白名单；不会关闭默认 loopback 放行。

### 3.2 Runtime / Model Registry 选路规则

当前 runtime 选路规则如下：

- 如果直接配置了 `KODACLAW_DEFAULT_MODEL` / `Runtime:DefaultModel`，优先使用 direct runtime config。
- 如果 direct runtime config 没有 default model，Gateway 会读取工作区里的 Model Registry default endpoint。
- Model Registry default endpoint 现在既支持 `ApiKeyEnvironmentVariable`，也支持 `ApiKeySecretRef`。
- 当 Runtime Control 里把某个 model endpoint 设为 default 后，新发起的 chat/new session 会即时走新的 default model，不需要重启 Gateway。
- 如果 provider key 只写在当前目录 `.env` / `.env.local` 中，也可以被 default endpoint 的 `ApiKeyEnvironmentVariable` 正常解析，因为 Gateway 启动时已先把这两个文件灌进进程环境。

## 4. 示例文件

### 4.1 `.env`

```bash
KODACLAW_WORKSPACE_ROOT=$HOME/.kodaclaw
KODACLAW_GATEWAY_URL=http://127.0.0.1:5076
KODACLAW_GATEWAY_TOKEN=replace-me
KODACLAW_DEFAULT_MODEL=
OPENAI_API_KEY=
ANTHROPIC_API_KEY=
```

### 4.2 `.env.local`

```bash
OPENAI_API_KEY=sk-local-openai-key
KODACLAW_GATEWAY_TOKEN=dev-token-only-on-my-machine
KODACLAW_CORS_ALLOWED_ORIGINS=http://127.0.0.1:4173,http://localhost:4173
```

### 4.3 `appsettings.Development.json`

仓库当前已提供 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/appsettings.Development.json`，可按下面这种方式使用：

```json
{
  "Workspace": {
    "RootPath": ""
  },
  "Gateway": {
    "Token": "replace-me",
    "TokenSecretRef": "",
    "StartupRepairEnabled": true,
    "CorsAllowedOrigins": [
      "http://127.0.0.1:4173"
    ]
  },
  "Runtime": {
    "DefaultModel": "",
    "OpenAIApiKey": "",
    "OpenAIApiKeySecretRef": "",
    "OpenAIBaseUrl": "https://api.openai.com/v1",
    "AnthropicApiKey": "",
    "AnthropicApiKeySecretRef": "",
    "AnthropicBaseUrl": ""
  },
  "Update": {
    "ManifestPath": "",
    "ReleaseChannel": "Stable",
    "GatewayCurrentVersion": ""
  }
}
```

## 5. 典型工作流

### 5.1 Runtime Control + `ApiKeyEnvironmentVariable` 无需重启激活

这是用户这轮反馈的核心场景，当前已补齐：

1. 在产品根目录准备 `.env` 或 `.env.local`，例如写入 `OPENAI_API_KEY=...`
2. 从产品根目录启动 Gateway
3. 打开 `Models / Settings`，新建 model endpoint，`ApiKeyEnvironmentVariable` 填 `OPENAI_API_KEY`
4. 在 Runtime Control 中把这个 endpoint 设为 default
5. 直接回到 `Chat Lane` 发起新对话

预期结果：

- 不需要重启 Gateway
- 新 chat 请求会直接走刚设定的 default endpoint
- 如果 default model/provider key 仍然缺失，会继续返回 `KodaClaw chat is not configured...`，而不是 silent fallback

### 5.2 纯 `appsettings*.json` 配置 Runtime

如果你不想在 `.env` 中放 provider base URL，可把结构化配置写进 `appsettings.Development.json`：

```json
{
  "Runtime": {
    "DefaultModel": "gpt-4o-mini",
    "OpenAIBaseUrl": "https://proxy.runtime.test/v1"
  }
}
```

再把真正的 key 放到 `.env.local`：

```bash
OPENAI_API_KEY=sk-local-openai-key
```

这种模式适合：

- Base URL 需要结构化维护
- key 仍然希望留在本机 `.env.local` / shell env 中
- 团队共享一份受控 `appsettings.Development.json` 模板

### 5.3 SecretRef 优先级

当前 secret 解析优先级：

- Gateway token：`KODACLAW_GATEWAY_TOKEN_SECRET_REF` / `Gateway:TokenSecretRef` 优先于原始 token
- Runtime provider：`OPENAI_API_KEY_SECRET_REF` / `Runtime:OpenAIApiKeySecretRef`、`ANTHROPIC_API_KEY_SECRET_REF` / `Runtime:AnthropicApiKeySecretRef` 优先于 direct key
- Model Registry default endpoint：`ApiKeySecretRef` 优先于 `ApiKeyEnvironmentVariable`

## 6. 故障排查

### 6.1 Chat 仍提示 “KodaClaw chat is not configured”

按下面顺序检查：

1. 确认 Gateway 是从产品根目录启动，或者确认当前 `cwd` 下确实存在目标 `.env` / `appsettings*.json`
2. 确认 Runtime Control 里已有 `Enabled = true` 的 default endpoint
3. 确认 `ApiKeyEnvironmentVariable` 指向的变量名真的存在于 shell env、`.env` 或 `.env.local`
4. 如果你刚改的是 `.env` / `.env.local` 内容，请重启 Gateway
5. 如果你刚改的是 Runtime Control 中的 default endpoint，本轮修复后不需要重启；直接发起新 chat 即可

### 6.2 CORS 被拦截

Gateway 默认允许：

- `http://localhost:*`
- `http://127.0.0.1:*`
- `http://[::1]:*`
- `https://localhost:*`
- `https://127.0.0.1:*`
- 打包桌面壳的 `Origin: null`

其他域名或端口需要显式加入：

- `KODACLAW_CORS_ALLOWED_ORIGINS`
- 或 `Gateway:CorsAllowedOrigins`

### 6.3 Update / Repair / Diagnostic bundle 路径错误

以下导出路径都必须位于 workspace 内：

- `BackupExportRequest.ArchivePath`
- `BackupImportRequest.ArchivePath`
- `DiagnosticBundleExportRequest.ArchivePath`

相对路径会解析到当前 workspace root；越界路径会返回 `validation.archive_path_invalid`。

## 7. 手工 smoke 建议

### 7.1 Gateway health

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw

dotnet run --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj --urls "$KODACLAW_GATEWAY_URL"
curl "$KODACLAW_GATEWAY_URL/api/system/health"
```

### 7.2 Browser / Desktop CORS preflight

```bash
curl -i -X OPTIONS \
  -H "Origin: http://127.0.0.1:4173" \
  -H "Access-Control-Request-Method: GET" \
  -H "Access-Control-Request-Headers: authorization" \
  "$KODACLAW_GATEWAY_URL/api/system/bootstrap-state"
```

- 返回头里应包含 `Access-Control-Allow-Origin: http://127.0.0.1:4173`
- 打包桌面壳可把 `Origin` 改成 `null` 验证
- 使用非 loopback 域名承载 Web 控制台时，请先设置 `KODACLAW_CORS_ALLOWED_ORIGINS` 或 `Gateway:CorsAllowedOrigins`

### 7.3 Runtime Control 动态激活 smoke

```bash
curl \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "$KODACLAW_GATEWAY_URL/api/models"
```

在 UI 中创建并设为 default 后，再直接发起：

```bash
curl -sN \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"message":"hello after runtime control update"}' \
  "$KODACLAW_GATEWAY_URL/api/chat/stream"
```

预期：

- 未配置前返回 SSE `error` 事件
- 配置 default model 后直接返回 `text_chunk` / `done`
- 全程无需重启 Gateway

### 7.4 Secret migration / startup repair / update / diagnostic bundle

```bash
curl \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "$KODACLAW_GATEWAY_URL/api/system/secret-migration-report"
```

```bash
curl \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  "$KODACLAW_GATEWAY_URL/api/system/startup-repair-report"
```

```bash
export KODACLAW_UPDATE_MANIFEST_PATH=/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/Fixtures/Updates/update-manifest.fixture.json
curl \
  -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"desktopCurrentVersion":"0.1.0","desktopReleaseChannel":"Stable"}' \
  "$KODACLAW_GATEWAY_URL/api/system/update-check"
```

```bash
curl -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"sessionId":"main-001","timelineLimit":120}' \
  "$KODACLAW_GATEWAY_URL/api/diagnostics/bundle-export"
```

### 7.5 Backup export / import preflight

```bash
curl -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"archivePath":"config/backups/manual-smoke.zip"}' \
  "$KODACLAW_GATEWAY_URL/api/system/backup-export"
```

```bash
curl -X POST \
  -H "Authorization: Bearer $KODACLAW_GATEWAY_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"archivePath":"config/backups/manual-smoke.zip"}' \
  "$KODACLAW_GATEWAY_URL/api/system/backup-import/preflight"
```

## 8. 相关 hardening 语义

当前与配置相关的 hardening 语义如下：

- `OPENAI_API_KEY_SECRET_REF` / `ANTHROPIC_API_KEY_SECRET_REF` 会优先于 raw provider key 解析；未命中时才回退到 `OPENAI_API_KEY` / `ANTHROPIC_API_KEY`
- Model Registry default endpoint 已支持 `ApiKeySecretRef`，不会把 resolved secret 回写到 Workspace
- Channel connectors 现在支持 `SecretRef` / `env:` / legacy literal fallback 共存，便于平滑迁移
- PluginHost 现在支持 `PluginRuntimeSpec.environmentReferences` / `headerReferences` 的 `SecretRef` 解析，resolved 值只保留在运行时内存
- Gateway 可通过 `/api/system/secret-migration-report` 输出脱敏迁移证据，并写入 `config/secret-migration-report.json`
- Gateway 启动时会自动做 startup repair inspection，并把结果写入 `config/startup-repair-report.json`
- manual-first update 的快照会写入 `config/update-state.json`
- diagnostic bundle 与 backup export/import 相关归档路径都已限制在 workspace 内

## 9. 相关文档

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/README.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_MANUAL.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/OPS_RUNBOOK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEVELOPER_GUIDE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/RUNTIME_CONFIG_ACTIVATION_PLAN.md`
