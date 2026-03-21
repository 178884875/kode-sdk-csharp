import {
  app,
  BrowserWindow,
  globalShortcut,
  ipcMain,
  Menu,
  nativeImage,
  Notification,
  shell,
  Tray,
  type MenuItemConstructorOptions,
} from "electron";
import { randomBytes } from "node:crypto";
import fs from "node:fs";
import { spawn } from "node:child_process";
import path from "node:path";
import {
  type DesktopLaunchTarget,
  type DesktopRuntimeConfig,
  type GatewayLifecycleMode,
  type UpdateReleaseChannel,
  isDesktopLaunchTarget,
} from "./desktop-shell-types";
import {
  resolveInitialLaunchTarget,
  resolveLaunchTargetFromArgv,
  resolveLaunchTargetFromProtocolUrl,
  resolveLaunchTargetFromRoute,
} from "./launch-targets";
import {
  buildNotificationCandidates,
  filterUnseenCandidates,
  isWithinQuietHours,
  type DesktopNotificationCandidate,
  type NotificationApproval,
  type NotificationInboxItem,
  type NotificationSettings,
} from "./notification-policy";

const DEFAULT_DEV_SERVER_URL = "http://127.0.0.1:4173";
const DEFAULT_GATEWAY_URL = "http://127.0.0.1:5076";
const DEFAULT_GATEWAY_HEALTH_POLL_INTERVAL_MS = 400;
const DEFAULT_ATTACH_HEALTH_TIMEOUT_MS = 3000;
const DEFAULT_MANAGED_HEALTH_TIMEOUT_MS = 15000;
const DEFAULT_NOTIFICATION_POLL_INTERVAL_MS = 30000;
const DEFAULT_GLOBAL_SHORTCUT = "CommandOrControl+Shift+K";
const SMOKE_EXIT_DELAY_MS = 800;
const WINDOW_STATE = {
  width: 1440,
  height: 920,
  minWidth: 1024,
  minHeight: 700,
} as const;

type GatewayStatus = "starting" | "healthy" | "degraded" | "stopped";

type GatewayRuntimeState = {
  config: DesktopRuntimeConfig;
  status: GatewayStatus;
  childPid: number | null;
  lastError: string | null;
};

type GatewayCommand = {
  command: string;
  args: string[];
  source: "default-dotnet-run" | "env-json";
};

type ManagedGatewayProcess = ReturnType<typeof spawn>;
type InboxQueryResponse = { items: NotificationInboxItem[] };
type ApprovalQueryResponse = { items: NotificationApproval[] };

const runtimeConfig: DesktopRuntimeConfig = {
  gatewayUrl: readStringEnv("KODACLAW_DESKTOP_GATEWAY_URL") ?? DEFAULT_GATEWAY_URL,
  gatewayToken: resolveGatewayToken(),
  platform: process.platform,
  appVersion: app.getVersion(),
  releaseChannel: readReleaseChannel(),
  desktopMode: true,
  initialTarget: resolveInitialLaunchTarget({
    initialTargetJson: readStringEnv("KODACLAW_DESKTOP_INITIAL_TARGET_JSON"),
    argv: process.argv,
  }),
  gatewayLifecycleMode: readGatewayLifecycleMode(),
};

const gatewayState: GatewayRuntimeState = {
  config: runtimeConfig,
  status: "starting",
  childPid: null,
  lastError: null,
};

let isQuitting = false;
let desktopBridgeRegistered = false;
let mainWindow: BrowserWindow | null = null;
let tray: Tray | null = null;
let managedGatewayProcess: ManagedGatewayProcess | null = null;
let gatewayTransition: Promise<void> | null = null;
let registeredShortcut: string | null = null;
let quitCleanupStarted = false;
let notificationPollTimer: NodeJS.Timeout | null = null;
let notificationPollInFlight = false;
const seenNotificationSignatures = new Set<string>();
const pendingExternalLaunchTargets: DesktopLaunchTarget[] = [];
const capturedNotifications: Array<{
  id: string;
  title: string;
  target: DesktopLaunchTarget;
}> = [];
// Maps a notification instance to its approvalId for action button handling.
const pendingApprovalNotifications = new Map<Notification, string>();
const hasSingleInstanceLock = app.requestSingleInstanceLock();

