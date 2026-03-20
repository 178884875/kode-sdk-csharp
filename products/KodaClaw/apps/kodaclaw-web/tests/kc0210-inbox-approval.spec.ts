import { expect, test } from "@playwright/test";

test("KC-0210 inbox/approval desk: list, approve, reject, and inbox feedback loop", async ({
  page,
}) => {
  await page.route("**/api/system/health", async (route) => {
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

  await page.route("**/api/system/bootstrap-state", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        workspaceRootPath: "/tmp/.kodaclaw-kc0210",
        workspaceVersion: 1,
        workspaceInitialized: true,
        requiresBootstrap: false,
        activeMainSessionId: "main-0210",
        mode: "Normal",
      }),
    });
  });

  const inboxItems = [
    {
      id: "inbox-a",
      kind: "Approval",
      status: "Open",
      title: "Outbound safety check",
      summary: "Requires decision for tool call",
      source: "runtime.main_session.approval",
      createdAt: "2026-03-18T09:00:00.000Z",
      updatedAt: "2026-03-18T09:00:00.000Z",
      requiresAction: true,
      approvalId: "approval-a",
    },
    {
      id: "inbox-b",
      kind: "Approval",
      status: "Open",
      title: "Channel dispatch decision",
      summary: "Requires operator review",
      source: "runtime.main_session.approval",
      createdAt: "2026-03-18T09:03:00.000Z",
      updatedAt: "2026-03-18T09:03:00.000Z",
      requiresAction: true,
      approvalId: "approval-b",
    },
  ];

  const approvals = [
    {
      id: "approval-a",
      kind: "ExternalAction",
      status: "Pending",
      title: "Approve outbound action",
      summary: "Needs user confirmation.",
      source: "runtime.main_session.approval",
      requestedAt: "2026-03-18T09:00:00.000Z",
      updatedAt: "2026-03-18T09:00:00.000Z",
      inboxItemId: "inbox-a",
    },
    {
      id: "approval-b",
      kind: "ChannelDelivery",
      status: "Pending",
      title: "Approve channel delivery",
      summary: "Needs user confirmation.",
      source: "runtime.main_session.approval",
      requestedAt: "2026-03-18T09:03:00.000Z",
      updatedAt: "2026-03-18T09:03:00.000Z",
      inboxItemId: "inbox-b",
    },
  ];

  let approveHit = false;
  let rejectHit = false;

  await page.route("**/api/inbox?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: inboxItems }),
    });
  });

  await page.route("**/api/approvals/approval-a/approve", async (route) => {
    approveHit = true;
    approvals[0] = {
      ...approvals[0],
      status: "Approved",
      updatedAt: "2026-03-18T09:05:00.000Z",
      decisionNote: "approved from desk",
    };
    inboxItems[0] = {
      ...inboxItems[0],
      status: "Resolved",
      updatedAt: "2026-03-18T09:05:00.000Z",
      requiresAction: false,
    };

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(approvals[0]),
    });
  });

  await page.route("**/api/approvals/approval-b/reject", async (route) => {
    rejectHit = true;
    approvals[1] = {
      ...approvals[1],
      status: "Rejected",
      updatedAt: "2026-03-18T09:06:00.000Z",
      decisionNote: "reject from desk",
    };
    inboxItems[1] = {
      ...inboxItems[1],
      status: "Acknowledged",
      updatedAt: "2026-03-18T09:06:00.000Z",
      requiresAction: false,
    };

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(approvals[1]),
    });
  });

  await page.route("**/api/approvals?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: approvals }),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  await page.getByTestId("desk-tab-inbox").click();

  await expect(page.getByTestId("inbox-approval-desk")).toBeVisible();
  await expect(page.getByTestId("inbox-list")).toBeVisible();
  await expect(page.getByTestId("approval-list")).toBeVisible();

  await page.getByTestId("approval-note-approval-a").fill("approved from desk");
  await page.getByTestId("approval-note-approval-b").fill("reject from desk");

  await page.getByTestId("approval-approve").first().click();
  expect(approveHit).toBeTruthy();
  await expect(page.getByTestId("approval-item-approval-a")).toContainText("已批准");
  await expect(page.getByTestId("inbox-status-inbox-a")).toHaveValue("Resolved");

  await page.getByTestId("approval-reject").nth(1).click();
  expect(rejectHit).toBeTruthy();
  await expect(page.getByTestId("approval-item-approval-b")).toContainText("已拒绝");
  await expect(page.getByTestId("inbox-status-inbox-b")).toHaveValue("Acknowledged");

  await page.getByTestId("inbox-refresh").click();
  await expect(page.getByText("Outbound safety check")).toBeVisible();
  await expect(page.getByText("Channel dispatch decision")).toBeVisible();
});
