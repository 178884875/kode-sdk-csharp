import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  __resetRuntimeConfigForTests,
  getDesktopAppVersion,
  getDesktopReleaseChannel,
  getGatewayToken,
  getGatewayUrl,
  getInitialLaunchTarget,
  initializeRuntimeConfig,
  resolveGatewayPath,
  subscribeDesktopLaunchTargets,
  type DesktopLaunchTarget,
} from "../lib/config";

describe("desktop runtime config", () => {
  beforeEach(() => {
    __resetRuntimeConfigForTests();
    delete window.kodaClawDesktop;
  });

  afterEach(() => {
    __resetRuntimeConfigForTests();
    delete window.kodaClawDesktop;
    vi.restoreAllMocks();
  });

  it("prefers desktop bridge config and forwards launch target events", async () => {
    let bridgeListener: ((target: DesktopLaunchTarget) => void) | null = null;

    window.kodaClawDesktop = {
      getRuntimeConfig: vi.fn().mockResolvedValue({
        gatewayUrl: "http://127.0.0.1:5076",
        gatewayToken: "desktop-token",
        platform: "darwin",
        appVersion: "0.1.0",
        releaseChannel: "Preview",
        desktopMode: true,
        gatewayLifecycleMode: "ManagedChild",
        initialTarget: {
          desk: "channels",
          reason: "bootstrap",
        },
      }),
      onLaunchTarget: (listener) => {
        bridgeListener = listener;
        return () => {
          bridgeListener = null;
        };
      },
    };

    const forwardedTargets: DesktopLaunchTarget[] = [];
    const unsubscribe = subscribeDesktopLaunchTargets((target) => {
      forwardedTargets.push(target);
    });

    const runtimeConfig = await initializeRuntimeConfig();

    expect(runtimeConfig.desktopMode).toBe(true);
    expect(runtimeConfig.platform).toBe("darwin");
    expect(runtimeConfig.appVersion).toBe("0.1.0");
    expect(runtimeConfig.releaseChannel).toBe("Preview");
    expect(runtimeConfig.gatewayLifecycleMode).toBe("ManagedChild");
    expect(getDesktopAppVersion()).toBe("0.1.0");
    expect(getDesktopReleaseChannel()).toBe("Preview");
    expect(getGatewayUrl()).toBe("http://127.0.0.1:5076");
    expect(getGatewayToken()).toBe("desktop-token");
    expect(getInitialLaunchTarget()).toEqual({
      desk: "channels",
      entityId: null,
      route: null,
      reason: "bootstrap",
    });
    expect(resolveGatewayPath("/api/system/health")).toBe("http://127.0.0.1:5076/api/system/health");

    if (bridgeListener) {
      (bridgeListener as (target: DesktopLaunchTarget) => void)({
        desk: "inbox",
        reason: "notification",
      });
    }

    expect(forwardedTargets).toEqual([
      {
        desk: "inbox",
        entityId: null,
        route: null,
        reason: "notification",
      },
    ]);

    unsubscribe();
  });
});