if (!hasSingleInstanceLock) {
  app.quit();
}

app.on("second-instance", (_event, argv) => {
  handleArgvLaunchTargets(argv, "second-instance");
});

app.on("open-url", (event, rawUrl) => {
  event.preventDefault();
  handleProtocolLaunchTarget(rawUrl, "open-url");
});

function readStringEnv(name: string): string | null {
  const value = process.env[name]?.trim();
  return value ? value : null;
}

function readBooleanEnv(name: string): boolean {
  const value = readStringEnv(name);
  return value === "1" || value === "true" || value === "yes";
}

function readIntegerEnv(name: string): number | null {
  const value = readStringEnv(name);
  if (!value) {
    return null;
  }

  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

function readGatewayLifecycleMode(): GatewayLifecycleMode {
  return readStringEnv("KODACLAW_DESKTOP_GATEWAY_LIFECYCLE_MODE") === "ManagedChild"
    ? "ManagedChild"
    : "AttachOnly";
}

function readReleaseChannel(): UpdateReleaseChannel {
  const configured = readStringEnv("KODACLAW_DESKTOP_RELEASE_CHANNEL")
    ?? readStringEnv("KODACLAW_UPDATE_RELEASE_CHANNEL");
  if (configured === "Preview" || configured === "Nightly" || configured === "Custom") {
    return configured;
  }

  return "Stable";
}

function isSmokeMode(): boolean {
  return readBooleanEnv("KODACLAW_DESKTOP_SMOKE_MODE");
}

function shouldCaptureNotificationSmoke(): boolean {
  return isSmokeMode() && readBooleanEnv("KODACLAW_DESKTOP_SMOKE_CAPTURE_NOTIFICATIONS");
}

function resolveGatewayToken(): string | null {
  const configured = readStringEnv("KODACLAW_DESKTOP_GATEWAY_TOKEN");
  if (configured) {
    return configured;
  }

  if (readGatewayLifecycleMode() === "ManagedChild") {
    return randomBytes(18).toString("base64url");
  }

  return null;
}

function getDesktopRoot(): string {
  return app.getAppPath();
}

function getPreloadPath(): string {
  return path.resolve(__dirname, "preload.js");
}

function getPlaceholderPath(): string {
  return path.resolve(getDesktopRoot(), "placeholder.html");
}

function getWebDistIndexPath(): string {
  const explicitPath = readStringEnv("KODACLAW_DESKTOP_WEB_DIST_INDEX");
  if (explicitPath) {
    return path.resolve(explicitPath);
  }

  return path.resolve(getDesktopRoot(), "..", "kodaclaw-web", "dist", "index.html");
}

function getDevServerUrl(): string | null {
  const configured = readStringEnv("KODACLAW_DESKTOP_WEB_DEV_SERVER_URL");
  if (configured) {
    return configured;
  }

  if (isSmokeMode()) {
    return null;
  }

  return app.isPackaged ? null : DEFAULT_DEV_SERVER_URL;
}

function getGatewayHealthTimeoutMs(): number {
  return readIntegerEnv("KODACLAW_DESKTOP_GATEWAY_HEALTH_TIMEOUT_MS") ??
    (runtimeConfig.gatewayLifecycleMode === "ManagedChild"
      ? DEFAULT_MANAGED_HEALTH_TIMEOUT_MS
      : DEFAULT_ATTACH_HEALTH_TIMEOUT_MS);
}

function getNotificationPollIntervalMs(): number {
  return readIntegerEnv("KODACLAW_DESKTOP_NOTIFICATION_POLL_MS") ?? DEFAULT_NOTIFICATION_POLL_INTERVAL_MS;
}

function getGlobalShortcutAccelerator(): string {
  return readStringEnv("KODACLAW_DESKTOP_GLOBAL_SHORTCUT") ?? DEFAULT_GLOBAL_SHORTCUT;
}

function getDefaultGatewayProjectPath(): string {
  return path.resolve(getDesktopRoot(), "..", "..", "src", "KodaClaw.Gateway", "KodaClaw.Gateway.csproj");
}

function getExplicitGatewayCommand(): GatewayCommand | null {
  const commandJson = readStringEnv("KODACLAW_DESKTOP_GATEWAY_COMMAND_JSON");
  if (!commandJson) {
    return null;
  }

  try {
    const parsed = JSON.parse(commandJson) as unknown;
    if (!Array.isArray(parsed) || parsed.length === 0 || parsed.some((item) => typeof item !== "string")) {
      return null;
    }

    const [command, ...args] = parsed;
    return {
      command,
      args,
      source: "env-json",
    };
  } catch {
    return null;
  }
}

function resolveGatewayCommand(): GatewayCommand | null {
  const explicit = getExplicitGatewayCommand();
  if (explicit) {
    return explicit;
  }

  const configuredProject = readStringEnv("KODACLAW_DESKTOP_GATEWAY_PROJECT");
  const projectPath = configuredProject ? path.resolve(configuredProject) : getDefaultGatewayProjectPath();
  if (!fs.existsSync(projectPath)) {
    return null;
  }

  return {
    command: "dotnet",
    args: [
      "run",
      "--project",
      projectPath,
      "--urls",
      runtimeConfig.gatewayUrl ?? DEFAULT_GATEWAY_URL,
    ],
    source: "default-dotnet-run",
  };
}

function buildGatewayHeaders(json = false): Headers {
  const headers = new Headers();
  if (runtimeConfig.gatewayToken) {
    headers.set("Authorization", `Bearer ${runtimeConfig.gatewayToken}`);
  }

  if (json) {
    headers.set("Content-Type", "application/json");
  }

  return headers;
}

async function requestGatewayJson<TResponse>(pathName: string): Promise<TResponse> {
  const gatewayUrl = runtimeConfig.gatewayUrl;
  if (!gatewayUrl) {
    throw new Error("Desktop Gateway URL is not configured.");
  }

  const response = await fetch(`${gatewayUrl.replace(/\/$/, "")}${pathName}`, {
    headers: buildGatewayHeaders(),
  });

  if (!response.ok) {
    throw new Error(`Gateway request failed for ${pathName}: ${response.status} ${response.statusText}`);
  }

  return await response.json() as TResponse;
}

async function waitForGatewayHealthy(url: string, timeoutMs: number): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  const healthUrl = `${url.replace(/\/$/, "")}/api/system/health`;

  while (Date.now() <= deadline) {
    try {
      const response = await fetch(healthUrl);
      if (response.ok) {
        return true;
      }
    } catch {
      // Keep polling until timeout.
    }

    await delay(DEFAULT_GATEWAY_HEALTH_POLL_INTERVAL_MS);
  }

  return false;
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}

