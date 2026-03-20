import { expect, test } from "@playwright/test";

test("KC-0308 automations desk: list/filter/detail/toggle workflow", async ({ page }) => {
  const context = page.context();

  let automations = [
    {
      id: "auto-a",
      title: "Daily digest",
      prompt: "Summarize inbox events and generate a concise daily update for operators.",
      source: "Manual",
      sourcePath: "/workspace/automations/digest.md",
      schedule: {
        kind: "Daily",
        localTime: "09:30",
      },
      enabled: true,
      inputPaths: ["/workspace/inbox", "/workspace/notes"],
      createdAt: "2026-03-18T08:00:00.000Z",
      updatedAt: "2026-03-18T08:00:00.000Z",
      lastRunAt: "2026-03-18T09:30:00.000Z",
      nextRunAt: "2026-03-19T09:30:00.000Z",
      lastRunStatus: "Succeeded",
      lastError: null,
    },
    {
      id: "auto-b",
      title: "Weekly heartbeat check",
      prompt: "Read heartbeat docs, compare drift, and emit remediation guidance.",
      source: "Heartbeat",
      sourcePath: "/workspace/HEARTBEAT.md",
      schedule: {
        kind: "Weekly",
        daysOfWeek: ["Monday", "Wednesday", "Friday"],
        localTime: "11:15",
      },
      enabled: false,
      inputPaths: ["/workspace/HEARTBEAT.md"],
      createdAt: "2026-03-18T08:30:00.000Z",
      updatedAt: "2026-03-18T08:30:00.000Z",
      lastRunAt: "2026-03-18T11:15:00.000Z",
      nextRunAt: "2026-03-19T11:15:00.000Z",
      lastRunStatus: "Failed",
      lastError: "threshold exceeded",
    },
  ];

  const runsByAutomation: Record<string, unknown[]> = {
    "auto-a": [
      {
        runId: "run-a-1",
        automationId: "auto-a",
        status: "Succeeded",
        trigger: "schedule",
        attempt: 1,
        sessionId: "session-auto-a",
        startedAt: "2026-03-18T09:30:00.000Z",
        completedAt: "2026-03-18T09:31:00.000Z",
        summary: "Digest generated.",
        errorMessage: null,
      },
    ],
    "auto-b": [
      {
        runId: "run-b-1",
        automationId: "auto-b",
        status: "Failed",
        trigger: "schedule",
        attempt: 2,
        sessionId: "session-auto-b",
        startedAt: "2026-03-18T11:15:00.000Z",
        completedAt: "2026-03-18T11:17:00.000Z",
        summary: "Drift exceeded threshold.",
        errorMessage: "threshold exceeded",
      },
    ],
  };

  let updatePayload: Record<string, unknown> | null = null;

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
        workspaceRootPath: "/tmp/.kodaclaw-kc0308",
        workspaceVersion: 1,
        workspaceInitialized: true,
        requiresBootstrap: false,
        activeMainSessionId: "main-kc0308",
        mode: "Normal",
      }),
    });
  });

  await context.route("**/api/automations?**", async (route) => {
    const url = new URL(route.request().url());
    const enabledFilter = url.searchParams.get("enabled");
    const sourceFilter = url.searchParams.get("source");

    let items = [...automations];

    if (enabledFilter === "true") {
      items = items.filter((item) => item.enabled);
    }

    if (sourceFilter) {
      items = items.filter((item) => item.source === sourceFilter);
    }

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items }),
    });
  });

  await context.route("**/api/automations/*/runs?**", async (route) => {
    const automationId = route.request().url().split("/api/automations/")[1]?.split("/")[0] ?? "";

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: runsByAutomation[automationId] ?? [] }),
    });
  });

  await context.route("**/api/automations/*", async (route) => {
    if (route.request().method() !== "PATCH") {
      await route.fulfill({ status: 405 });
      return;
    }

    const automationId = route.request().url().split("/api/automations/")[1]?.split("?")[0] ?? "";
    const payload = route.request().postDataJSON() as { enabled?: boolean };
    updatePayload = payload;

    automations = automations.map((item) =>
      item.id === automationId
        ? {
            ...item,
            enabled: Boolean(payload.enabled),
            updatedAt: "2026-03-18T12:00:00.000Z",
          }
        : item,
    );

    const updated = automations.find((item) => item.id === automationId);
    if (!updated) {
      await route.fulfill({ status: 404 });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(updated),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  await page.getByTestId("desk-tab-automations").click();

  await expect(page.getByTestId("automations-desk")).toBeVisible();
  await expect(page.getByTestId("automations-list")).toContainText("Daily digest");
  await expect(page.getByTestId("automations-list")).toContainText("Weekly heartbeat check");

  await page.getByTestId("automations-source-filter").selectOption("Heartbeat");
  await expect(page.getByTestId("automations-list")).toContainText("Weekly heartbeat check");
  await expect(page.getByTestId("automations-list")).not.toContainText("Daily digest");

  await page.getByTestId("automations-source-filter").selectOption("all");
  await page.getByTestId("automations-enabled-filter").selectOption("all");

  await page.getByTestId("automation-select-auto-b").click();
  await expect(page.getByTestId("automation-detail")).toContainText("Weekly heartbeat check");
  await expect(page.getByTestId("automation-detail-input-paths")).toContainText("/workspace/HEARTBEAT.md");
  await expect(page.getByTestId("automation-runs")).toContainText("Drift exceeded threshold.");

  await page.getByTestId("automation-toggle-auto-b").click();
  await expect.poll(() => updatePayload).not.toBeNull();

  if (!updatePayload) {
    throw new Error("Expected update payload to be captured.");
  }

  expect(updatePayload).toMatchObject({ enabled: true });
  await expect(page.getByTestId("automation-enabled-chip")).toHaveText("已启用");
  await expect(page.getByTestId("automation-item-auto-b")).toContainText("已启用");
});
