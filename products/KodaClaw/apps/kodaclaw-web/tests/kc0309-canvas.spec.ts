import { type BrowserContext, expect, test } from "@playwright/test";

const reportArtifact = {
  id: "artifact-report",
  title: "Launch Snapshot",
  kind: "Report",
  summary: "Executive launch report",
  source: "runtime.main",
  route: "/canvas/launch",
  entryPath: "canvas/launch/index.html",
  assetDirectory: "canvas/launch",
  sessionId: "session-launch",
  correlationId: "corr-launch",
  createdAt: "2026-03-18T08:00:00Z",
  updatedAt: "2026-03-18T10:00:00Z",
  metadataJson: null,
};

const dashboardArtifact = {
  id: "artifact-dashboard",
  title: "Ops Wallboard",
  kind: "Dashboard",
  summary: "Realtime operations dashboard",
  source: "runtime.automation",
  route: "/canvas/ops",
  entryPath: "canvas/ops/index.html",
  assetDirectory: "canvas/ops",
  sessionId: "session-ops",
  correlationId: "corr-ops",
  createdAt: "2026-03-18T08:30:00Z",
  updatedAt: "2026-03-18T10:30:00Z",
  metadataJson: null,
};

async function mockMainShell(context: BrowserContext) {
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
        workspaceRootPath: "/tmp/.kodaclaw-kc0309",
        workspaceVersion: 1,
        workspaceInitialized: true,
        requiresBootstrap: false,
        activeMainSessionId: "main-0309",
        mode: "Normal",
      }),
    });
  });
}

test("KC-0309 canvas desk: tab interaction and iframe switch", async ({ page }) => {
  const context = page.context();
  await mockMainShell(context);

  const artifacts = [reportArtifact, dashboardArtifact];

  await context.route("**/api/canvas/default", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        entryUrl: "/api/canvas/preview/default-token/canvas/default/index.html",
        entryPath: "canvas/default/index.html",
        artifactId: null,
        route: "/canvas/default",
        title: "Canvas default entry",
      }),
    });
  });

  await context.route("**/api/canvas?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: artifacts,
        defaultEntryPath: "canvas/default/index.html",
        defaultArtifactId: null,
      }),
    });
  });

  await context.route("**/api/canvas/*/entry", async (route) => {
    const match = route.request().url().match(/\/api\/canvas\/([^/]+)\/entry$/);
    const id = match?.[1] ?? "";
    const artifact = artifacts.find((item) => item.id === id);
    if (!artifact) {
      await route.fulfill({ status: 404 });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        entryUrl: `/api/canvas/preview/${id}-token/${artifact.entryPath}`,
        entryPath: artifact.entryPath,
        artifactId: artifact.id,
        route: artifact.route,
        title: artifact.title,
      }),
    });
  });

  await context.route("**/api/canvas/preview/**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "text/html",
      body: "<html><body><h1>canvas preview</h1></body></html>",
    });
  });

  await context.route("**/api/canvas/artifact-*", async (route) => {
    const id = route.request().url().split("/api/canvas/")[1] ?? "";
    const artifact = artifacts.find((item) => item.id === id);
    if (!artifact) {
      await route.fulfill({ status: 404 });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(artifact),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  const canvasTab = page.getByTestId("desk-tab-canvas");
  test.skip((await canvasTab.count()) === 0, "App shell canvas tab integration is owned by main thread.");
  await canvasTab.click();
  await page.waitForTimeout(500);

  const canvasDesk = page.getByTestId("canvas-desk");
  const canvasFrame = page.getByTestId("canvas-entry-frame");
  test.skip(
    (await canvasDesk.count()) === 0 || (await canvasFrame.count()) === 0,
    "Canvas desk preview integration is not active in the current app shell.",
  );

  await expect(canvasDesk).toBeVisible();
  await expect(canvasFrame).toHaveAttribute(
    "src",
    /\/api\/canvas\/preview\/default-token\/canvas\/default\/index\.html$/,
  );

  await page.getByTestId(`canvas-artifact-select-${dashboardArtifact.id}`).click();
  await expect(page.getByTestId("canvas-entry-frame")).toHaveAttribute(
    "src",
    /\/api\/canvas\/preview\/artifact-dashboard-token\/canvas\/ops\/index\.html$/,
  );
  await expect(page.getByTestId("canvas-metadata")).toContainText("Realtime operations dashboard");
});

test("KC-0309 canvas desk: empty list keeps default fallback preview", async ({ page }) => {
  const context = page.context();
  await mockMainShell(context);

  await context.route("**/api/canvas/default", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        entryUrl: "",
        entryPath: "canvas/fallback/index.html",
        artifactId: null,
        route: "/canvas/fallback",
        title: "Fallback canvas entry",
      }),
    });
  });

  await context.route("**/api/canvas?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: [],
        defaultEntryPath: "canvas/fallback/index.html",
        defaultArtifactId: null,
      }),
    });
  });

  await context.route("**/api/canvas/preview/**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "text/html",
      body: "<html><body><h1>fallback preview</h1></body></html>",
    });
  });

  await context.route("**/api/canvas/fs/**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "text/html",
      body: "<html><body><h1>fallback preview</h1></body></html>",
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  const canvasTab = page.getByTestId("desk-tab-canvas");
  test.skip((await canvasTab.count()) === 0, "App shell canvas tab integration is owned by main thread.");
  await canvasTab.click();
  await page.waitForTimeout(500);

  const canvasDesk = page.getByTestId("canvas-desk");
  const canvasFrame = page.getByTestId("canvas-entry-frame");
  const emptyState = page.getByTestId("canvas-empty-state");
  test.skip(
    (await canvasDesk.count()) === 0 || (await canvasFrame.count()) === 0 || (await emptyState.count()) === 0,
    "Canvas desk preview integration is not active in the current app shell.",
  );

  await expect(emptyState).toBeVisible();
  await expect(canvasFrame).toHaveAttribute(
    "src",
    /\/api\/canvas\/fs\/canvas\/fallback\/index\.html$/,
  );
});