function queueExternalLaunchTarget(target: DesktopLaunchTarget): void {
  pendingExternalLaunchTargets.push(target);
}

function stopNotificationPolling(): void {
  if (notificationPollTimer) {
    clearInterval(notificationPollTimer);
    notificationPollTimer = null;
  }
}

function showDesktopNotification(candidate: DesktopNotificationCandidate): void {
  if (shouldCaptureNotificationSmoke()) {
    capturedNotifications.push({
      id: candidate.id,
      title: candidate.title,
      target: candidate.target,
    });
    return;
  }

  if (isSmokeMode() || !Notification.isSupported()) {
    return;
  }

  // On macOS, Notification supports action buttons via the `actions` option.
  // We only add quick-approve/reject actions for ChannelDelivery approvals.
  const isChannelDelivery = candidate.isChannelDelivery === true && Boolean(candidate.approvalId);
  const notification = new Notification({
    title: candidate.title,
    body: candidate.body,
    ...(isChannelDelivery && process.platform === "darwin"
      ? {
          actions: [
            { type: "button", text: "✓ 发送" },
            { type: "button", text: "✗ 不发送" },
          ],
        }
      : {}),
  });

  if (isChannelDelivery && candidate.approvalId) {
    pendingApprovalNotifications.set(notification, candidate.approvalId);
  }

  notification.on("click", () => {
    pendingApprovalNotifications.delete(notification);
    void dispatchLaunchTarget({
      ...candidate.target,
      reason: candidate.target.reason ?? "notification",
    });
  });

  notification.on("close", () => {
    pendingApprovalNotifications.delete(notification);
  });

  notification.show();
}

