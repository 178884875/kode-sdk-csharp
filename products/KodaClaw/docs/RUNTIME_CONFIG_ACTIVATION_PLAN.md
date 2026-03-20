# Runtime Config Activation Plan

Last updated: 2026-03-19
Status: Completed

## 1. 背景

用户反馈了两个直接影响可用性的配置问题：

1. 在 Runtime Control 里设置 default model 之后，`Chat Lane` 仍然提示：`KodaClaw chat is not configured. Set KODACLAW_DEFAULT_MODEL and one provider API key.`
2. Gateway 启动时不会默认读取当前目录 `.env`，导致 Model Registry endpoint 中的 `ApiKeyEnvironmentVariable` 即使在本地 `.env` 里存在，也无法被 runtime 解析。

同时，产品根目录缺少正式纳管的 `appsettings.json` / `appsettings.Development.json` 入口，配置说明也没有覆盖当前目录 bootstrap 语义。

## 2. 目标

本轮收口目标：

- 让 Gateway 默认加载当前目录 `.env` / `.env.local`
- 让 Gateway 默认加载当前目录 `appsettings.json` / `appsettings.{Environment}.json`
- 让 Runtime Control 切换 default model 后，新 chat/new session 无需重启 Gateway 即可生效
- 为产品根目录补齐受控的 `appsettings*.json` 样例
- 把配置说明、使用手册、开发/运维文档同步到一致状态

## 3. 实施决策

### 3.1 当前目录 `.env` / `.env.local` 引导

- 新增 `GatewayConfigurationBootstrap.LoadCurrentDirectoryDotEnvFiles(...)`
- 启动时先读取 `.env`，再读取 `.env.local`
- `.env.local` 可覆盖 `.env`
- shell 已存在的真实环境变量保持最高优先级，不被 `.env` 覆盖
- 选择把值注入真实 `Environment`，而不是只放进 `IConfiguration`，因为现有链路里仍有多处直接调用 `Environment.GetEnvironmentVariable(...)`

## 3.2 当前目录 `appsettings*.json` 引导

- 新增 `GatewayConfigurationBootstrap.ApplyCurrentDirectoryJsonFiles(...)`
- 把 `appsettings.json` / `appsettings.{Environment}.json` 插入到 env vars / command line 之前
- 实际优先级固定为：
  1. `appsettings.json`
  2. `appsettings.{Environment}.json`
  3. `.env`
  4. `.env.local`
  5. 真实 shell env
  6. command line

## 3.3 Runtime 动态激活

- Runtime 新增 `RuntimeConfigurationSnapshot` 与 `IRuntimeConfigurationResolver`
- Gateway 侧新增 `GatewayRuntimeConfigurationResolver`
- `DynamicModelProvider` 在每次请求时按最新 runtime snapshot 解析 provider
- `MainSessionService` / `AutomationSessionService` / `ChannelSessionService` 在创建或恢复 session 时动态解析当前 model
- 结果：Runtime Control 中把 endpoint 设为 default 后，新 chat/new session 直接生效，不需要重启 Gateway

## 3.4 产品根配置样例

- 新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/appsettings.json`
- 新增 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/appsettings.Development.json`
- `.gitignore` 已允许纳管 `products/KodaClaw/appsettings.Development.json`

## 4. 验收与验证

### 4.1 定向测试

已新增并通过：

- `GatewayConfigurationBootstrapIntegrationTests`
  - `.env` 加载缺失键
  - `.env.local` 覆盖 `.env`
  - 真实环境变量优先于 `.env`
  - 当前目录 `appsettings*.json` 在 env / command line 之前生效
- `ModelRuntimeBootstrapIntegrationTests`
  - direct runtime secret-ref bootstrap
  - default model endpoint secret-ref bootstrap
  - default model 配置后 chat 无需重启即可激活

### 4.2 全量验证要求

本轮收口后的标准验证矩阵：

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test`

如需做浏览器层确认，可追加：

- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`

## 5. 文档闭环

本轮至少同步以下文档：

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/README.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEV_CONFIG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEVELOPER_GUIDE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_MANUAL.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/OPS_RUNBOOK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/RELEASE_SUMMARY_2026-03-19.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/.env.example`

## 6. 已知非目标

本轮没有处理 Web Runtime Control 对 `ApiKeySecretRef` 的编辑能力；当前 Web 合同/UI 仍主要暴露 `ApiKeyEnvironmentVariable`。这不影响当前“`.env` + Runtime Control + default model 即时激活”的主链路，但属于下一轮可继续增强的 operator UX。
