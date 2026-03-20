# KodaClaw 开发者指南

Last updated: 2026-03-20

## 1. 适合谁看

这份文档面向参与 KodaClaw 开发的人，重点是：

- 本地开发环境与配置装载方式
- 项目结构
- 常用验证命令
- 推荐开发流程
- 文档闭环要求

## 2. 项目结构

核心目录：

- `src/`：后端与领域模块
- `apps/kodaclaw-web`：共享 Web shell
- `apps/kodaclaw-desktop`：Electron 桌面壳
- `tests/`：Contract / Integration / Unit / Smoke
- `docs/`：产品、架构、迭代、验收与运维文档

关键模块：

- `KodaClaw.Gateway`
- `KodaClaw.Workspace`
- `KodaClaw.Runtime`
- `KodaClaw.ControlPlane`
- `KodaClaw.ChannelHub`
- `KodaClaw.PluginHost`
- `KodaClaw.Automation`

## 3. 本地开发准备

### 3.1 .NET

```bash
dotnet build /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln
```

### 3.2 Web

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web
npm install
```

### 3.3 Desktop

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop
npm install
```

## 4. 本地配置约定

### 4.1 一定要注意当前工作目录

Gateway 现在会默认加载“当前工作目录”下的：

- `.env`
- `.env.local`
- `appsettings.json`
- `appsettings.{Environment}.json`

因此，开发时推荐统一从产品根目录启动：

```bash
cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw
cp .env.example .env.local

dotnet run \
  --project /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj \
  --urls http://127.0.0.1:5076
```

### 4.2 配置优先级

当前优先级从低到高：

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. `.env`
4. `.env.local`
5. 真实 shell 环境变量
6. 命令行参数

补充：

- `.env` / `.env.local` 改动后需要重启 Gateway
- `appsettings*.json` 已接入 reload-on-change，但为了避免 operator 歧义，基础设施变更仍建议重启
- Runtime Control 切换 default model 是已经明确支持的“无需重启即时生效”链路

### 4.3 推荐联读

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEV_CONFIG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/RUNTIME_CONFIG_ACTIVATION_PLAN.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/GATEWAY_RUNTIME_FLOW_AND_REFACTOR_PLAN.md`

### 4.4 前端 Locale / UI 约定

- `kodaclaw-web` 当前默认语言是 `zh-CN`，并通过 `localStorage["kodaclaw.locale"]` 持久化用户选择。
- Web shell 右上角语言切换器负责切到 `en-US`，同时同步 `document.documentElement.lang`、document title 与 description。
- 新增前端文案时，优先走 `src/i18n/I18nProvider.tsx` 驱动的 locale text，而不是把字符串直接硬编码在组件逻辑里。
- 调整 shell / desk 文案时，除了组件测试，也要同步 Playwright 断言；优先使用 `data-testid`，降低单语文案耦合。
- 组件测试统一可复用 `src/__tests__/test-utils.tsx` 里的 `renderWithI18n(...)`。

### 4.5 Gateway 开发写入边界（Wave 4 已完成）

Gateway 现在已经完成 `Program.cs` 维护性重构收口，后续开发时请遵守下面的写入边界：

- 不要再把新 endpoint、helper 或领域逻辑写回 `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/src/KodaClaw.Gateway/Program.cs`
- 新增 API 时优先放入对应的 `Endpoints/GatewayApp.*Endpoints.cs`
- request 标准化放到 `Validation/`，HTTP 结果映射放到 `Mapping/`
- auth/cors/correlation/diagnostics/query/session/canvas 这类横切 helper 继续放在 `Infrastructure/`
- 如果只是补服务注册、中间件顺序或顶层路由编排，才修改 `Composition/GatewayApp.Composition.cs`

## 5. 常用命令

### 5.1 直接命令

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj --filter "BootstrapFlowIntegrationTests|GatewayDiagnosticsIntegrationTests|ModelApiIntegrationTests|SettingsApiIntegrationTests|AutomationApiIntegrationTests|CanvasApiIntegrationTests|PluginApiIntegrationTests|ApprovalApiIntegrationTests|InboxApiIntegrationTests|GatewaySessionsIntegrationTests|ChannelApiIntegrationTests|BackupApiIntegrationTests|StartupRepairApiIntegrationTests|UpdateStateApiIntegrationTests|SecretMigrationReportIntegrationTests" -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test:e2e`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run smoke:wave3`

### 5.2 Makefile

KodaClaw 提供独立 Makefile：

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/Makefile`

常用目标：

- `make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw help`
- `make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw test-solution`
- `make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw verify-web`
- `make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw verify-desktop`
- `make -C /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw verify-all`

详细说明见：

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/MAKEFILE_GUIDE.md`

## 6. 推荐开发流程

1. 先阅读相关迭代文档、ADR 和配置说明
2. 明确写集与测试范围，避免与现有脏工作区相互覆盖
3. 优先补 contract / integration / component test
4. 再做实现
5. 完成后同步 README / status / runbook / manual / dev config
6. 最后跑 solution 串行回归；如改动涉及 Web 交互，再跑 `npm run build && npm run test`

## 7. 文档同步要求

本项目要求“实现、测试、文档闭环一致”。这轮 Runtime/Gateway 配置修复后，常见需要同步的文档包括：

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/README.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_BACKLOG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ARCHITECTURE.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/DEV_CONFIG.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/USER_MANUAL.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/OPS_RUNBOOK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/RUNTIME_CONFIG_ACTIVATION_PLAN.md`
- 对应迭代 acceptance pack / freeze / ADR（如果范围或结论发生变化）

## 8. 当前验证基线

当前稳定基线是：

- `dotnet test /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/KodaClaw.sln -m:1`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run build && npm run test && npm run test:e2e`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-desktop && npm run test && npm run smoke:wave3 && npm run smoke:managed-gateway && npm run package:smoke`

前端两轮收口后的额外约定：

- `kodaclaw-web` 现在是 Chinese-first shell；新增 E2E 断言时优先按默认中文界面编写，再保留少量 locale-switch coverage。
- 如果只验证前端改动，至少保证最新的 `npm run build`、`npm run test`、`npm run test:e2e` 三条命令全部绿色。

## 9. 推荐联读

- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ENGINEERING_PLAYBOOK.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/IMPLEMENTATION_STATUS.md`
- `/Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/docs/ITERATION_7_ACCEPTANCE_PACK.md`