async function handleApprovalNotificationAction(notification: Notification, actionText: string): Promise<void> {
  const approvalId = pendingApprovalNotifications.get(notification);
  if (!approvalId) {
    return;
  }

  pendingApprovalNotifications.delete(notification);

  const gatewayUrl = runtimeConfig.gatewayUrl;
  if (!gatewayUrl) {
    return;
  }

  try {
    const endpoint = actionText === "✓ 发送" ? "approve" : "reject";
    await fetch(`${gatewayUrl.replace(/\/$/, "")}/api/approvals/${approvalId}/${endpoint}`, {
      method: "POST",
      headers: buildGatewayHeaders(true),
      body: JSON.stringify({}),
    });
  } catch (err) {
    console.error("[desktop] Failed to handle approval notification action.", err);
  }
}

async function pollNotificationsOnce(): Promise<void> {
  if (notificationPollInFlight || !runtimeConfig.gatewayToken || gatewayState.status !== "healthy") {
    return;
  }

  notificationPollInFlight = true;
  try {
    const settings = await requestGatewayJson<NotificationSettings>("/api/settings");
    if (!settings.notificationsEnabled || isWithinQuietHours(settings)) {
      return;
    }

    const [approvalsResponse, inboxResponse] = await Promise.all([
      requestGatewayJson<ApprovalQueryResponse>("/api/approvals?status=Pending&limit=10"),
      requestGatewayJson<InboxQueryResponse>("/api/inbox?status=Open&limit=10"),
    ]);

    const candidates = buildNotificationCandidates(approvalsResponse.items, inboxResponse.items);
    const unseen = filterUnseenCandidates(candidates, seenNotificationSignatures);
    for (const candidate of unseen) {
      seenNotificationSignatures.add(candidate.signature);
      showDesktopNotification(candidate);
    }
  } catch (error) {
    console.warn("[desktop] Notification poll failed.", error);
  } finally {
    notificationPollInFlight = false;
  }
}

function startNotificationPolling(): void {
  if (notificationPollTimer || (isSmokeMode() && !shouldCaptureNotificationSmoke())) {
    return;
  }

  notificationPollTimer = setInterval(() => {
    void pollNotificationsOnce();
  }, getNotificationPollIntervalMs());

  void pollNotificationsOnce();
}

function updateGatewayState(status: GatewayStatus, lastError: string | null = null): void {
  gatewayState.status = status;
  gatewayState.lastError = lastError;
  gatewayState.childPid = managedGatewayProcess?.pid ?? null;

  if (status === "healthy") {
    startNotificationPolling();
  } else {
    stopNotificationPolling();
  }

  refreshTrayMenu();
}

function registerGatewayProcessLogging(child: ManagedGatewayProcess): void {
  child.stdout?.on("data", (chunk: Buffer) => {
    const text = chunk.toString("utf8").trim();
    if (text) {
      console.log(`[gateway] ${text}`);
    }
  });

  child.stderr?.on("data", (chunk: Buffer) => {
    const text = chunk.toString("utf8").trim();
    if (text) {
      console.error(`[gateway] ${text}`);
    }
  });
}

function spawnManagedGateway(): ManagedGatewayProcess {
  const command = resolveGatewayCommand();
  if (!command) {
    throw new Error(
      "ManagedChild mode could not resolve a Gateway launch command. Set KODACLAW_DESKTOP_GATEWAY_COMMAND_JSON or KODACLAW_DESKTOP_GATEWAY_PROJECT.",
    );
  }

  const child = spawn(command.command, command.args, {
    cwd: getDesktopRoot(),
    env: {
      ...process.env,
      ASPNETCORE_URLS: runtimeConfig.gatewayUrl ?? DEFAULT_GATEWAY_URL,
      KODACLAW_GATEWAY_TOKEN: runtimeConfig.gatewayToken ?? "",
    },
    stdio: ["ignore", "pipe", "pipe"],
    windowsHide: true,
  });

  registerGatewayProcessLogging(child);
  child.once("exit", (code, signal) => {
    managedGatewayProcess = null;
    gatewayState.childPid = null;

    if (isQuitting) {
      return;
    }

    updateGatewayState(
      "degraded",
      `Managed Gateway exited unexpectedly (${signal ?? code ?? "unknown"}).`,
    );
  });

  managedGatewayProcess = child;
  gatewayState.childPid = child.pid ?? null;
  console.log(`[desktop] Started managed Gateway via ${command.source}. pid=${child.pid ?? "unknown"}`);
  return child;
}

