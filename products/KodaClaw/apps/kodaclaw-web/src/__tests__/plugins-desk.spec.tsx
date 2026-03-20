import "@testing-library/jest-dom";
import React from "react";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PluginsDesk } from "../components/PluginsDesk";
import { renderWithI18n } from "./test-utils";

const originalFetch = global.fetch;

function jsonResponse(payload: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 200 ? "OK" : "ERROR",
    json: async () => payload,
    text: async () => JSON.stringify(payload),
  } as Response;
}

describe("PluginsDesk", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.restoreAllMocks();
    global.fetch = originalFetch;
  });

  it("loads plugin list/detail/logs and renders permission summary", async () => {
    const plugins = [
      {
        id: "plugin.alpha",
        name: "Alpha Toolchain",
        version: "0.1.0",
        types: ["Tool"],
        installSource: "LocalDirectory",
        trustState: "Signed",
        enabled: true,
        runtimeState: "Running",
        rootPath: "/tmp/plugins/plugin.alpha",
        updatedAt: "2026-03-19T08:00:00Z",
        lastError: null,
      },
      {
        id: "plugin.beta",
        name: "Beta Bridge",
        version: "0.2.0",
        types: ["Tool", "Channel"],
        installSource: "Bundled",
        trustState: "Untrusted",
        enabled: false,
        runtimeState: "Stopped",
        rootPath: "/tmp/plugins/plugin.beta",
        updatedAt: "2026-03-19T08:05:00Z",
        lastError: null,
      },
    ];

    const details: Record<string, unknown> = {
      "plugin.alpha": {
        record: {
          id: "plugin.alpha",
          manifest: {
            id: "plugin.alpha",
            name: "Alpha Toolchain",
            version: "0.1.0",
            types: ["Tool"],
            runtime: {
              transport: "Stdio",
              command: "fixture",
            },
            permissions: {
              network: true,
              background: true,
              secrets: ["alpha.token"],
            },
            capabilities: {
              tools: ["echo"],
            },
          },
          installSource: "LocalDirectory",
          rootPath: "/tmp/plugins/plugin.alpha",
          trustState: "Signed",
          enabled: true,
          runtimeState: "Running",
          discoveredAt: "2026-03-19T07:00:00Z",
          installedAt: "2026-03-19T07:00:00Z",
          updatedAt: "2026-03-19T08:00:00Z",
          lastStartedAt: "2026-03-19T07:30:00Z",
          lastStoppedAt: null,
          lastHealthAt: "2026-03-19T08:00:00Z",
          restartCount: 1,
          lastError: null,
          trustEvidence: {
            source: "SignatureSidecar",
            verificationState: "Verified",
            summary: "Signature sidecar matched the current manifest and package digests.",
            verifiedAt: "2026-03-19T08:00:00Z",
            manifestDigestSha256: "abc1234567890defabc1234567890def",
            packageDigestSha256: "fed0987654321cbafed0987654321cba",
            signer: "Fixture Publisher",
            signatureFilePath: "/tmp/plugins/plugin.alpha/plugin.signature.json",
          },
        },
        permissionSummary: {
          highRiskReasons: ["Requests network access."],
          mediumRiskReasons: ["Can run in background."],
          hasHighRisk: true,
        },
        healthSummary: {
          status: "healthy",
          message: "healthcheck passed",
          lastHealthAt: "2026-03-19T08:00:00Z",
          restartCount: 1,
          isHealthy: true,
        },
        availableTools: ["mcp__plugin.alpha__scan", "mcp__plugin.alpha__sync"],
      },
      "plugin.beta": {
        record: {
          id: "plugin.beta",
          manifest: {
            id: "plugin.beta",
            name: "Beta Bridge",
            version: "0.2.0",
            types: ["Tool", "Channel"],
            runtime: {
              transport: "Stdio",
              command: "fixture",
            },
            permissions: {
              filesystem: ["workspace/plugins/beta"],
            },
            capabilities: {
              tools: ["echo"],
            },
          },
          installSource: "Bundled",
          rootPath: "/tmp/plugins/plugin.beta",
          trustState: "Untrusted",
          enabled: false,
          runtimeState: "Stopped",
          discoveredAt: "2026-03-19T07:10:00Z",
          installedAt: "2026-03-19T07:10:00Z",
          updatedAt: "2026-03-19T08:05:00Z",
          lastStartedAt: null,
          lastStoppedAt: null,
          lastHealthAt: null,
          restartCount: 0,
          lastError: null,
          trustEvidence: {
            source: "LocalDigest",
            verificationState: "DigestOnly",
            summary: "Local digest captured. No signature sidecar was found.",
            verifiedAt: "2026-03-19T08:05:00Z",
            manifestDigestSha256: "beta-manifest",
            packageDigestSha256: "beta-package",
          },
        },
        permissionSummary: {
          highRiskReasons: [],
          mediumRiskReasons: ["Requests 1 filesystem scope(s)."],
          hasHighRisk: false,
        },
        healthSummary: {
          status: "stopped",
          message: "plugin is stopped",
          lastHealthAt: null,
          restartCount: 0,
          isHealthy: false,
        },
        availableTools: ["mcp__plugin.beta__echo"],
      },
    };

    const logs: Record<string, unknown> = {
      "plugin.alpha": [
        {
          entryId: "log-alpha-1",
          pluginId: "plugin.alpha",
          level: "info",
          source: "plugin.host",
          message: "Plugin started successfully.",
          timestamp: "2026-03-19T08:00:00Z",
          payloadJson: null,
        },
      ],
      "plugin.beta": [
        {
          entryId: "log-beta-1",
          pluginId: "plugin.beta",
          level: "warning",
          source: "plugin.health",
          message: "Plugin is stopped.",
          timestamp: "2026-03-19T08:06:00Z",
          payloadJson: null,
        },
      ],
    };

    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url =
        typeof input === "string"
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url;

      if (url.endsWith("/api/plugins?limit=80")) {
        return jsonResponse({ items: plugins });
      }

      if (url.endsWith("/api/plugins/plugin.alpha")) {
        return jsonResponse(details["plugin.alpha"]);
      }

      if (url.endsWith("/api/plugins/plugin.beta")) {
        return jsonResponse(details["plugin.beta"]);
      }

      if (url.includes("/api/plugins/plugin.alpha/logs")) {
        return jsonResponse(logs["plugin.alpha"]);
      }

      if (url.includes("/api/plugins/plugin.beta/logs")) {
        return jsonResponse(logs["plugin.beta"]);
      }

      if (init?.method === "POST" && url.endsWith("/api/plugins/discover")) {
        return jsonResponse({ items: plugins });
      }

      throw new Error(`Unexpected request: ${url}`);
    });

    renderWithI18n(<PluginsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("plugins-list")).toHaveTextContent("Alpha Toolchain");
    });
    await waitFor(() => {
      expect(screen.getByTestId("plugin-tools")).toHaveTextContent("mcp__plugin.alpha__scan");
    });
    expect(screen.getByTestId("plugin-permissions")).toHaveTextContent("Requests network access.");
    expect(screen.getByTestId("plugin-logs")).toHaveTextContent("Plugin started successfully.");
    expect(screen.getByTestId("plugin-trust-evidence")).toHaveTextContent("Fixture Publisher");
    expect(screen.getByTestId("plugin-trust-evidence")).toHaveTextContent("签名证据与当前插件内容一致。");

    const user = userEvent.setup();
    await user.click(screen.getByTestId("plugin-item-plugin.beta"));

    await waitFor(() => {
      expect(screen.getByTestId("plugin-detail")).toHaveTextContent("plugin.beta");
    });
    expect(screen.getByTestId("plugin-tools")).toHaveTextContent("mcp__plugin.beta__echo");
    expect(screen.getByTestId("plugin-logs")).toHaveTextContent("Plugin is stopped.");
  });

  it("applies trust/enable/start/stop actions with refresh", async () => {
    let plugin = {
      id: "plugin.beta",
      name: "Beta Bridge",
      version: "0.2.0",
      types: ["Tool"],
      installSource: "Bundled",
      trustState: "Untrusted",
      enabled: false,
      runtimeState: "Stopped",
      rootPath: "/tmp/plugins/plugin.beta",
      updatedAt: "2026-03-19T08:05:00Z",
      lastError: null,
      restartCount: 0,
    };
    const actions: string[] = [];

    function buildDetail(): Record<string, unknown> {
      return {
        record: {
          id: plugin.id,
          manifest: {
            id: plugin.id,
            name: plugin.name,
            version: plugin.version,
            types: plugin.types,
            runtime: {
              transport: "Stdio",
              command: "fixture",
            },
            permissions: {},
            capabilities: {
              tools: ["echo"],
            },
          },
          installSource: plugin.installSource,
          rootPath: plugin.rootPath,
          trustState: plugin.trustState,
          enabled: plugin.enabled,
          runtimeState: plugin.runtimeState,
          discoveredAt: "2026-03-19T07:10:00Z",
          installedAt: "2026-03-19T07:10:00Z",
          updatedAt: plugin.updatedAt,
          lastStartedAt: plugin.runtimeState === "Running" ? "2026-03-19T08:00:00Z" : null,
          lastStoppedAt: plugin.runtimeState === "Stopped" ? "2026-03-19T08:00:00Z" : null,
          lastHealthAt: null,
          restartCount: plugin.restartCount,
          lastError: null,
          trustEvidence:
            plugin.trustState === "Signed"
              ? {
                  source: "SignatureSidecar",
                  verificationState: "Verified",
                  summary: "Signature sidecar matched the current manifest and package digests.",
                  verifiedAt: "2026-03-19T08:06:00Z",
                  manifestDigestSha256: "beta-manifest",
                  packageDigestSha256: "beta-package",
                  signer: "Fixture Publisher",
                  signatureFilePath: "/tmp/plugins/plugin.beta/plugin.signature.json",
                }
              : {
                  source: "LocalDigest",
                  verificationState: "DigestOnly",
                  summary: "Local digest captured. No signature sidecar was found.",
                  verifiedAt: "2026-03-19T08:05:00Z",
                  manifestDigestSha256: "beta-manifest",
                  packageDigestSha256: "beta-package",
                },
        },
        permissionSummary: {
          highRiskReasons: [],
          mediumRiskReasons: [],
          hasHighRisk: false,
        },
        healthSummary: {
          status: plugin.runtimeState === "Running" ? "healthy" : "stopped",
          message: null,
          lastHealthAt: null,
          restartCount: plugin.restartCount,
          isHealthy: plugin.runtimeState === "Running",
        },
        availableTools: ["mcp__plugin.beta__echo"],
      };
    }

    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url =
        typeof input === "string"
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url;

      if (url.endsWith("/api/plugins?limit=80")) {
        return jsonResponse({ items: [plugin] });
      }

      if (url.endsWith("/api/plugins/plugin.beta")) {
        return jsonResponse(buildDetail());
      }

      if (url.includes("/api/plugins/plugin.beta/logs")) {
        return jsonResponse([
          {
            entryId: "log-beta-1",
            pluginId: "plugin.beta",
            level: "info",
            source: "plugin.host",
            message: "Plugin action observed.",
            timestamp: "2026-03-19T08:06:00Z",
            payloadJson: null,
          },
        ]);
      }

      if (init?.method === "POST" && url.endsWith("/api/plugins/plugin.beta/trust")) {
        actions.push("trust");
        plugin = { ...plugin, trustState: "Signed", updatedAt: "2026-03-19T08:06:00Z" };
        return jsonResponse(buildDetail(), 200);
      }

      if (init?.method === "POST" && url.endsWith("/api/plugins/plugin.beta/enable")) {
        actions.push("enable");
        plugin = { ...plugin, enabled: true, updatedAt: "2026-03-19T08:07:00Z" };
        return jsonResponse(buildDetail(), 200);
      }

      if (init?.method === "POST" && url.endsWith("/api/plugins/plugin.beta/start")) {
        actions.push("start");
        plugin = { ...plugin, runtimeState: "Running", updatedAt: "2026-03-19T08:08:00Z" };
        return jsonResponse(buildDetail(), 200);
      }

      if (init?.method === "POST" && url.endsWith("/api/plugins/plugin.beta/stop")) {
        actions.push("stop");
        plugin = { ...plugin, runtimeState: "Stopped", updatedAt: "2026-03-19T08:09:00Z" };
        return jsonResponse(buildDetail(), 200);
      }

      throw new Error(`Unexpected request: ${url}`);
    });

    renderWithI18n(<PluginsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("plugin-item-plugin.beta")).toBeInTheDocument();
    });

    const user = userEvent.setup();
    await user.click(screen.getByTestId("plugin-action-trust"));
    await waitFor(() => {
      expect(actions).toContain("trust");
    });
    await waitFor(() => {
      expect(screen.getByTestId("plugins-note")).toHaveTextContent("插件信任已授予，并验证了签名证据。");
    });

    await user.click(screen.getByTestId("plugin-action-enable"));
    await waitFor(() => {
      expect(actions).toContain("enable");
    });

    await user.click(screen.getByTestId("plugin-action-start"));
    await waitFor(() => {
      expect(actions).toContain("start");
    });
    await waitFor(() => {
      expect(screen.getByTestId("plugin-enabled-chip")).toHaveTextContent("已启用");
    });

    await user.click(screen.getByTestId("plugin-action-stop"));
    await waitFor(() => {
      expect(actions).toContain("stop");
    });
    await waitFor(() => {
      expect(screen.getByTestId("plugin-detail-core")).toHaveTextContent("Stopped");
    });
  });

  it("renders error state when plugin query fails", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse({ message: "plugins unavailable" }, 500));

    renderWithI18n(<PluginsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("plugins-error")).toHaveTextContent("plugins unavailable");
    });
  });
});
