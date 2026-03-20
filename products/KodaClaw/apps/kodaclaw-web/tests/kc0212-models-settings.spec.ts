import { expect, test } from "@playwright/test";

test("KC-0212 models/settings desk: manage endpoints and persist settings", async ({ page }) => {
  const context = page.context();

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
      {
        key: "docker-isolation",
        displayName: "Docker sandbox (SDK-supported)",
        active: false,
        supported: true,
        boundaryEnforced: true,
        bestEffort: false,
        summary: "Can move command execution into a container.",
        blastRadius: "Mounted paths stay writable unless mounted read-only.",
        guardrails: ["Container isolation"],
        residualRisks: ["Not active by default"],
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
      totalCount: 1,
      signedCount: 1,
      trustedCount: 0,
      untrustedCount: 0,
      highRiskCount: 1,
      networkEnabledCount: 1,
      backgroundCount: 1,
      broadFilesystemCount: 1,
      secretAccessCount: 1,
      channelAccessCount: 1,
      advisory: "Review high-risk plugins together with trust and enablement state.",
      riskItems: [
        {
          pluginId: "plugin.fixture",
          displayName: "Fixture Plugin",
          trustState: "Signed",
          enabled: true,
          runtimeState: "Running",
          requestedScopes: ["network outbound", "filesystem: workspace"],
          highRiskReasons: ["Requests network access."],
          mediumRiskReasons: ["Requests 1 channel scope(s)."],
          trustEvidenceSummary: "Signature sidecar matched current digests.",
        },
      ],
    },
    channelRisk: {
      totalThreads: 1,
      outboundCapableThreadCount: 1,
      autoSendCount: 0,
      draftApprovalCount: 1,
      requireApprovalCount: 0,
      pendingApprovalCount: 1,
      advisory: "Current defaults keep direct messages in draft approval.",
      riskItems: [
        {
          bindingId: "binding-001",
          displayTitle: "Alice",
          connectorKind: "Telegram",
          supportsOutbound: true,
          threadType: "DirectMessage",
          accountState: "Connected",
          deliveryMode: "DraftApproval",
          hasPendingApproval: true,
          pendingApprovalId: "approval-channel-001",
        },
      ],
    },
    operatorWarnings: [
      "Local sandbox is active today.",
      "Docker isolation is supported by the SDK, but it is not the active runtime profile.",
    ],
  };

  const updateState = {
    generatedAt: "2026-03-19T10:05:00Z",
    artifactPath: "/tmp/.kodaclaw/config/update-state.json",
    manifestSource: "/tmp/fixtures/update-manifest.json",
    components: [
      {
        component: "gateway",
        displayName: "Gateway",
        currentVersion: "0.1.0",
        releaseChannel: "Stable",
        lastCheckedAt: "2026-03-19T10:05:00Z",
        latestKnownVersion: "0.1.2",
        updateAvailability: "UpdateAvailable",
        downloadUrl: "https://example.com/download",
        releaseNotesUrl: "https://example.com/release-notes",
        releaseNotes: [
          "Adds the manual-first update desk.",
          "Improves operator release visibility.",
        ],
        guidance: "Review the release notes, then complete the guided handoff manually.",
      },
      {
        component: "desktop",
        displayName: "Desktop Shell",
        currentVersion: "0.1.0",
        releaseChannel: "Stable",
        lastCheckedAt: "2026-03-19T10:05:00Z",
        latestKnownVersion: "0.1.3",
        updateAvailability: "UpdateAvailable",
        downloadUrl: "https://example.com/desktop-download",
        releaseNotesUrl: "https://example.com/desktop-release-notes",
        releaseNotes: [
          "Desktop bridge now reports app version and release channel.",
        ],
        guidance: "Download the packaged shell build and relaunch after the installer finishes.",
      },
    ],
    operatorNotes: [
      "KodaClaw uses a manual-first update flow. No background download or silent install is performed.",
    ],
  };

  let savedSettingsPayload: Record<string, unknown> | null = null;
  let updateCheckCount = 0;

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
        workspaceRootPath: "/tmp/.kodaclaw-kc0212",
        workspaceVersion: 1,
        workspaceInitialized: true,
        requiresBootstrap: false,
        activeMainSessionId: "main-0212",
        mode: "Normal",
      }),
    });
  });

  await context.route("**/api/models", async (route) => {
    if (route.request().method() === "GET") {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ items: models }),
      });
      return;
    }

    if (route.request().method() === "POST") {
      const payload = route.request().postDataJSON() as Record<string, unknown>;
      const created = {
        id: "model-c",
        displayName: String(payload.displayName ?? "Model C"),
        provider: String(payload.provider ?? "OpenAICompatible"),
        modelId: String(payload.modelId ?? "o3"),
        baseUrl: payload.baseUrl ?? null,
        apiKeyEnvironmentVariable: payload.apiKeyEnvironmentVariable ?? null,
        enabled: Boolean(payload.enabled ?? true),
        supportsToolCalling: Boolean(payload.supportsToolCalling ?? true),
        isDefault: false,
        createdAt: "2026-03-18T12:00:00Z",
        updatedAt: "2026-03-18T12:00:00Z",
      };
      models.unshift(created);
      await route.fulfill({
        status: 201,
        contentType: "application/json",
        body: JSON.stringify(created),
      });
      return;
    }

    await route.fulfill({ status: 405 });
  });

  await context.route("**/api/models/*/default", async (route) => {
    const id = route.request().url().split("/").slice(-2)[0];
    const index = models.findIndex((item) => item.id === id);
    if (index < 0) {
      await route.fulfill({ status: 404 });
      return;
    }

    for (const item of models) {
      item.isDefault = false;
    }
    models[index].isDefault = true;
    models[index].updatedAt = "2026-03-18T12:15:00Z";

    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(models[index]),
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
      settings.updatedAt = "2026-03-18T12:30:00Z";
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
    updateCheckCount += 1;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(updateState),
    });
  });

  await page.goto("/", { waitUntil: "domcontentloaded" });
  await page.getByTestId("desk-tab-models").click();

  const desk = page.getByTestId("models-settings-desk");
  await expect(desk).toBeVisible();
  await expect(page.getByTestId("models-list")).toBeVisible();
  await expect(page.getByTestId("model-detail")).toContainText("OpenAI Core");
  await expect(page.getByTestId("settings-form")).toBeVisible();
  await expect(page.getByTestId("settings-update-watch")).toBeVisible();
  await expect(page.getByTestId("settings-risk-briefing")).toBeVisible();
  await expect(page.getByTestId("settings-update-watch").getByText("更新观察台")).toBeVisible();
  await expect(page.getByTestId("settings-risk-briefing").getByText("沙箱与风险简报")).toBeVisible();
  await expect(page.getByTestId("update-component-gateway")).toBeVisible();
  await expect(page.getByTestId("update-component-desktop")).toBeVisible();
  await expect(page.getByTestId("settings-risk-briefing").getByText("Fixture Plugin")).toBeVisible();
  const initialUpdateCheckCount = updateCheckCount;
  await page.getByTestId("settings-update-check").click();
  await expect.poll(() => updateCheckCount).toBe(initialUpdateCheckCount + 1);

  await page.getByTestId("model-display-name").fill("Proxy X");
  await page.getByTestId("model-provider").selectOption("OpenAICompatible");
  await page.getByTestId("model-id").fill("o3");
  await page.getByTestId("model-base-url").fill("https://proxy-x.example");
  await page.getByTestId("model-api-key-env").fill("PROXY_X_KEY");
  await page.getByTestId("model-create-submit").click();

  await expect(page.getByTestId("model-item-model-c")).toContainText("Proxy X");
  await expect(page.getByTestId("model-detail")).toContainText("Proxy X");

  const modelItems = page.getByTestId("models-list").locator("li");
  await modelItems.first().getByTestId("model-default").click();
  await expect(modelItems.first()).toContainText("默认");

  await page.getByTestId("settings-theme").selectOption("Dark");
  await page.getByTestId("settings-save").click();
  await expect.poll(() => savedSettingsPayload).not.toBeNull();
  if (!savedSettingsPayload) {
    throw new Error("Expected settings save payload to be captured.");
  }
  expect(savedSettingsPayload).toMatchObject({ theme: "Dark" });
});