async function stopManagedGateway(): Promise<void> {
  const child = managedGatewayProcess;
  if (!child) {
    return;
  }

  const exitPromise = new Promise<void>((resolve) => {
    child.once("exit", () => {
      resolve();
    });
  });

  child.kill("SIGTERM");

  const completed = await Promise.race([
    exitPromise.then(() => true),
    delay(3000).then(() => false),
  ]);

  if (!completed) {
    child.kill("SIGKILL");
    await exitPromise;
  }

  managedGatewayProcess = null;
  gatewayState.childPid = null;
}

async function runGatewayTransition(operation: () => Promise<void>): Promise<void> {
  if (gatewayTransition) {
    return gatewayTransition;
  }

  gatewayTransition = (async () => {
    try {
      await operation();
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      console.error("[desktop] Gateway transition failed.", error);
      updateGatewayState("degraded", message);
    } finally {
      gatewayTransition = null;
      refreshTrayMenu();
    }
  })();

  return gatewayTransition;
}

async function initializeGatewayRuntime(): Promise<void> {
  await runGatewayTransition(async () => {
    const gatewayUrl = runtimeConfig.gatewayUrl;
    if (!gatewayUrl) {
      updateGatewayState("degraded", "No Gateway URL is configured for the desktop shell.");
      return;
    }

    updateGatewayState("starting");

    if (runtimeConfig.gatewayLifecycleMode === "ManagedChild") {
      const explicitToken = readStringEnv("KODACLAW_DESKTOP_GATEWAY_TOKEN");
      if (explicitToken) {
        const attached = await waitForGatewayHealthy(gatewayUrl, DEFAULT_ATTACH_HEALTH_TIMEOUT_MS);
        if (attached) {
          updateGatewayState("healthy");
          return;
        }
      }

      spawnManagedGateway();
      const healthy = await waitForGatewayHealthy(gatewayUrl, getGatewayHealthTimeoutMs());
      if (!healthy) {
        updateGatewayState(
          "degraded",
          `ManagedChild did not become healthy at ${gatewayUrl} within ${getGatewayHealthTimeoutMs()}ms.`,
        );
        return;
      }

      updateGatewayState("healthy");
      return;
    }

    const attached = await waitForGatewayHealthy(gatewayUrl, getGatewayHealthTimeoutMs());
    if (!attached) {
      updateGatewayState(
        "degraded",
        `AttachOnly could not reach a healthy Gateway at ${gatewayUrl}.`,
      );
      return;
    }

    updateGatewayState("healthy");
  });
}

async function restartGateway(): Promise<void> {
  await runGatewayTransition(async () => {
    updateGatewayState("starting");

    if (runtimeConfig.gatewayLifecycleMode === "ManagedChild") {
      await stopManagedGateway();
      spawnManagedGateway();
      const healthy = await waitForGatewayHealthy(
        runtimeConfig.gatewayUrl ?? DEFAULT_GATEWAY_URL,
        getGatewayHealthTimeoutMs(),
      );
      if (!healthy) {
        updateGatewayState(
          "degraded",
          `Restarted Gateway did not become healthy at ${runtimeConfig.gatewayUrl ?? DEFAULT_GATEWAY_URL}.`,
        );
        return;
      }

      updateGatewayState("healthy");
      return;
    }

    const attached = await waitForGatewayHealthy(
      runtimeConfig.gatewayUrl ?? DEFAULT_GATEWAY_URL,
      getGatewayHealthTimeoutMs(),
    );
    updateGatewayState(
      attached ? "healthy" : "degraded",
      attached ? null : `AttachOnly could not reattach to ${runtimeConfig.gatewayUrl ?? DEFAULT_GATEWAY_URL}.`,
    );
  });
}

async function loadRenderer(window: BrowserWindow): Promise<void> {
  const devServerUrl = getDevServerUrl();
  if (devServerUrl) {
    try {
      await window.loadURL(devServerUrl);
      return;
    } catch (error) {
      console.warn("Failed to load desktop renderer from dev server URL.", error);
    }
  }

  const webDistIndex = getWebDistIndexPath();
  if (fs.existsSync(webDistIndex)) {
    await window.loadFile(webDistIndex);
    return;
  }

  await window.loadFile(getPlaceholderPath());
}

