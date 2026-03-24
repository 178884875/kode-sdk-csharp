# Iteration 49 FREEZE — 桌面安装包 + 首次启动引导

**日期**：2026-03-24
**类型**：新功能
**范围**：apps/kodaclaw-desktop / electron-builder / Makefile

---

## 背景与动机

KodaClaw 目前只能通过 `make run-gateway` + `npm run dev` 组合在开发环境运行，无法分发给最终用户。
本迭代目标：**打包成可分发的安装包，用户下载后打开即有引导界面，填写 API Key 后直接使用。**

---

## 范围（IN SCOPE）

### 轨道一：electron-builder 正式发行配置

- 新增 `electron-builder.release.json`（独立于现有 smoke 配置）
- 目标：
  - macOS → `.dmg`
  - Windows → NSIS `.exe` installer
  - Linux → `.AppImage`
- `extraResources`：打包 self-contained Gateway 二进制（`gateway/` 目录）
- `files`：`dist/**/*`（Electron 编译输出）+ `kodaclaw-web/dist/**/*`（Web UI 静态文件）

### 轨道二：打包时 Gateway 二进制路径解析

- `resolveGatewayCommand()` 增加打包模式分支：
  - `app.isPackaged === true` → `process.resourcesPath/gateway/KodaClaw.Gateway[.exe]`
  - `app.isPackaged === false` → 保持现有 `dotnet run --project ...` 逻辑不变
- `spawnManagedGateway()` 在 spawn env 中注入：
  - `KODACLAW_WORKSPACE_ROOT=<userWorkspaceRoot>`（`~/.kodaclaw`）
  - `ANTHROPIC_API_KEY` / `OPENAI_API_KEY`（从 workspace `.env` 读取，如存在）
- `getUserWorkspaceRoot()` helper：返回 `path.join(os.homedir(), '.kodaclaw')`

### 轨道三：首次启动检测与 Onboarding 窗口

- `isFirstRun()` 判断逻辑：检查 `~/.kodaclaw/` 目录**不存在**，则为首次运行
- `app.whenReady()` 启动流程变为：
  ```
  isFirstRun() === true  → 打开 onboarding 窗口（跳过 initializeGatewayRuntime）
  isFirstRun() === false → 原有流程（initializeGatewayRuntime → createMainWindow）
  ```
- Onboarding 窗口：`BrowserWindow` 加载 `onboarding.html`，尺寸 520×480，不可调大小，无菜单栏
- IPC handler：`kodaclaw:setup-complete` 接收 `{ provider: 'anthropic' | 'openai', apiKey: string }`
  1. `fs.mkdirSync('~/.kodaclaw/workspace', { recursive: true })`（创建 workspace 目录结构）
  2. `fs.mkdirSync('~/.kodaclaw/config', { recursive: true })`
  3. 写入 `~/.kodaclaw/.env`：`ANTHROPIC_API_KEY=xxx` 或 `OPENAI_API_KEY=xxx`
  4. 关闭 onboarding 窗口
  5. 调用 `initializeGatewayRuntime()` → `createMainWindow()`（后续和正常启动相同）

### 轨道四：Onboarding UI（onboarding.html）

纯 HTML + 内联 CSS/JS，不依赖 React 构建，与 Electron 主进程通过 `ipcRenderer` 通信（需对应 preload 扩展）。

界面结构：
1. **标题区**：KodaClaw logo 文字 + "欢迎使用 KodaClaw" 副标题
2. **Provider 选择**：两个卡片（Anthropic Claude / OpenAI），点击高亮选中
3. **API Key 输入**：`<input type="password">`，placeholder 随 provider 变化
4. **"开始使用" 按钮**：非空校验；点击后按钮变为 "正在启动…" disabled 状态
5. **错误提示**：内联红色文字，不弹窗

Preload 补充（`preload.ts`）：
- 暴露 `window.kodaclawSetup.complete(provider, apiKey)` → `ipcRenderer.invoke('kodaclaw:setup-complete', ...)`

### 轨道五：Makefile 打包目标

```makefile
package-gateway-mac:        # dotnet publish -r osx-arm64 --self-contained
package-gateway-mac-x64:    # dotnet publish -r osx-x64  --self-contained
package-gateway-win:        # dotnet publish -r win-x64  --self-contained
package-gateway-linux:      # dotnet publish -r linux-x64 --self-contained

build-web-prod:             # cd apps/kodaclaw-web && npm run build

package-mac:                # package-gateway-mac + build-web-prod + electron-builder --config electron-builder.release.json --mac
package-win:                # package-gateway-win + build-web-prod + electron-builder --config electron-builder.release.json --win
package-linux:              # package-gateway-linux + build-web-prod + electron-builder --config electron-builder.release.json --linux
package:                    # package-mac（默认仅当前平台，CI 中按需拆分）
```

---

## 非目标（OUT OF SCOPE）

- 不做代码签名 / Notarization（macOS）或 Authenticode（Windows）——留后续迭代
- 不做自动更新机制（Update 配置节骨架已存在，但实现留后续）
- 不在 Onboarding 做连通性测试（用户填完即启动，如果 key 错了进入主界面后会有错误）
- 不做 Keychain 存储（v1 写 `.env` 文件，后续迭代迁移到 ModelHub Keychain）
- 不改变 `electron-builder.json`（smoke 配置保持不动）
- 不做 Windows / Linux 的 preload 差异化处理

---

## 关键契约

### `~/.kodaclaw/.env` 写入格式

```bash
# 由 KodaClaw 首次启动引导写入
ANTHROPIC_API_KEY=sk-ant-xxxxx
# 或
OPENAI_API_KEY=sk-xxxxx
```

### Gateway spawn env（packaged 模式追加）

```ts
{
  ...process.env,
  ASPNETCORE_URLS: runtimeConfig.gatewayUrl,
  KODACLAW_GATEWAY_TOKEN: runtimeConfig.gatewayToken,
  KODACLAW_WORKSPACE_ROOT: getUserWorkspaceRoot(),  // 新增
  ANTHROPIC_API_KEY: readUserEnvKey('ANTHROPIC_API_KEY'),  // 新增，可能为 undefined
  OPENAI_API_KEY: readUserEnvKey('OPENAI_API_KEY'),        // 新增，可能为 undefined
}
```

### electron-builder.release.json 关键字段

```json
{
  "appId": "ai.kodaclaw.desktop",
  "productName": "KodaClaw",
  "directories": { "output": "dist-release" },
  "files": ["dist/**/*", "package.json", "placeholder.html"],
  "extraResources": [
    { "from": "../../publish/gateway-${platform}", "to": "gateway", "filter": ["**/*"] }
  ],
  "mac": { "target": [{ "target": "dmg", "arch": ["arm64", "x64"] }] },
  "win": { "target": [{ "target": "nsis", "arch": ["x64"] }] },
  "linux": { "target": [{ "target": "AppImage", "arch": ["x64"] }] }
}
```

---

## 验证矩阵

| 层级 | 验证内容 | 工具 |
|------|---------|------|
| L0 | `npm run typecheck`（desktop）通过；`dotnet build` 0 错 0 警告 | tsc / dotnet |
| L1 | smoke：`make desktop-package-smoke` 仍通过（electron-builder.json 不变） | Makefile |
| L5 | Dogfood：删除 `~/.kodaclaw/`，`make package-mac` 构建，打开 .app，验证引导窗口出现、填 Key 后进入主界面；再次打开验证跳过引导 | 真实打包产物 |
