export type GatewayLifecycleMode = "AttachOnly" | "ManagedChild";
export type UpdateReleaseChannel = "Stable" | "Preview" | "Nightly" | "Custom";

export type DesktopLaunchTarget = {
  desk: string;
  entityId?: string;
  route?: string;
  reason?: string;
};

export type DesktopRuntimeConfig = {
  gatewayUrl: string | null;
  gatewayToken: string | null;
  platform: NodeJS.Platform;
  appVersion: string;
  releaseChannel: UpdateReleaseChannel;
  desktopMode: boolean;
  initialTarget: DesktopLaunchTarget | null;
  gatewayLifecycleMode: GatewayLifecycleMode;
};

export function isDesktopLaunchTarget(value: unknown): value is DesktopLaunchTarget {
  if (!value || typeof value !== "object") {
    return false;
  }

  const candidate = value as DesktopLaunchTarget;
  return typeof candidate.desk === "string" && candidate.desk.trim().length > 0;
}