async function createMainWindow(): Promise<BrowserWindow> {
  const window = new BrowserWindow({
    ...WINDOW_STATE,
    show: false,
    autoHideMenuBar: true,
    webPreferences: {
      preload: getPreloadPath(),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  window.once("ready-to-show", () => {
    if (!isSmokeMode()) {
      window.show();
    }
  });

  window.on("close", (event) => {
    if (!isQuitting) {
      event.preventDefault();
      window.hide();
    }
  });

  window.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith("http://") || url.startsWith("https://")) {
      void shell.openExternal(url);
    }

    return { action: "deny" };
  });

  await loadRenderer(window);
  mainWindow = window;
  return window;
}

async function ensureMainWindow(): Promise<BrowserWindow> {
  if (mainWindow && !mainWindow.isDestroyed()) {
    return mainWindow;
  }

  return createMainWindow();
}

async function showMainWindow(): Promise<void> {
  const window = await ensureMainWindow();
  if (window.isMinimized()) {
    window.restore();
  }

  window.show();
  window.focus();
}

function resolveLaunchTargetForDesk(desk: string, reason: string): DesktopLaunchTarget {
  return resolveLaunchTargetFromRoute(`/${desk}`, reason) ?? { desk, reason };
}

async function dispatchLaunchTarget(target: DesktopLaunchTarget): Promise<void> {
  const window = await ensureMainWindow();
  const emit = () => {
    window.webContents.send("kodaclaw:launch-target", target);
  };

  if (window.webContents.isLoadingMainFrame()) {
    window.webContents.once("did-finish-load", emit);
  } else {
    emit();
  }

  await showMainWindow();
}

async function flushPendingExternalLaunchTargets(): Promise<void> {
  while (pendingExternalLaunchTargets.length > 0) {
    const nextTarget = pendingExternalLaunchTargets.shift();
    if (nextTarget) {
      await dispatchLaunchTarget(nextTarget);
    }
  }
}

function handleArgvLaunchTargets(argv: string[], reason: string): void {
  const target = resolveLaunchTargetFromArgv(argv, reason);
  if (!target) {
    if (app.isReady()) {
      void showMainWindow();
    }
    return;
  }

  if (app.isReady()) {
    void dispatchLaunchTarget(target);
    return;
  }

  queueExternalLaunchTarget(target);
}

function handleProtocolLaunchTarget(rawUrl: string, reason: string): void {
  const target = resolveLaunchTargetFromProtocolUrl(rawUrl, reason);
  if (!target) {
    return;
  }

  if (app.isReady()) {
    void dispatchLaunchTarget(target);
    return;
  }

  queueExternalLaunchTarget(target);
}

