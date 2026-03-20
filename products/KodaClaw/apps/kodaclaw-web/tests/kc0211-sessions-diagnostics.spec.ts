import { expect, test } from "@playwright/test";

test("KC-0211 sessions/diagnostics: session switch updates detail and timeline", async ({
  page,
}) => {
  const context = page.context();

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
        workspaceRootPath: "/tmp/.kodaclaw-kc0211",
        workspaceVersion: 1,
        workspaceInitialized: true,
        requiresBootstrap: false,
        activeMainSessionId: "main-001",
        mode: "Normal",
      }),
    });
  });

  await context.route("**/api/sessions?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        sessions: [
          {
            sessionId: "main-001",
            sessionKind: "Main",
            status: {
              isActiveMainSession: true,
              breakpointState: null,
              messageCount: 9,
              pendingApprovalCount: 1,
            },
            createdAt: "2026-03-18T10:00:00.0000000+00:00",
            lastEventAt: "2026-03-18T10:01:00.0000000+00:00",
          },
          {
            sessionId: "auto-002",
            sessionKind: "Automation",
            status: {
              isActiveMainSession: false,
              breakpointState: "paused",
              messageCount: 6,
              pendingApprovalCount: 2,
            },
            createdAt: "2026-03-18T09:00:00.0000000+00:00",
            lastEventAt: "2026-03-18T09:03:00.0000000+00:00",
          },
        ],
      }),
    });
  });

  await context.route("**/api/sessions/*", async (route) => {
    const sessionId = route.request().url().split("/api/sessions/")[1] ?? "unknown";
    const isMain = sessionId === "main-001";

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        sessionId,
        sessionKind: isMain ? "Main" : "Automation",
        status: {
          isActiveMainSession: isMain,
          breakpointState: isMain ? null : "paused",
          messageCount: isMain ? 9 : 6,
          pendingApprovalCount: isMain ? 1 : 2,
        },
        createdAt: "2026-03-18T09:00:00.0000000+00:00",
        lastEventAt: "2026-03-18T10:01:00.0000000+00:00",
        userMessageCount: 3,
        assistantMessageCount: 3,
        toolCallCount: 2,
        lastSfpIndex: 12,
        pendingApprovalCallIds: isMain ? ["call-main"] : ["call-auto"],
      }),
    });
  });

  await context.route("**/api/diagnostics/timeline?**", async (route) => {
    const url = new URL(route.request().url());
    const sessionId = url.searchParams.get("sessionId") ?? "unknown";
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        events: [
          {
            id: `evt-${sessionId}`,
            source: "gateway.diagnostics",
            eventType: "timeline.loaded",
            level: "info",
            message: `timeline-${sessionId}`,
            timestamp: "2026-03-18T10:01:20.0000000+00:00",
            sessionId,
            correlationId: "corr-kc0211",
          },
        ],
      }),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  await page.getByTestId("desk-tab-sessions").click();

  await expect(page.getByTestId("sessions-diagnostics-desk")).toBeVisible();
  await expect(page.getByTestId("session-detail")).toContainText("main-001");
  await expect(page.getByTestId("diagnostics-timeline")).toContainText("timeline-main-001");

  await page.getByTestId("session-select-auto-002").click();
  await expect(page.getByTestId("session-detail")).toContainText("auto-002");
  await expect(page.getByTestId("diagnostics-timeline")).toContainText("timeline-auto-002");
});

