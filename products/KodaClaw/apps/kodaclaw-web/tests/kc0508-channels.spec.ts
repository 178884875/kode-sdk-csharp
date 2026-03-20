import { expect, test } from "@playwright/test";

test("KC-0508 channels desk: connectors/accounts/threads/detail workflow", async ({ page }) => {
  const context = page.context();
  const pageErrors: string[] = [];
  page.on("pageerror", (error) => {
    pageErrors.push(error.message);
    console.log(`pageerror: ${error.message}`);
  });

  const connectors = [
    {
      kind: "Telegram",
      displayName: "Telegram",
      implemented: true,
      supportsInbound: true,
      supportsOutbound: true,
      productOwned: true,
    },
    {
      kind: "GenericWebhook",
      displayName: "Generic Webhook",
      implemented: true,
      supportsInbound: true,
      supportsOutbound: false,
      productOwned: true,
    },
  ];

  const accounts = [
    {
      id: "telegram-main",
      connectorKind: "Telegram",
      displayName: "Telegram Bot",
      state: "Connected",
      createdAt: "2026-03-19T08:00:00.000Z",
      updatedAt: "2026-03-19T08:30:00.000Z",
      externalAccountId: "bot-001",
      credentialReference: "env:KODACLAW_TELEGRAM_TOKEN",
      description: null,
      configurationJson: "{\"botToken\":\"inline:test-token\"}",
      inboundEnabled: true,
      lastConnectedAt: "2026-03-19T08:00:00.000Z",
      lastDisconnectedAt: null,
      lastError: null,
    },
  ];

  const threads = {
    items: [
      {
        bindingId: "binding-telegram-001",
        connectorKind: "Telegram",
        accountId: "telegram-main",
        externalThreadId: "10001",
        threadType: "DirectMessage",
        sessionId: "channel-dm-001",
        sessionKind: "ChannelDirectMessage",
        displayTitle: "Alice",
        deliveryMode: "DraftApproval",
        accountState: "Connected",
        updatedAt: "2026-03-19T08:30:00.000Z",
        lastInboundAt: "2026-03-19T08:29:00.000Z",
        lastOutboundAt: null,
        lastMessagePreview: "hello from telegram",
        pendingApprovalId: "approval-channel-001",
        hasPendingDraft: true,
      },
    ],
  };

  const detail = {
    account: accounts[0],
    binding: {
      id: "binding-telegram-001",
      connectorKind: "Telegram",
      accountId: "telegram-main",
      externalThreadId: "10001",
      threadType: "DirectMessage",
      sessionId: "channel-dm-001",
      sessionKind: "ChannelDirectMessage",
      channelIdentity: {
        id: "20001",
        username: "alice",
        displayName: "Alice",
        isBot: false,
      },
      policyId: "policy-default-dm",
      deliveryRuleId: "delivery-default-dm",
      createdAt: "2026-03-19T08:00:00.000Z",
      updatedAt: "2026-03-19T08:30:00.000Z",
      lastInboundAt: "2026-03-19T08:29:00.000Z",
      lastOutboundAt: null,
      lastMessagePreview: "hello from telegram",
    },
    policy: {
      id: "policy-default-dm",
      threadType: "DirectMessage",
      updatedAt: "2026-03-19T08:30:00.000Z",
      loadAgents: true,
      loadIdentity: true,
      loadSoul: true,
      loadUserProfile: true,
      loadLongTermMemory: false,
      loadRecentThreadSummary: true,
      allowDirectReply: true,
      requireExplicitMention: false,
      workspaceMuted: false,
      connectorMuted: false,
      threadMuted: false,
      notes: null,
    },
    deliveryRule: {
      id: "delivery-default-dm",
      mode: "DraftApproval",
      updatedAt: "2026-03-19T08:30:00.000Z",
      allowProactiveSend: false,
      muteDuringQuietHours: true,
    },
    recentAudit: [],
    session: {
      sessionId: "channel-dm-001",
      sessionKind: "ChannelDirectMessage",
      status: {
        isActiveMainSession: false,
        breakpointState: null,
        messageCount: 3,
        pendingApprovalCount: 1,
      },
      createdAt: "2026-03-19T08:00:00.000Z",
      lastEventAt: "2026-03-19T08:29:00.000Z",
    },
    pendingApprovalId: "approval-channel-001",
    hasPendingDraft: true,
  };

  const audit = [
    {
      id: "audit-001",
      bindingId: "binding-telegram-001",
      connectorKind: "Telegram",
      accountId: "telegram-main",
      externalThreadId: "10001",
      threadType: "DirectMessage",
      eventType: "message.received",
      createdAt: "2026-03-19T08:29:00.000Z",
      sessionId: "channel-dm-001",
      approvalId: null,
      deliveryMode: "DraftApproval",
      externalMessageId: "11",
      summary: "hello from telegram",
      metadataJson: null,
    },
  ];

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
        workspaceRootPath: "/tmp/.kodaclaw-kc0508",
        workspaceVersion: 1,
        workspaceInitialized: true,
        requiresBootstrap: false,
        activeMainSessionId: "main-kc0508",
        mode: "Normal",
      }),
    });
  });

  await context.route("**/api/channels/connectors", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(connectors),
    });
  });

  await context.route("**/api/channels/accounts?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(accounts),
    });
  });

  await context.route("**/api/channels/threads?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(threads),
    });
  });

  await context.route("**/api/channels/threads/binding-telegram-001", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(detail),
    });
  });

  await context.route("**/api/channels/threads/binding-telegram-001/audit?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(audit),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  await expect(page.getByTestId("desk-tab-channels")).toBeVisible();
  await page.getByTestId("desk-tab-channels").click();

  // KC-1503: V2 shell data-kc-view="channels" is stable after navigation
  await expect(page.locator('[data-kc-view="channels"]')).toBeVisible();
  await expect(page.getByTestId("channels-desk")).toBeVisible();
  await expect(page.getByTestId("channels-summary")).toContainText("1 个账号 · 1 条线程");
  await expect(page.getByTestId("channels-threads")).toContainText("Alice");
  await expect(page.getByTestId("channel-thread-detail")).toContainText("草稿审批");
  await expect(page.getByTestId("channel-thread-audit")).toContainText("message.received");

  await page.getByTestId("channels-refresh").click();
  await expect(page.getByTestId("channels-connectors-accounts")).toContainText("Generic Webhook");

  expect(pageErrors, `Unexpected page errors: ${pageErrors.join(" | ")}`).toEqual([]);
});
