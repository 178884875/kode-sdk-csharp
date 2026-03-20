function trim(value: string | undefined): string | undefined {
  const next = value?.trim();
  return next ? next : undefined;
}

export type DesktopDeskId =
  | "chat"
  | "inbox"
  | "sessions"
  | "models"
  | "automations"
  | "channels"
  | "plugins"
  | "canvas";

export type DesktopGatewayLifecycleMode = "AttachOnly" | "ManagedChild";
export type UpdateReleaseChannel = "Stable" | "Preview" | "Nightly" | "Custom";

export interface DesktopLaunchTarget {
  desk: DesktopDeskId;
  entityId?: string | null;
  route?: string | null;
  reason?: string | null;
}

export interface DesktopRuntimeConfig {
  gatewayUrl: string;
  gatewayToken: string;
  platform: string;
  appVersion: string;
  releaseChannel: UpdateReleaseChannel;
  desktopMode: boolean;
  initialTarget: DesktopLaunchTarget | null;
  gatewayLifecycleMode: DesktopGatewayLifecycleMode | null;
}

export interface DesktopShellBridge {
  getRuntimeConfig(): Promise<DesktopRuntimeConfig | Partial<DesktopRuntimeConfig>>;
  openTarget?(target: DesktopLaunchTarget): Promise<void> | void;
  onLaunchTarget?(listener: (target: DesktopLaunchTarget) => void): (() => void) | void;
  showWindow?(): Promise<void> | void;
}

declare global {
  interface Window {
    kodaClawDesktop?: DesktopShellBridge;
  }
}

const fallbackRuntimeConfig = Object.freeze<DesktopRuntimeConfig>({
  gatewayUrl: trim(import.meta.env.VITE_KODACLAW_GATEWAY_URL) ?? "",
  gatewayToken: trim(import.meta.env.VITE_KODACLAW_GATEWAY_TOKEN) ?? "",
  platform: "browser",
  appVersion: trim(import.meta.env.VITE_KODACLAW_APP_VERSION) ?? "",
  releaseChannel: "Stable",
  desktopMode: false,
  initialTarget: null,
  gatewayLifecycleMode: null,
});

let runtimeConfig: DesktopRuntimeConfig = fallbackRuntimeConfig;
const launchTargetListeners = new Set<(target: DesktopLaunchTarget) => void>();
let launchTargetSubscriptionCleanup: (() => void) | null = null;

function isDesktopDeskId(value: unknown): value is DesktopDeskId {
  return value === "chat" ||
    value === "inbox" ||
    value === "sessions" ||
    value === "models" ||
    value === "automations" ||
    value === "channels" ||
    value === "plugins" ||
    value === "canvas";
}

function normalizeLaunchTarget(value: unknown): DesktopLaunchTarget | null {
  if (!value || typeof value !== "object") {
    return null;
  }

  const candidate = value as Partial<DesktopLaunchTarget>;
  if (!isDesktopDeskId(candidate.desk)) {
    return null;
  }

  return {
    desk: candidate.desk,
    entityId: typeof candidate.entityId === "string" ? candidate.entityId : null,
    route: typeof candidate.route === "string" ? candidate.route : null,
    reason: typeof candidate.reason === "string" ? candidate.reason : null,
  };
}

function normalizeLifecycleMode(value: unknown): DesktopGatewayLifecycleMode | null {
  return value === "AttachOnly" || value === "ManagedChild" ? value : null;
}

function normalizeReleaseChannel(value: unknown): UpdateReleaseChannel {
  return value === "Preview" || value === "Nightly" || value === "Custom" ? value : "Stable";
}

function normalizeRuntimeConfig(value?: DesktopRuntimeConfig | Partial<DesktopRuntimeConfig> | null): DesktopRuntimeConfig {
  return {
    gatewayUrl: trim(value?.gatewayUrl) ?? fallbackRuntimeConfig.gatewayUrl,
    gatewayToken: trim(value?.gatewayToken) ?? fallbackRuntimeConfig.gatewayToken,
    platform: trim(value?.platform) ?? fallbackRuntimeConfig.platform,
    appVersion: trim(value?.appVersion) ?? fallbackRuntimeConfig.appVersion,
    releaseChannel: normalizeReleaseChannel(value?.releaseChannel),
    desktopMode: value?.desktopMode === true,
    initialTarget: normalizeLaunchTarget(value?.initialTarget),
    gatewayLifecycleMode: normalizeLifecycleMode(value?.gatewayLifecycleMode),
  };
}

function clearLaunchTargetSubscription(): void {
  launchTargetSubscriptionCleanup?.();
  launchTargetSubscriptionCleanup = null;
}

function publishLaunchTarget(target: DesktopLaunchTarget): void {
  for (const listener of launchTargetListeners) {
    listener(target);
  }
}

export async function initializeRuntimeConfig(): Promise<DesktopRuntimeConfig> {
  runtimeConfig = fallbackRuntimeConfig;
  clearLaunchTargetSubscription();

  if (typeof window === "undefined") {
    return runtimeConfig;
  }

  const bridge = window.kodaClawDesktop;
  if (!bridge?.getRuntimeConfig) {
    return runtimeConfig;
  }

  runtimeConfig = normalizeRuntimeConfig(await bridge.getRuntimeConfig());

  if (bridge.onLaunchTarget) {
    const cleanup = bridge.onLaunchTarget((nextTarget) => {
      const normalized = normalizeLaunchTarget(nextTarget);
      if (normalized) {
        publishLaunchTarget(normalized);
      }
    });

    if (typeof cleanup === "function") {
      launchTargetSubscriptionCleanup = cleanup;
    }
  }

  return runtimeConfig;
}

export function subscribeDesktopLaunchTargets(listener: (target: DesktopLaunchTarget) => void): () => void {
  launchTargetListeners.add(listener);
  return () => {
    launchTargetListeners.delete(listener);
  };
}

export function getRuntimeConfig(): DesktopRuntimeConfig {
  return runtimeConfig;
}

export function getGatewayUrl(): string {
  return runtimeConfig.gatewayUrl;
}

export function getGatewayToken(): string {
  return runtimeConfig.gatewayToken;
}

export function getDesktopAppVersion(): string {
  return runtimeConfig.appVersion;
}

export function getDesktopReleaseChannel(): UpdateReleaseChannel {
  return runtimeConfig.releaseChannel;
}

export function getInitialLaunchTarget(): DesktopLaunchTarget | null {
  return runtimeConfig.initialTarget;
}

export function isDesktopMode(): boolean {
  return runtimeConfig.desktopMode;
}

export function resolveGatewayPath(path: string): string {
  const gatewayUrl = getGatewayUrl();
  return gatewayUrl ? `${gatewayUrl}${path}` : path;
}

export async function openDesktopLaunchTarget(target: DesktopLaunchTarget): Promise<void> {
  await window.kodaClawDesktop?.openTarget?.(target);
}

export function __resetRuntimeConfigForTests(): void {
  clearLaunchTargetSubscription();
  runtimeConfig = fallbackRuntimeConfig;
  launchTargetListeners.clear();
}
