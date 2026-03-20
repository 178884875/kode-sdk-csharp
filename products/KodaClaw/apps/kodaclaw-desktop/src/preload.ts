import { contextBridge, ipcRenderer } from "electron";
import type { DesktopLaunchTarget, DesktopRuntimeConfig } from "./desktop-shell-types";

type LaunchTargetListener = (target: DesktopLaunchTarget) => void;

const FALLBACK_RUNTIME_CONFIG: DesktopRuntimeConfig = {
  gatewayUrl: null,
  gatewayToken: null,
  platform: process.platform,
  appVersion: "0.0.0",
  releaseChannel: "Stable",
  desktopMode: true,
  initialTarget: null,
  gatewayLifecycleMode: "AttachOnly",
};

contextBridge.exposeInMainWorld("kodaClawDesktop", {
  async getRuntimeConfig(): Promise<DesktopRuntimeConfig> {
    const runtimeConfig = await ipcRenderer.invoke("kodaclaw:get-runtime-config");
    return (runtimeConfig as DesktopRuntimeConfig | null | undefined) ?? FALLBACK_RUNTIME_CONFIG;
  },
  onLaunchTarget(listener: LaunchTargetListener): () => void {
    const channel = "kodaclaw:launch-target";
    const wrappedListener = (_event: unknown, target: DesktopLaunchTarget) => {
      listener(target);
    };

    ipcRenderer.on(channel, wrappedListener);
    return () => {
      ipcRenderer.removeListener(channel, wrappedListener);
    };
  },
  openTarget(target: DesktopLaunchTarget): void {
    ipcRenderer.send("kodaclaw:open-target", target);
  },
  showWindow(): Promise<void> {
    return ipcRenderer.invoke("kodaclaw:show-window");
  },
});