test("KC-0709 sessions/diagnostics: export bundle carries selected session and desktop context", async ({
  page,
}) => {
  await page.addInitScript(() => {
    window.kodaClawDesktop = {
      getRuntimeConfig: async () => ({
        gatewayUrl: "",
        gatewayToken: "",
        platform: "darwin",
        appVersion: "0.1.0",
        releaseChannel: "Stable",
        desktopMode: true,
        initialTarget: null,
        gatewayLifecycleMode: "ManagedChild",
      }),
    };
  });

  const context = page.context();
  let exportRequestBody: Record<string, unknown> | null = null;

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
        workspaceRootPath: "/tmp/.kodaclaw-kc0709",
        workspaceVersion: 1,
        workspaceInitialized: true,
        requiresBootstrap: false,
        activeMainSessionId: "main-001",
        mode: "Normal",
      }),
    });
  });

  await context.route("**/api/sessions?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        sessions: [
          {
            sessionId: "main-001",
            sessionKind: "Main",
            status: {
              isActiveMainSession: true,
              breakpointState: null,
              messageCount: 9,
              pendingApprovalCount: 1,
            },
            createdAt: "2026-03-18T10:00:00.0000000+00:00",
            lastEventAt: "2026-03-18T10:01:00.0000000+00:00",
          },
          {
            sessionId: "auto-002",
            sessionKind: "Automation",
            status: {
              isActiveMainSession: false,
              breakpointState: "paused",
              messageCount: 6,
              pendingApprovalCount: 2,
            },
            createdAt: "2026-03-18T09:00:00.0000000+00:00",
            lastEventAt: "2026-03-18T09:03:00.0000000+00:00",
          },
        ],
      }),
    });
  });

  await context.route("**/api/sessions/*", async (route) => {
    const sessionId = route.request().url().split("/api/sessions/")[1] ?? "unknown";
    const isMain = sessionId === "main-001";

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        sessionId,
        sessionKind: isMain ? "Main" : "Automation",
        status: {
          isActiveMainSession: isMain,
          breakpointState: isMain ? null : "paused",
          messageCount: isMain ? 9 : 6,
          pendingApprovalCount: isMain ? 1 : 2,
        },
        createdAt: "2026-03-18T09:00:00.0000000+00:00",
        lastEventAt: "2026-03-18T10:01:00.0000000+00:00",
        userMessageCount: 3,
        assistantMessageCount: 3,
        toolCallCount: 2,
        lastSfpIndex: 12,
        pendingApprovalCallIds: isMain ? ["call-main"] : ["call-auto"],
      }),
    });
  });

  await context.route("**/api/diagnostics/timeline?**", async (route) => {
    const url = new URL(route.request().url());
    const sessionId = url.searchParams.get("sessionId") ?? "unknown";
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        events: [
          {
            id: `evt-${sessionId}`,
            source: "gateway.diagnostics",
            eventType: "timeline.loaded",
            level: "info",
            message: `timeline-${sessionId}`,
            timestamp: "2026-03-18T10:01:20.0000000+00:00",
            sessionId,
            correlationId: "corr-kc0709",
          },
        ],
      }),
    });
  });

  await context.route("**/api/diagnostics/bundle-export", async (route) => {
    exportRequestBody = route.request().postDataJSON() as Record<string, unknown>;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        generatedAt: "2026-03-19T10:06:00Z",
        workspaceRootPath: "/tmp/.kodaclaw-kc0709",
        bundlePath:
          "/tmp/.kodaclaw-kc0709/cache/diagnostics/kodaclaw-diagnostic-bundle-20260319-100600.zip",
        manifest: {
          product: "KodaClaw",
          formatVersion: 1,
          generatedAt: "2026-03-19T10:06:00Z",
          archiveName: "kodaclaw-diagnostic-bundle-20260319-100600.zip",
          sourceWorkspaceRoot: "/tmp/.kodaclaw-kc0709",
          requestedSessionId: "auto-002",
          desktopContext: {
            desktopMode: true,
            platform: "darwin",
            appVersion: "0.1.0",
            releaseChannel: "Stable",
            gatewayLifecycleMode: "ManagedChild",
          },
          redactionSummary: {
            includesRawSecrets: false,
            includesMessageBodies: false,
            appliedRules: [
              "secret-like diagnostics attribute values are replaced with [REDACTED]",
            ],
            notes: [
              "Timeline export is scoped to session 'auto-002'.",
              "Desktop runtime context was supplied by the renderer.",
            ],
          },
          entries: [
            {
              path: "snapshot/diagnostics/timeline.json",
              sha256: "abc123",
              sizeBytes: 512,
              category: "diagnostics",
            },
          ],
          includes: ["diagnostics recent/timeline exports"],
          excludes: ["raw secrets and secret values"],
          notes: ["Desktop runtime context was supplied by the renderer."],
        },
      }),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  await page.getByTestId("desk-tab-sessions").click();

  await expect(page.getByTestId("sessions-diagnostics-desk")).toBeVisible();
  await page.getByTestId("session-select-auto-002").click();
  await expect(page.getByTestId("session-detail")).toContainText("auto-002");

  await page.getByTestId("diagnostic-bundle-export-button").click();

  await expect(page.getByTestId("diagnostic-bundle-export-note")).toContainText(
    "诊断包已为 auto-002 导出。",
  );
  await expect(page.getByTestId("diagnostic-bundle-export-result")).toContainText(
    "kodaclaw-diagnostic-bundle-20260319-100600.zip",
  );
  await expect(page.getByTestId("diagnostic-bundle-redaction-notes")).toContainText(
    "Timeline export is scoped to session 'auto-002'.",
  );

  expect(exportRequestBody).toEqual({
    sessionId: "auto-002",
    timelineLimit: 120,
    desktopContext: {
      desktopMode: true,
      platform: "darwin",
      appVersion: "0.1.0",
      releaseChannel: "Stable",
      gatewayLifecycleMode: "ManagedChild",
    },
  });
});