function createTrayIcon() {
  const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16">
      <path fill="black" d="M3 2h2.2l2.8 4.1L10.8 2H13l-3.5 5.2L13.3 14h-2.2L8 9.6 4.8 14H2.6l3.8-6.8z"/>
    </svg>
  `.trim();
  const image = nativeImage
    .createFromDataURL(`data:image/svg+xml;base64,${Buffer.from(svg, "utf8").toString("base64")}`)
    .resize({ width: 16, height: 16 });

  if (process.platform === "darwin") {
    image.setTemplateImage(true);
  }

  return image;
}

function getGatewayStatusLabel(): string {
  if (gatewayState.status === "healthy") {
    return "Gateway: healthy";
  }

  if (gatewayState.status === "starting") {
    return "Gateway: starting";
  }

  if (gatewayState.status === "stopped") {
    return "Gateway: stopped";
  }

  return gatewayState.lastError ? `Gateway: degraded (${gatewayState.lastError})` : "Gateway: degraded";
}

function buildTrayMenu(): Menu {
  const items: MenuItemConstructorOptions[] = [
    { label: "KodaClaw", enabled: false },
    { label: getGatewayStatusLabel(), enabled: false },
    { type: "separator" },
    {
      label: "Show KodaClaw",
      click: () => {
        void showMainWindow();
      },
    },
    {
      label: "Open Chat",
      click: () => {
        void dispatchLaunchTarget(resolveLaunchTargetForDesk("chat", "tray"));
      },
    },
    {
      label: "Open Inbox",
      click: () => {
        void dispatchLaunchTarget(resolveLaunchTargetForDesk("inbox", "tray"));
      },
    },
    {
      label: "Open Canvas",
      click: () => {
        void dispatchLaunchTarget(resolveLaunchTargetForDesk("canvas", "tray"));
      },
    },
    {
      label: "Open Channels",
      click: () => {
        void dispatchLaunchTarget(resolveLaunchTargetForDesk("channels", "tray"));
      },
    },
    { type: "separator" },
    {
      label: "Restart Gateway",
      click: () => {
        void restartGateway();
      },
    },
    {
      label: "Quit",
      click: () => {
        isQuitting = true;
        app.quit();
      },
    },
  ];

  return Menu.buildFromTemplate(items);
}

function refreshTrayMenu(): void {
  tray?.setContextMenu(buildTrayMenu());
}

function ensureTray(): void {
  if (tray) {
    refreshTrayMenu();
    return;
  }

  tray = new Tray(createTrayIcon());
  tray.setToolTip("KodaClaw");
  tray.on("click", () => {
    void showMainWindow();
  });
  refreshTrayMenu();
}

function registerGlobalShortcut(): void {
  const accelerator = getGlobalShortcutAccelerator();
  const registered = globalShortcut.register(accelerator, () => {
    void showMainWindow();
  });

  if (!registered) {
    console.warn(`[desktop] Failed to register global shortcut: ${accelerator}`);
    return;
  }

  registeredShortcut = accelerator;
}

function registerDesktopBridge(): void {
  if (desktopBridgeRegistered) {
    return;
  }

  desktopBridgeRegistered = true;

  ipcMain.handle("kodaclaw:get-runtime-config", async () => runtimeConfig);
  ipcMain.handle("kodaclaw:show-window", async () => {
    await showMainWindow();
  });
  ipcMain.on("kodaclaw:open-target", (_event, payload: DesktopLaunchTarget) => {
    if (!isDesktopLaunchTarget(payload)) {
      return;
    }

    void dispatchLaunchTarget(payload);
  });
}

async function runSmokeValidation(): Promise<void> {
  if (shouldCaptureNotificationSmoke()) {
    await pollNotificationsOnce();
  }

  await delay(SMOKE_EXIT_DELAY_MS);
  console.log(JSON.stringify({
    smokeMode: true,
    gatewayStatus: gatewayState.status,
    gatewayLifecycleMode: runtimeConfig.gatewayLifecycleMode,
    gatewayUrl: runtimeConfig.gatewayUrl,
    appVersion: runtimeConfig.appVersion,
    releaseChannel: runtimeConfig.releaseChannel,
    initialTarget: runtimeConfig.initialTarget,
    childPid: gatewayState.childPid,
    trayReady: tray !== null,
    shortcutRegistered: registeredShortcut,
    capturedNotifications,
  }));

  isQuitting = true;
  await stopManagedGateway();
  app.exit(gatewayState.status === "healthy" ? 0 : 1);
}

app.on("before-quit", (event) => {
  isQuitting = true;
  stopNotificationPolling();

  if (!managedGatewayProcess || quitCleanupStarted) {
    return;
  }

  quitCleanupStarted = true;
  event.preventDefault();
  void stopManagedGateway().finally(() => {
    app.quit();
  });
});

app.on("will-quit", () => {
  globalShortcut.unregisterAll();
});

app.whenReady().then(async () => {
  registerDesktopBridge();

  // Handle quick approve/reject actions triggered from macOS notification banners.
  // Note: "notification-action" is a valid Electron app event on macOS but is not
  // included in the Electron 31 TypeScript overloads, hence the cast below.
  (app as NodeJS.EventEmitter).on(
    "notification-action",
    (_event: unknown, notification: Notification, action: { text: string }) => {
      void handleApprovalNotificationAction(notification, action.text);
    },
  );

  await initializeGatewayRuntime();
  await createMainWindow();
  ensureTray();
  registerGlobalShortcut();
  await flushPendingExternalLaunchTargets();

  if (isSmokeMode()) {
    await runSmokeValidation();
    return;
  }

  app.on("activate", async () => {
    await showMainWindow();
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    app.quit();
  }
});
