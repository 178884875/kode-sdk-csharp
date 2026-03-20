import { expect, test } from "@playwright/test";

test("KC-0407 plugin desk: list/detail/lifecycle/log workflow", async ({ page }) => {
  const context = page.context();
  const pageErrors: string[] = [];
  page.on("pageerror", (error) => {
    pageErrors.push(error.message);
    console.log(`pageerror: ${error.message}`);
  });

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
      rootPath: "/tmp/.kodaclaw/workspace/plugins/plugin.alpha",
      updatedAt: "2026-03-19T08:00:00.000Z",
      lastError: null,
      restartCount: 1,
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
      rootPath: "/tmp/.kodaclaw/workspace/plugins/plugin.beta",
      updatedAt: "2026-03-19T08:05:00.000Z",
      lastError: null,
      restartCount: 0,
    },
  ];

  const logsByPlugin: Record<string, Array<Record<string, unknown>>> = {
    "plugin.alpha": [
      {
        entryId: "log-alpha-1",
        pluginId: "plugin.alpha",
        level: "info",
        source: "plugin.host",
        message: "Plugin started successfully.",
        timestamp: "2026-03-19T08:00:00.000Z",
      },
    ],
    "plugin.beta": [
      {
        entryId: "log-beta-1",
        pluginId: "plugin.beta",
        level: "warning",
        source: "plugin.health",
        message: "Plugin is currently stopped.",
        timestamp: "2026-03-19T08:05:00.000Z",
      },
    ],
  };

  const actions: string[] = [];

  function buildPluginDetail(pluginId: string): Record<string, unknown> {
    const plugin = plugins.find((item) => item.id === pluginId);
    if (!plugin) {
      throw new Error(`Missing plugin fixture: ${pluginId}`);
    }

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
          permissions: plugin.id === "plugin.alpha"
            ? {
                network: true,
                background: true,
                secrets: ["alpha.token"],
              }
            : {
                filesystem: ["workspace/plugins/plugin.beta"],
              },
          capabilities: {
            tools: plugin.id === "plugin.alpha"
              ? ["scan"]
              : ["echo"],
          },
        },
        installSource: plugin.installSource,
        rootPath: plugin.rootPath,
        trustState: plugin.trustState,
        enabled: plugin.enabled,
        runtimeState: plugin.runtimeState,
        discoveredAt: "2026-03-19T07:00:00.000Z",
        installedAt: "2026-03-19T07:00:00.000Z",
        updatedAt: plugin.updatedAt,
        lastStartedAt: plugin.runtimeState === "Running" ? "2026-03-19T07:30:00.000Z" : null,
        lastStoppedAt: plugin.runtimeState === "Stopped" ? "2026-03-19T07:55:00.000Z" : null,
        lastHealthAt: "2026-03-19T08:06:00.000Z",
        restartCount: plugin.restartCount,
        lastError: plugin.lastError,
        trustEvidence: plugin.trustState === "Signed"
          ? {
              source: "SignatureSidecar",
              verificationState: "Verified",
              summary: "Signature sidecar matched the current manifest and package digests.",
              verifiedAt: "2026-03-19T08:06:00.000Z",
              manifestDigestSha256: "alpha-manifest-digest",
              packageDigestSha256: "alpha-package-digest",
              signer: "Fixture Publisher",
              signatureFilePath: "/tmp/.kodaclaw/workspace/plugins/plugin.alpha/plugin.signature.json",
            }
          : {
              source: "LocalDigest",
              verificationState: "DigestOnly",
              summary: "Local digest captured. No signature sidecar was found.",
              verifiedAt: "2026-03-19T08:06:00.000Z",
              manifestDigestSha256: "beta-manifest-digest",
              packageDigestSha256: "beta-package-digest",
            },
      },
      permissionSummary: {
        highRiskReasons: plugin.id === "plugin.alpha" ? ["Requests network access."] : [],
        mediumRiskReasons: plugin.id === "plugin.alpha" ? ["Can run in background."] : ["Requests filesystem scope."],
        hasHighRisk: plugin.id === "plugin.alpha",
      },
      healthSummary: {
        status: plugin.runtimeState === "Running" ? "healthy" : "stopped",
        message: plugin.runtimeState === "Running" ? "healthcheck passed" : "plugin stopped",
        lastHealthAt: "2026-03-19T08:06:00.000Z",
        restartCount: plugin.restartCount,
        isHealthy: plugin.runtimeState === "Running",
      },
      availableTools: plugin.id === "plugin.alpha"
        ? ["mcp__plugin.alpha__scan"]
        : ["mcp__plugin.beta__echo"],
    };
  }

  await context.route("**/api/system/health", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        name: "KodaClaw Gateway",
        status: "healthy",
        mode: "Normal",
      }),
    });
  });

  await context.route("**/api/system/bootstrap-state", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        workspaceRootPath: "/tmp/.kodaclaw-kc0407",
        workspaceVersion: 1,
        workspaceInitialized: true,
        requiresBootstrap: false,
        activeMainSessionId: "main-kc0407",
        mode: "Normal",
      }),
    });
  });

  await context.route("**/api/plugins?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: plugins.map((item) => ({
          id: item.id,
          name: item.name,
          version: item.version,
          types: item.types,
          installSource: item.installSource,
          trustState: item.trustState,
          enabled: item.enabled,
          runtimeState: item.runtimeState,
          rootPath: item.rootPath,
          updatedAt: item.updatedAt,
          lastError: item.lastError,
        })),
      }),
    });
  });

  await context.route("**/api/plugins/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const afterPrefix = path.split("/api/plugins/")[1] ?? "";
    const [pluginId, action] = afterPrefix.split("/");

    if (action === "logs") {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(logsByPlugin[pluginId] ?? []),
      });
      return;
    }

    if (request.method() === "GET") {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(buildPluginDetail(pluginId)),
      });
      return;
    }

    if (request.method() === "POST" && action) {
      actions.push(action);
      const target = plugins.find((item) => item.id === pluginId);
      if (!target) {
        await route.fulfill({ status: 404 });
        return;
      }

      if (action === "trust") {
        target.trustState = "Signed";
      }
      if (action === "enable") {
        target.enabled = true;
      }
      if (action === "disable") {
        target.enabled = false;
      }
      if (action === "start") {
        target.runtimeState = "Running";
      }
      if (action === "stop") {
        target.runtimeState = "Stopped";
      }
      target.updatedAt = "2026-03-19T08:15:00.000Z";

      logsByPlugin[pluginId] = [
        {
          entryId: `log-${pluginId}-${action}`,
          pluginId,
          level: "info",
          source: "plugin.host",
          message: `Action applied: ${action}.`,
          timestamp: "2026-03-19T08:15:00.000Z",
        },
      ];

      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(buildPluginDetail(pluginId)),
      });
      return;
    }

    await route.fulfill({ status: 405 });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  await expect(page.getByTestId("desk-tab-plugins")).toBeVisible();
  await page.getByTestId("desk-tab-plugins").click();

  // KC-1503: V2 shell data-kc-view="plugins" is stable after navigation
  await expect(page.locator('[data-kc-view="plugins"]')).toBeVisible();
  await expect(page.getByTestId("plugins-desk")).toBeVisible();
  await expect(page.getByTestId("plugins-list")).toContainText("Alpha Toolchain");
  await expect(page.getByTestId("plugins-list")).toContainText("Beta Bridge");

  await page.getByTestId("plugin-item-plugin.beta").click();
  await expect(page.getByTestId("plugin-detail")).toContainText("plugin.beta");
  await expect(page.getByTestId("plugin-logs")).toContainText("Plugin is currently stopped.");
  await expect(page.getByTestId("plugin-logs-link")).toHaveAttribute("href", /\/api\/plugins\/plugin.beta\/logs\?limit=200$/);
  await expect(page.getByTestId("plugin-trust-evidence")).toContainText("Local digest captured");

  await expect(page.getByTestId("plugin-action-trust")).toBeEnabled();
  await page.getByTestId("plugin-action-trust").click();
  await expect.poll(() => actions.includes("trust")).toBeTruthy();
  await expect(page.getByTestId("plugins-note")).toContainText("插件信任已授予，并验证了签名证据。");
  await expect(page.getByTestId("plugin-trust-evidence")).toContainText("Fixture Publisher");
  await expect(page.getByTestId("plugin-detail-core")).toContainText("Signed");

  await expect(page.getByTestId("plugin-action-enable")).toBeEnabled();
  await page.getByTestId("plugin-action-enable").click();
  await expect.poll(() => actions.includes("enable")).toBeTruthy();

  await expect(page.getByTestId("plugin-action-start")).toBeEnabled();
  await page.getByTestId("plugin-action-start").click();
  await expect.poll(() => actions.includes("start")).toBeTruthy();
  await expect(page.getByTestId("plugin-detail-core")).toContainText("Running");

  await expect(page.getByTestId("plugin-action-stop")).toBeEnabled();
  await page.getByTestId("plugin-action-stop").click();
  await expect.poll(() => actions.includes("stop")).toBeTruthy();
  await expect(page.getByTestId("plugin-detail-core")).toContainText("Stopped");
  expect(pageErrors).toEqual([]);
});
