# kodaclaw-desktop

KodaClaw 的桌面壳。Iteration 6 已完成并通过 acceptance pack 收口。

当前能力：

- `Electron + TypeScript` 主进程/预加载进程骨架
- `BrowserWindow` 加载策略：
  - 开发态优先加载 `KODACLAW_DESKTOP_WEB_DEV_SERVER_URL`（默认 `http://127.0.0.1:4173`）
  - 生产态优先加载 `../kodaclaw-web/dist/index.html`
  - 找不到 web entry 时回退到本地 `placeholder.html`
- Gateway 生命周期桥：
  - `AttachOnly`：附着到已运行的本地 Gateway
  - `ManagedChild`：在开发态默认通过 `dotnet run --project ... --urls ...` 启动并监督本地 Gateway
  - 支持健康等待、restart、退出协调，以及由主进程通过 IPC 向 renderer 提供 runtime config
- 关闭窗口默认隐藏，支持 macOS 激活恢复窗口
- tray / menu 与快捷键：
  - `Show KodaClaw`
  - `Open Chat`
  - `Open Inbox`
  - `Open Canvas`
  - `Open Channels`
  - `Restart Gateway`
  - `Quit`
  - `CommandOrControl+Shift+K` 用于显示或聚焦主窗口
- 通知与 launch target：
  - 定时轮询 `/api/settings`、`/api/approvals?status=Pending`、`/api/inbox?status=Open`
  - 复用 `notificationsEnabled` 与 quiet hours 设置
  - approval / inbox 通知会按 underlying inbox 关系去重
  - notification click、tray、startup args、`kodaclaw://...` 协议 payload 都会收口为统一 launch target
  - 支持 `--kodaclaw-route=/channels/binding-01` 与 `--kodaclaw-target='{\"desk\":\"inbox\"}'`
- 手动更新 handoff：
  - 主进程会把 `appVersion` / `releaseChannel` 注入 renderer runtime config，供共享 `Models / Settings` desk 触发 `Update Watch`
  - `Update Watch` 会通过 Gateway `GET /api/system/update-state` / `POST /api/system/update-check` 展示 release channel、release notes、download handoff 与 operator notes
  - release notes / download URL 会通过 `shell.openExternal` 交给系统浏览器，不在桌面壳内做 silent install
- Diagnostic bundle handoff：
  - 共享 `Sessions / Diagnostics` desk 现可通过 Gateway `POST /api/diagnostics/bundle-export` 导出默认脱敏的 diagnostic bundle
  - desktop 模式下 renderer 会把 `platform` / `appVersion` / `releaseChannel` / `gatewayLifecycleMode` runtime context 一并写入 bundle 的 `snapshot/desktop/runtime-context.json`
  - bundle 默认附带 `manifest.json` / `redaction-summary.json`、diagnostics recent/timeline、session `meta.json`、repair/update evidence 与 log summary，并明确排除 raw secrets 与完整聊天正文
- preload bridge 已对齐 desktop 契约：
  - `window.kodaClawDesktop.getRuntimeConfig()` 返回 `{ gatewayUrl, gatewayToken, platform, appVersion, releaseChannel, desktopMode, initialTarget, gatewayLifecycleMode }`
  - `window.kodaClawDesktop.onLaunchTarget(listener)` 监听主进程转发的 launch target 事件
  - `window.kodaClawDesktop.openTarget(target)` 发送 target 到主进程做基础转发
  - `window.kodaClawDesktop.showWindow()` 可显式恢复主窗口

当前约束：

- 仍未覆盖签名分发、silent auto-update、Keychain、系统协议注册硬化等后续主题
- packaged app 默认优先寻找相邻 `../kodaclaw-web/dist/index.html`；也可以通过环境变量显式指定 renderer 入口

常用环境变量：

- `KODACLAW_DESKTOP_WEB_DEV_SERVER_URL`：开发态 renderer URL，默认 `http://127.0.0.1:4173`
- `KODACLAW_DESKTOP_WEB_DIST_INDEX`：显式指定打包态要加载的 `index.html`
- `KODACLAW_DESKTOP_GATEWAY_URL`：desktop preload 注入给 renderer 的 Gateway URL
- `KODACLAW_DESKTOP_GATEWAY_TOKEN`：desktop preload 注入给 renderer 的 Bearer token
- `KODACLAW_DESKTOP_RELEASE_CHANNEL`：显式指定 desktop release channel；为空时回退到 `KODACLAW_UPDATE_RELEASE_CHANNEL`
- `KODACLAW_DESKTOP_INITIAL_TARGET_JSON`：首个 launch target，例如 `{"desk":"inbox","reason":"notification"}`
- `KODACLAW_DESKTOP_GATEWAY_LIFECYCLE_MODE`：`AttachOnly` 或 `ManagedChild`
- `KODACLAW_DESKTOP_NOTIFICATION_POLL_MS`：通知轮询周期，默认 `30000`
- `KODACLAW_UPDATE_RELEASE_CHANNEL`：Gateway / Desktop 共享的默认 release channel
- `KODACLAW_UPDATE_MANIFEST_PATH`：Gateway update manifest 路径；若 desktop 以 `ManagedChild` 启动 Gateway，可直接继承此变量支撑 `Update Watch`

常用命令：

- `npm run typecheck`
- `npm run build`
- `npm run test`
- `npm run dev`
- `npm run smoke:wave3`
- `npm run smoke:managed-gateway`
- `npm run package:smoke`

最新验证：

- `npm run typecheck`
- `npm run build`
- `npm run test`
- `npm run smoke:wave3`
- `npm run smoke:managed-gateway`
- `npm run package:smoke`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/models-settings-desk.spec.tsx`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0212-models-settings.spec.ts`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npm run test -- --run src/__tests__/sessions-diagnostics-desk.spec.tsx`
- `cd /Users/vanzheng/projects/ai-agent/kode-sdk-csharp/products/KodaClaw/apps/kodaclaw-web && npx playwright test tests/kc0211-sessions-diagnostics.spec.ts`
