import { expect, test } from "@playwright/test";

test("KC-0213 control-plane integrated acceptance across inbox/sessions/models desks", async ({
  page,
}) => {
  const context = page.context();
  const sessionId = "main-kc0213";

  const inboxItems = [
    {
      id: "inbox-a",
      kind: "Approval",
      status: "Open",
      title: "Outbound send requires approval",
      summary: "Pending decision for external action",
      source: "runtime.main_session.approval",
      createdAt: "2026-03-18T09:00:00.000Z",
      updatedAt: "2026-03-18T09:00:00.000Z",
      requiresAction: true,
      approvalId: "approval-a",
      sessionId,
    },
    {
      id: "inbox-b",
      kind: "Approval",
      status: "Open",
      title: "Channel reply requires approval",
      summary: "Pending decision for channel delivery",
      source: "runtime.main_session.approval",
      createdAt: "2026-03-18T09:02:00.000Z",
      updatedAt: "2026-03-18T09:02:00.000Z",
      requiresAction: true,
      approvalId: "approval-b",
      sessionId,
    },
  ];

  const approvals = [
    {
      id: "approval-a",
      kind: "ExternalAction",
      status: "Pending",
      title: "Approve outbound send",
      summary: "Allow external action to proceed.",
      source: "runtime.main_session.approval",
      requestedAt: "2026-03-18T09:00:00.000Z",
      updatedAt: "2026-03-18T09:00:00.000Z",
      inboxItemId: "inbox-a",
      sessionId,
    },
    {
      id: "approval-b",
      kind: "ChannelDelivery",
      status: "Pending",
      title: "Approve channel reply",
      summary: "Allow outbound channel delivery.",
      source: "runtime.main_session.approval",
      requestedAt: "2026-03-18T09:02:00.000Z",
      updatedAt: "2026-03-18T09:02:00.000Z",
      inboxItemId: "inbox-b",
      sessionId,
    },
  ];

  const timelineEvents: Array<{
    id: string;
    source: string;
    eventType: string;
    level: string;
    message: string;
    timestamp: string;
    sessionId: string;
    correlationId: string;
  }> = [
    {
      id: "evt-requested-a",
      source: "runtime.main_session",
      eventType: "main_session.approval.requested",
      level: "info",
      message: "approval requested: approval-a",
      timestamp: "2026-03-18T09:00:10.000Z",
      sessionId,
      correlationId: "corr-kc0213",
    },
    {
      id: "evt-requested-b",
      source: "runtime.main_session",
      eventType: "main_session.approval.requested",
      level: "info",
      message: "approval requested: approval-b",
      timestamp: "2026-03-18T09:02:10.000Z",
      sessionId,
      correlationId: "corr-kc0213",
    },
  ];

  const models = [
    {
      id: "model-a",
      displayName: "OpenAI Core",
      provider: "OpenAI",
      modelId: "gpt-4.1",
      baseUrl: null,
      apiKeyEnvironmentVariable: "OPENAI_API_KEY",
      enabled: true,
      supportsToolCalling: true,
      isDefault: true,
      createdAt: "2026-03-18T10:00:00Z",
      updatedAt: "2026-03-18T10:00:00Z",
    },
    {
      id: "model-b",
      displayName: "Anthropic Draft",
      provider: "AnthropicCompatible",
      modelId: "claude-3-7-sonnet",
      baseUrl: "https://proxy.example",
      apiKeyEnvironmentVariable: "ANTHROPIC_PROXY_KEY",
      enabled: true,
      supportsToolCalling: true,
      isDefault: false,
      createdAt: "2026-03-18T11:00:00Z",
      updatedAt: "2026-03-18T11:00:00Z",
    },
  ];

  const settings = {
    defaultLandingRoute: "/chat",
    theme: "System",
    requireApprovalForExternalActions: true,
    notificationsEnabled: true,
    quietHoursEnabled: false,
    quietHoursStartLocalTime: null,
    quietHoursEndLocalTime: null,
    updatedAt: "2026-03-18T10:00:00Z",
  };

  const sandboxRisk = {
    generatedAt: "2026-03-19T10:00:00Z",
    executionProfiles: [
      {
        key: "local-boundary",
        displayName: "Local sandbox (current)",
        active: true,
        supported: true,
        boundaryEnforced: true,
        bestEffort: true,
        summary: "Runs on the host with boundary enforcement.",
        blastRadius: "Host tools and writable workspace files remain in scope.",
        guardrails: ["Per-session working directory", "Boundary enforcement"],
        residualRisks: ["Host toolchain", "Writable mounted paths"],
      },
    ],
    approvalPosture: {
      requireApprovalForExternalActions: true,
      notificationsEnabled: true,
      quietHoursEnabled: false,
      quietHoursWindow: null,
      persistedPreferenceOnly: true,
      advisory: "Runtime-specific surfaces can still add their own approval gates.",
    },
    pluginRisk: {
      totalCount: 0,
      signedCount: 0,
      trustedCount: 0,
      untrustedCount: 0,
      highRiskCount: 0,
      networkEnabledCount: 0,
      backgroundCount: 0,
      broadFilesystemCount: 0,
      secretAccessCount: 0,
      channelAccessCount: 0,
      advisory: "No plugin escalations are active.",
      riskItems: [],
    },
    channelRisk: {
      totalThreads: 0,
      outboundCapableThreadCount: 0,
      autoSendCount: 0,
      draftApprovalCount: 0,
      requireApprovalCount: 0,
      pendingApprovalCount: 0,
      advisory: "No active channel threads are widening outbound risk right now.",
      riskItems: [],
    },
    operatorWarnings: ["Local sandbox is active today."],
  };

  const updateState = {
    generatedAt: "2026-03-19T10:05:00Z",
    artifactPath: "/tmp/.kodaclaw-kc0213/config/update-state.json",
    manifestSource: "/tmp/fixtures/update-manifest.json",
    components: [],
    operatorNotes: [
      "KodaClaw uses a manual-first update flow. No background download or silent install is performed.",
    ],
  };

  let savedSettingsPayload: Record<string, unknown> | null = null;

  function pendingApprovalCount(): number {
    return approvals.filter((item) => item.status === "Pending").length;
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
        workspaceRootPath: "/tmp/.kodaclaw-kc0213",
        workspaceVersion: 1,
        workspaceInitialized: true,
        requiresBootstrap: false,
        activeMainSessionId: sessionId,
        mode: "Normal",
      }),
    });
  });

  await context.route("**/api/inbox?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: inboxItems }),
    });
  });

  await context.route("**/api/approvals/approval-a/approve", async (route) => {
    approvals[0] = {
      ...approvals[0],
      status: "Approved",
      decisionNote: "approved from kc0213",
      updatedAt: "2026-03-18T09:05:00.000Z",
    };
    inboxItems[0] = {
      ...inboxItems[0],
      status: "Resolved",
      requiresAction: false,
      updatedAt: "2026-03-18T09:05:00.000Z",
    };
    timelineEvents.push({
      id: "evt-approved-a",
      source: "runtime.main_session",
      eventType: "main_session.approval.decided",
      level: "info",
      message: "approval approved: approval-a",
      timestamp: "2026-03-18T09:05:10.000Z",
      sessionId,
      correlationId: "corr-kc0213",
    });

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(approvals[0]),
    });
  });

  await context.route("**/api/approvals/approval-b/reject", async (route) => {
    approvals[1] = {
      ...approvals[1],
      status: "Rejected",
      decisionNote: "rejected from kc0213",
      updatedAt: "2026-03-18T09:06:00.000Z",
    };
    inboxItems[1] = {
      ...inboxItems[1],
      status: "Acknowledged",
      requiresAction: false,
      updatedAt: "2026-03-18T09:06:00.000Z",
    };
    timelineEvents.push({
      id: "evt-rejected-b",
      source: "runtime.main_session",
      eventType: "main_session.approval.decided",
      level: "warning",
      message: "approval rejected: approval-b",
      timestamp: "2026-03-18T09:06:10.000Z",
      sessionId,
      correlationId: "corr-kc0213",
    });

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(approvals[1]),
    });
  });

  await context.route("**/api/approvals?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: approvals }),
    });
  });

  await context.route("**/api/sessions?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        sessions: [
          {
            sessionId,
            sessionKind: "Main",
            status: {
              isActiveMainSession: true,
              breakpointState: pendingApprovalCount() > 0 ? "AwaitingApproval" : null,
              messageCount: 10,
              pendingApprovalCount: pendingApprovalCount(),
            },
            createdAt: "2026-03-18T09:00:00.0000000+00:00",
            lastEventAt: "2026-03-18T09:06:10.0000000+00:00",
          },
        ],
      }),
    });
  });

  await context.route("**/api/sessions/*", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        sessionId,
        sessionKind: "Main",
        status: {
          isActiveMainSession: true,
          breakpointState: pendingApprovalCount() > 0 ? "AwaitingApproval" : null,
          messageCount: 10,
          pendingApprovalCount: pendingApprovalCount(),
        },
        createdAt: "2026-03-18T09:00:00.0000000+00:00",
        lastEventAt: "2026-03-18T09:06:10.0000000+00:00",
        userMessageCount: 4,
        assistantMessageCount: 4,
        toolCallCount: 2,
        lastSfpIndex: 11,
        pendingApprovalCallIds: pendingApprovalCount() > 0 ? ["call-approval-a", "call-approval-b"] : [],
      }),
    });
  });

  await context.route("**/api/diagnostics/timeline?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        events: timelineEvents,
      }),
    });
  });

  await context.route("**/api/models", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: models }),
    });
  });

  await context.route("**/api/settings", async (route) => {
    if (route.request().method() === "GET") {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(settings),
      });
      return;
    }

    if (route.request().method() === "PUT") {
      const payload = route.request().postDataJSON() as Record<string, unknown>;
      savedSettingsPayload = payload;
      settings.theme = String(payload.theme ?? "System");
      settings.updatedAt = "2026-03-18T09:09:00.000Z";

      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(settings),
      });
      return;
    }

    await route.fulfill({ status: 405 });
  });

  await context.route("**/api/settings/sandbox-risk", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(sandboxRisk),
    });
  });

  await context.route("**/api/system/update-check", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(updateState),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });

  await page.getByTestId("desk-tab-sessions").click();
  await expect(page.getByTestId("sessions-diagnostics-desk")).toBeVisible();
  await expect(page.getByTestId("session-detail")).toContainText("AwaitingApproval");
  await expect(page.getByTestId("diagnostics-timeline")).toContainText("approval requested: approval-a");

  await page.getByTestId("desk-tab-inbox").click();
  await expect(page.getByTestId("inbox-approval-desk")).toBeVisible();
  await page.getByTestId("approval-note-approval-a").fill("approved from kc0213");
  await page.getByTestId("approval-note-approval-b").fill("rejected from kc0213");
  await page.getByTestId("approval-approve").first().click();
  await page.getByTestId("approval-reject").nth(1).click();
  await expect(page.getByTestId("approval-item-approval-a")).toContainText("已批准");
  await expect(page.getByTestId("approval-item-approval-b")).toContainText("已拒绝");
  await expect(page.getByTestId("inbox-status-inbox-a")).toHaveValue("Resolved");
  await expect(page.getByTestId("inbox-status-inbox-b")).toHaveValue("Acknowledged");

  await page.getByTestId("desk-tab-sessions").click();
  await page.getByTestId("sessions-refresh").click();
  await expect(page.getByTestId("session-detail")).toContainText("无");
  await expect(page.getByTestId("diagnostics-timeline")).toContainText("approval approved: approval-a");
  await expect(page.getByTestId("diagnostics-timeline")).toContainText("approval rejected: approval-b");

  await page.getByTestId("desk-tab-models").click();
  await expect(page.getByTestId("models-settings-desk")).toBeVisible();
  await page.getByTestId("settings-theme").selectOption("Dark");
  await page.getByTestId("settings-save").click();
  await expect(page.getByText("设置已保存。")).toBeVisible();
  await expect.poll(() => savedSettingsPayload).not.toBeNull();

  if (!savedSettingsPayload) {
    throw new Error("Expected settings save payload to be captured.");
  }
  expect(savedSettingsPayload).toMatchObject({ theme: "Dark" });
});
