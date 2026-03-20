import "@testing-library/jest-dom";
import React from "react";
import { ReadableStream } from "node:stream/web";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { I18nProvider } from "../i18n/I18nProvider";
import {
  __resetRuntimeConfigForTests,
  initializeRuntimeConfig,
  type DesktopLaunchTarget,
} from "../lib/config";

const originalFetch = global.fetch;

function renderApp() {
  return render(
    <I18nProvider>
      <App />
    </I18nProvider>,
  );
}

function jsonResponse(payload: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 200 ? "OK" : "ERROR",
    json: async () => payload,
    text: async () => JSON.stringify(payload),
  } as Response;
}

function streamResponse(payload: string): Response {
  const encoder = new TextEncoder();

  return {
    ok: true,
    status: 200,
    statusText: "OK",
    body: new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(encoder.encode(payload));
        controller.close();
      },
    }),
  } as unknown as Response;
}

const automationFixture = {
  id: "auto-heartbeat",
  title: "Heartbeat Digest",
  prompt: "Review unresolved inbox items and summarize the queue.",
  source: "Heartbeat",
  sourcePath: "workspace/HEARTBEAT.md",
  schedule: {
    kind: "Weekly",
    interval: null,
    localTime: "09:00",
    daysOfWeek: ["Monday", "Wednesday", "Friday"],
  },
  enabled: true,
  inputPaths: ["inbox", "tasks"],
  createdAt: "2026-03-18T08:00:00Z",
  updatedAt: "2026-03-18T08:00:00Z",
  lastRunAt: "2026-03-18T09:00:00Z",
  nextRunAt: "2026-03-19T09:00:00Z",
  lastRunStatus: "Succeeded",
  lastError: null,
};

const automationRunFixture = {
  runId: "run-heartbeat-001",
  automationId: "auto-heartbeat",
  status: "Succeeded",
  trigger: "heartbeat",
  attempt: 1,
  sessionId: "auto-session-001",
  startedAt: "2026-03-18T09:00:00Z",
  completedAt: "2026-03-18T09:01:00Z",
  summary: "Queue digest posted.",
  errorMessage: null,
};

const automationSessionDetailFixture = {
  sessionId: "auto-session-001",
  sessionKind: "Automation",
  status: {
    isActiveMainSession: false,
    breakpointState: "Ready",
    messageCount: 4,
    pendingApprovalCount: 0,
  },
  createdAt: "2026-03-18T09:00:00Z",
  lastEventAt: "2026-03-18T09:01:00Z",
  userMessageCount: 1,
  assistantMessageCount: 2,
  toolCallCount: 1,
  lastSfpIndex: 3,
  pendingApprovalCallIds: [],
  promptReport: {
    profileId: "Automation",
    systemPrompt: "Automation prompt",
    characterCount: 320,
    loadedContextFiles: ["workspace/IDENTITY.md", "workspace/tasks/index.md"],
    generatedAt: "2026-03-18T09:00:00Z",
    characterBudget: 1200,
    remainingCharacterBudget: 880,
    wasTruncated: false,
    truncatedContextFiles: [],
    truncationNotes: [],
  },
  promptReportDelta: {
    previousGeneratedAt: "2026-03-18T08:00:00Z",
    characterCountDelta: 14,
    truncationStateChanged: false,
    addedContextFiles: ["workspace/tasks/index.md"],
    removedContextFiles: [],
  },
  recentPromptReports: [],
};

const canvasFixture = {
  id: "canvas-launch",
  title: "Launch Snapshot",
  kind: "Report",
  summary: "Executive summary board.",
  source: "runtime.main",
  route: "/canvas/launch",
  entryPath: "canvas/launch/index.html",
  assetDirectory: "canvas/launch",
  sessionId: "session-main",
  correlationId: "corr-launch",
  createdAt: "2026-03-18T08:00:00Z",
  updatedAt: "2026-03-18T08:30:00Z",
  metadataJson: null,
};

const pluginSummaryFixture = {
  id: "plugin.fixture",
  name: "Fixture Plugin",
  version: "0.1.0",
  types: ["Tool"],
  installSource: "LocalDirectory",
  trustState: "Signed",
  enabled: true,
  runtimeState: "Running",
  rootPath: "/tmp/.kodaclaw/workspace/plugins/plugin.fixture",
  updatedAt: "2026-03-18T08:35:00Z",
  lastError: null,
};

const pluginDetailFixture = {
  record: {
    id: "plugin.fixture",
    manifest: {
      id: "plugin.fixture",
      name: "Fixture Plugin",
      version: "0.1.0",
      types: ["Tool"],
      runtime: {
        transport: "Stdio",
        command: "fixture",
      },
      permissions: {
        background: true,
      },
      capabilities: {
        tools: ["echo"],
      },
    },
    installSource: "LocalDirectory",
    rootPath: "/tmp/.kodaclaw/workspace/plugins/plugin.fixture",
    trustState: "Signed",
    enabled: true,
    runtimeState: "Running",
    discoveredAt: "2026-03-18T08:00:00Z",
    installedAt: "2026-03-18T08:00:00Z",
    updatedAt: "2026-03-18T08:35:00Z",
    lastStartedAt: "2026-03-18T08:10:00Z",
    lastStoppedAt: null,
    lastHealthAt: "2026-03-18T08:35:00Z",
    restartCount: 0,
    lastError: null,
    trustEvidence: {
      source: "SignatureSidecar",
      verificationState: "Verified",
      summary: "Signature sidecar matched the current manifest and package digests.",
      verifiedAt: "2026-03-18T08:35:00Z",
      manifestDigestSha256: "fixture-manifest-digest",
      packageDigestSha256: "fixture-package-digest",
      signer: "Fixture Publisher",
      signatureFilePath: "/tmp/.kodaclaw/workspace/plugins/plugin.fixture/plugin.signature.json",
    },
  },
  permissionSummary: {
    highRiskReasons: ["Can run in background."],
    mediumRiskReasons: [],
    hasHighRisk: true,
  },
  healthSummary: {
    status: "Healthy",
    message: "Plugin runtime is responding.",
    lastHealthAt: "2026-03-18T08:35:00Z",
    restartCount: 0,
    isHealthy: true,
  },
  availableTools: ["mcp__plugin.fixture__echo"],
};

const pluginLogFixture = {
  entryId: "plugin-log-001",
  pluginId: "plugin.fixture",
  level: "info",
  source: "plugin.host",
  message: "Plugin started successfully.",
  timestamp: "2026-03-18T08:35:00Z",
  payloadJson: null,
};

const channelConnectorFixture = [
  {
    kind: "Telegram",
    displayName: "Telegram",
    implemented: true,
    supportsInbound: true,
    supportsOutbound: true,
    productOwned: true,
  },
];

const channelAccountFixture = {
  id: "telegram-main",
  connectorKind: "Telegram",
  displayName: "Telegram Bot",
  state: "Connected",
  createdAt: "2026-03-18T08:00:00Z",
  updatedAt: "2026-03-18T08:30:00Z",
  externalAccountId: "bot-001",
  credentialReference: "env:KODACLAW_TELEGRAM_TOKEN",
  description: null,
  configurationJson: "{\"botToken\":\"inline:token\"}",
  inboundEnabled: true,
};

const channelThreadFixture = {
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
  updatedAt: "2026-03-18T08:30:00Z",
  lastInboundAt: "2026-03-18T08:29:00Z",
  lastOutboundAt: null,
  lastMessagePreview: "hello from telegram",
  pendingApprovalId: null,
  hasPendingDraft: false,
};

const channelThreadDetailFixture = {
  account: channelAccountFixture,
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
    createdAt: "2026-03-18T08:00:00Z",
    updatedAt: "2026-03-18T08:30:00Z",
    lastInboundAt: "2026-03-18T08:29:00Z",
    lastOutboundAt: null,
    lastMessagePreview: "hello from telegram",
  },
  policy: {
    id: "policy-default-dm",
    threadType: "DirectMessage",
    updatedAt: "2026-03-18T08:30:00Z",
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
    updatedAt: "2026-03-18T08:30:00Z",
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
      pendingApprovalCount: 0,
    },
    createdAt: "2026-03-18T08:00:00Z",
    lastEventAt: "2026-03-18T08:29:00Z",
  },
  pendingApprovalId: null,
  hasPendingDraft: false,
};

describe("App shell", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
    __resetRuntimeConfigForTests();
    delete window.kodaClawDesktop;
    window.localStorage.clear();
    window.history.pushState({}, "", "/");
  });

  afterEach(() => {
    __resetRuntimeConfigForTests();
    delete window.kodaClawDesktop;
    vi.restoreAllMocks();
    global.fetch = originalFetch;
  });

  it("renders bootstrap desk when bootstrap-state requires onboarding", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(
        jsonResponse({ name: "KodaClaw Gateway", status: "healthy", mode: "Bootstrap" }),
      )
      .mockResolvedValueOnce(
        jsonResponse({
          workspaceRootPath: "/tmp/.kodaclaw",
          workspaceVersion: 1,
          workspaceInitialized: true,
          requiresBootstrap: true,
          activeMainSessionId: null,
          mode: "Bootstrap",
        }),
      );

    renderApp();

    await waitFor(() => {
      expect(screen.getByText("引导编排")).toBeInTheDocument();
    });
    expect(screen.getByTestId("v2-shell")).toBeInTheDocument();
    expect(screen.getByTestId("chat-input")).toBeInTheDocument();
    expect(screen.getByTestId("bootstrap-identity-input")).toBeInTheDocument();
    expect(screen.getByTestId("bootstrap-soul-input")).toBeInTheDocument();
    expect(screen.getByTestId("bootstrap-user-input")).toBeInTheDocument();
  });

  it("defaults to Chinese and allows switching the shell copy to English", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(
        jsonResponse({ name: "KodaClaw Gateway", status: "healthy", mode: "Bootstrap" }),
      )
      .mockResolvedValueOnce(
        jsonResponse({
          workspaceRootPath: "/tmp/.kodaclaw",
          workspaceVersion: 1,
          workspaceInitialized: true,
          requiresBootstrap: true,
          activeMainSessionId: null,
          mode: "Bootstrap",
        }),
      );

    renderApp();

    await screen.findByText("引导编排");
    expect(document.documentElement.lang).toBe("zh-CN");

    const user = userEvent.setup();
    await user.click(screen.getByTestId("locale-toggle-en-US"));

    await waitFor(() => {
      expect(screen.getByText("Bootstrap Guidance")).toBeInTheDocument();
    });
    expect(window.localStorage.getItem("kodaclaw.locale")).toBe("en-US");
    expect(document.documentElement.lang).toBe("en-US");
    expect(document.title).toBe("KodaClaw Field Console");
  });

  it("renders main chat status when bootstrap has completed", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(
        jsonResponse({ name: "KodaClaw Gateway", status: "healthy", mode: "Normal" }),
      )
      .mockResolvedValueOnce(
        jsonResponse({
          workspaceRootPath: "/tmp/.kodaclaw",
          workspaceVersion: 1,
          workspaceInitialized: true,
          requiresBootstrap: false,
          activeMainSessionId: "main-001",
          mode: "Normal",
        }),
      )
      .mockResolvedValueOnce(
        jsonResponse({
          sessions: [
            {
              sessionId: "main-001",
              sessionKind: "Main",
              status: {
                isActiveMainSession: true,
                breakpointState: "Ready",
                messageCount: 4,
                pendingApprovalCount: 0,
              },
              createdAt: "2026-03-18T10:00:00Z",
              lastEventAt: "2026-03-18T10:01:00Z",
            },
          ],
        }),
      );

    renderApp();

    await waitFor(() => {
      expect(screen.getByText("主控对话已激活")).toBeInTheDocument();
    });
    expect(screen.getByTestId("v2-shell")).toBeInTheDocument();
    expect(screen.getByText("引导已归档")).toBeInTheDocument();
    expect(screen.getByTestId("desk-tab-chat")).toBeInTheDocument();
    expect(screen.getByTestId("desk-tab-automations")).toBeInTheDocument();
    expect(screen.getByTestId("desk-tab-channels")).toBeInTheDocument();
    expect(screen.getByTestId("desk-tab-plugins")).toBeInTheDocument();
    expect(screen.getByTestId("desk-tab-canvas")).toBeInTheDocument();
  });

  it("switches to the v2 shell when requested via query string", async () => {
    window.history.pushState({}, "", "/?shell=v2");

    vi.mocked(fetch).mockImplementation(async (input) => {
      const url =
        typeof input === "string"
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url;

      if (url.endsWith("/api/system/health")) {
        return jsonResponse({ name: "KodaClaw Gateway", status: "healthy", mode: "Normal" });
      }

      if (url.endsWith("/api/system/bootstrap-state")) {
        return jsonResponse({
          workspaceRootPath: "/tmp/.kodaclaw",
          workspaceVersion: 7,
          workspaceInitialized: true,
          requiresBootstrap: false,
          activeMainSessionId: "main-v2-001",
          mode: "Normal",
        });
      }

      if (url.includes("/api/sessions?limit=8")) {
        return jsonResponse({
          sessions: [
            {
              sessionId: "main-v2-001",
              sessionKind: "Main",
              status: {
                isActiveMainSession: true,
                breakpointState: "Ready",
                messageCount: 12,
                pendingApprovalCount: 1,
              },
              createdAt: "2026-03-20T03:00:00Z",
              lastEventAt: "2026-03-20T03:05:00Z",
            },
            {
              sessionId: "channel-archive-001",
              sessionKind: "ChannelDirectMessage",
              status: {
                isActiveMainSession: false,
                breakpointState: null,
                messageCount: 4,
                pendingApprovalCount: 0,
              },
              createdAt: "2026-03-19T22:00:00Z",
              lastEventAt: "2026-03-19T22:10:00Z",
            },
          ],
        });
      }

      if (url.includes("/api/sessions?limit=20")) {
        return jsonResponse({
          sessions: [
            {
              sessionId: "main-v2-001",
              sessionKind: "Main",
              status: {
                isActiveMainSession: true,
                breakpointState: "Ready",
                messageCount: 12,
                pendingApprovalCount: 1,
              },
              createdAt: "2026-03-20T03:00:00Z",
              lastEventAt: "2026-03-20T03:05:00Z",
            },
            {
              sessionId: "channel-archive-001",
              sessionKind: "ChannelDirectMessage",
              status: {
                isActiveMainSession: false,
                breakpointState: null,
                messageCount: 4,
                pendingApprovalCount: 0,
              },
              createdAt: "2026-03-19T22:00:00Z",
              lastEventAt: "2026-03-19T22:10:00Z",
            },
          ],
        });
      }

      if (url.endsWith("/api/sessions/channel-archive-001")) {
        return jsonResponse({
          sessionId: "channel-archive-001",
          sessionKind: "ChannelDirectMessage",
          status: {
            isActiveMainSession: false,
            breakpointState: null,
            messageCount: 4,
            pendingApprovalCount: 0,
          },
          createdAt: "2026-03-19T22:00:00Z",
          lastEventAt: "2026-03-19T22:10:00Z",
          userMessageCount: 2,
          assistantMessageCount: 2,
          toolCallCount: 0,
          lastSfpIndex: 3,
          pendingApprovalCallIds: [],
        });
      }

      if (url.includes("/api/diagnostics/timeline?sessionId=channel-archive-001&limit=60")) {
        return jsonResponse({
          events: [
            {
              id: "evt-channel-archive-001",
              source: "gateway.diagnostics",
              eventType: "timeline.loaded",
              level: "info",
              message: "timeline-channel-archive-001",
              timestamp: "2026-03-19T22:10:30Z",
              sessionId: "channel-archive-001",
              correlationId: null,
            },
          ],
        });
      }

      if (url.includes("/api/inbox?limit=50")) {
        return jsonResponse({ items: [] });
      }

      if (url.includes("/api/approvals?limit=50")) {
        return jsonResponse({ items: [] });
      }

      throw new Error(`Unexpected fetch request: ${url}`);
    });

    renderApp();

    await waitFor(() => {
      expect(screen.getByTestId("v2-shell")).toBeInTheDocument();
    });

    expect(screen.getByTestId("v2-global-rail")).toBeInTheDocument();
    expect(screen.getByTestId("v2-context-rail")).toBeInTheDocument();
    expect(screen.getByTestId("v2-main-stage")).toBeInTheDocument();
    expect(screen.getByTestId("v2-chat-stage")).toBeInTheDocument();
    expect(screen.getByTestId("v2-chat-context")).toBeInTheDocument();
    expect(within(screen.getByTestId("v2-chat-context")).getByText("main-v2-001")).toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(screen.getByTestId("v2-chat-session-channel-archive-001"));

    await waitFor(() => {
      expect(screen.getByTestId("sessions-diagnostics-desk")).toBeInTheDocument();
    });
    expect(screen.getByTestId("session-detail")).toHaveTextContent("channel-archive-001");
    expect(screen.getByTestId("diagnostics-timeline")).toHaveTextContent("timeline-channel-archive-001");

    await user.click(screen.getByTestId("desk-tab-inbox"));

    await waitFor(() => {
      expect(screen.getByTestId("inbox-approval-desk")).toBeInTheDocument();
    });

    expect(window.localStorage.getItem("kodaclaw.shellVariant")).toBe("v2");
  });

  it("allows forcing the legacy shell with query string override", async () => {
    window.localStorage.setItem("kodaclaw.shellVariant", "v2");
    window.history.pushState({}, "", "/?shell=v1");

    vi.mocked(fetch)
      .mockResolvedValueOnce(
        jsonResponse({ name: "KodaClaw Gateway", status: "healthy", mode: "Bootstrap" }),
      )
      .mockResolvedValueOnce(
        jsonResponse({
          workspaceRootPath: "/tmp/.kodaclaw",
          workspaceVersion: 1,
          workspaceInitialized: true,
          requiresBootstrap: true,
          activeMainSessionId: null,
          mode: "Bootstrap",
        }),
      );

    renderApp();

    await waitFor(() => {
      expect(screen.getByTestId("bootstrap-shell")).toBeInTheDocument();
    });
    expect(screen.queryByTestId("v2-shell")).not.toBeInTheDocument();
  });

  it("switches into automations and canvas desks from the main shell", async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      const url =
        typeof input === "string"
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url;

      if (url.endsWith("/api/system/health")) {
        return jsonResponse({ name: "KodaClaw Gateway", status: "healthy", mode: "Normal" });
      }

      if (url.endsWith("/api/system/bootstrap-state")) {
        return jsonResponse({
          workspaceRootPath: "/tmp/.kodaclaw",
          workspaceVersion: 1,
          workspaceInitialized: true,
          requiresBootstrap: false,
          activeMainSessionId: "main-001",
          mode: "Normal",
        });
      }

      if (url.includes("/api/automations/auto-heartbeat/runs")) {
        return jsonResponse({ items: [automationRunFixture] });
      }

      if (url.endsWith("/api/automations?limit=60")) {
        return jsonResponse({ items: [automationFixture] });
      }

      if (url.endsWith("/api/sessions/auto-session-001")) {
        return jsonResponse(automationSessionDetailFixture);
      }

      if (url.endsWith("/api/canvas/default")) {
        return jsonResponse({
          entryUrl: "/api/canvas/fs/canvas/default/index.html",
          entryPath: "canvas/default/index.html",
          artifactId: null,
          route: "/canvas/default",
          title: "Canvas default entry",
        });
      }

      if (url.endsWith("/api/canvas?limit=60")) {
        return jsonResponse({
          items: [canvasFixture],
          defaultEntryPath: "canvas/default/index.html",
          defaultArtifactId: null,
        });
      }

      if (url.endsWith("/api/plugins?limit=80")) {
        return jsonResponse({
          items: [pluginSummaryFixture],
        });
      }

      if (url.endsWith("/api/plugins/plugin.fixture")) {
        return jsonResponse(pluginDetailFixture);
      }

      if (url.includes("/api/plugins/plugin.fixture/logs")) {
        return jsonResponse([pluginLogFixture]);
      }

      if (url.endsWith("/api/channels/connectors")) {
        return jsonResponse(channelConnectorFixture);
      }

      if (url.includes("/api/channels/accounts")) {
        return jsonResponse([channelAccountFixture]);
      }

      if (url.includes("/api/channels/threads?")) {
        return jsonResponse({ items: [channelThreadFixture] });
      }

      if (url.endsWith("/api/channels/threads/binding-telegram-001")) {
        return jsonResponse(channelThreadDetailFixture);
      }

      if (url.includes("/api/channels/threads/binding-telegram-001/audit")) {
        return jsonResponse([]);
      }

      throw new Error(`Unexpected fetch request: ${url}`);
    });

    renderApp();

    await waitFor(() => {
      expect(screen.getByText("主控对话已激活")).toBeInTheDocument();
    });

    const user = userEvent.setup();
    await user.click(screen.getByTestId("desk-tab-automations"));
    await waitFor(() => {
      expect(screen.getByTestId("automations-desk")).toBeInTheDocument();
    });

    await user.click(screen.getByTestId("desk-tab-canvas"));
    await waitFor(() => {
      expect(screen.getByTestId("canvas-desk")).toBeInTheDocument();
    });

    await user.click(screen.getByTestId("desk-tab-plugins"));
    await waitFor(() => {
      expect(screen.getByTestId("plugins-desk")).toBeInTheDocument();
    });

    await user.click(screen.getByTestId("desk-tab-channels"));
    await waitFor(() => {
      expect(screen.getByTestId("channels-desk")).toBeInTheDocument();
    });
  });

  it("completes bootstrap when onboarding form is submitted", async () => {
    let bootstrapCompleted = false;
    let completionRequest: unknown = null;

    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url =
        typeof input === "string"
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url;

      if (url.endsWith("/api/system/health")) {
        return jsonResponse({
          name: "KodaClaw Gateway",
          status: "healthy",
          mode: bootstrapCompleted ? "Normal" : "Bootstrap",
        });
      }

      if (url.endsWith("/api/system/bootstrap-state")) {
        return jsonResponse({
          workspaceRootPath: "/tmp/.kodaclaw",
          workspaceVersion: 1,
          workspaceInitialized: true,
          requiresBootstrap: !bootstrapCompleted,
          activeMainSessionId: bootstrapCompleted ? "main-001" : null,
          mode: bootstrapCompleted ? "Normal" : "Bootstrap",
        });
      }

      if (url.endsWith("/api/system/bootstrap-complete")) {
        completionRequest = JSON.parse((init?.body as string) ?? "{}");
        bootstrapCompleted = true;

        return jsonResponse({
          workspaceRootPath: "/tmp/.kodaclaw",
          bootstrapCompleted: true,
          identityFilePath: "/tmp/.kodaclaw/workspace/IDENTITY.md",
          soulFilePath: "/tmp/.kodaclaw/workspace/SOUL.md",
          userFilePath: "/tmp/.kodaclaw/workspace/USER.md",
          bootstrapFileArchived: true,
        });
      }

      throw new Error(`Unexpected fetch request: ${url}`);
    });

    renderApp();

    const user = userEvent.setup();
    const identityInput = (await screen.findByTestId("bootstrap-identity-input")) as HTMLTextAreaElement;
    const soulInput = screen.getByTestId("bootstrap-soul-input") as HTMLTextAreaElement;
    const userInput = screen.getByTestId("bootstrap-user-input") as HTMLTextAreaElement;

    await user.clear(identityInput);
    await user.type(identityInput, "# identity guide");
    await user.clear(soulInput);
    await user.type(soulInput, "# soul guide");
    await user.clear(userInput);
    await user.type(userInput, "# user boundaries");
    await user.click(screen.getByTestId("bootstrap-submit"));

    await waitFor(() => {
      expect(completionRequest).toEqual({
        identityMarkdown: "# identity guide",
        soulMarkdown: "# soul guide",
        userMarkdown: "# user boundaries",
        archiveBootstrapFile: true,
      });
    });

    await waitFor(() => {
      expect(screen.getByText("主控对话已激活")).toBeInTheDocument();
    });
    expect(screen.getByText("引导已归档")).toBeInTheDocument();
  });

  it("generates bootstrap draft from onboarding conversation before submission", async () => {
    let draftRequest: unknown = null;

    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url =
        typeof input === "string"
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url;

      if (url.endsWith("/api/system/health")) {
        return jsonResponse({
          name: "KodaClaw Gateway",
          status: "healthy",
          mode: "Bootstrap",
        });
      }

      if (url.endsWith("/api/system/bootstrap-state")) {
        return jsonResponse({
          workspaceRootPath: "/tmp/.kodaclaw",
          workspaceVersion: 1,
          workspaceInitialized: true,
          requiresBootstrap: true,
          activeMainSessionId: null,
          mode: "Bootstrap",
        });
      }

      if (url.endsWith("/api/chat/stream")) {
        const payload = [
          `event: text_chunk\ndata: ${JSON.stringify({ type: "text_chunk", sessionId: "main-bootstrap", delta: "我会优先保护本地数据边界。" })}`,
          `event: done\ndata: ${JSON.stringify({ type: "done", sessionId: "main-bootstrap", reason: "completed" })}`,
          "",
        ].join("\n\n");

        return streamResponse(payload);
      }

      if (url.endsWith("/api/system/bootstrap-draft")) {
        draftRequest = JSON.parse((init?.body as string) ?? "{}");
        return jsonResponse({
          identityMarkdown: "# Koda Identity\n\n- Name: Koda",
          soulMarkdown: "# Koda Soul\n\n- Rule: protect local trust",
          userMarkdown: "# User Profile\n\n- Working style: direct",
          summary: "Captured identity, soul, and user collaboration style from the onboarding chat.",
        });
      }

      throw new Error(`Unexpected fetch request: ${url}`);
    });

    renderApp();

    const user = userEvent.setup();
    await user.type(await screen.findByTestId("chat-input"), "我是你的长期用户，偏好直接沟通。");
    await user.click(screen.getByRole("button", { name: "发送给 Koda" }));

    await waitFor(() => {
      expect(screen.getByText("我会优先保护本地数据边界。")).toBeInTheDocument();
    });

    await user.click(screen.getByTestId("bootstrap-generate-draft"));

    await waitFor(() => {
      expect(draftRequest).toEqual({
        conversation: [
          { role: "user", text: "我是你的长期用户，偏好直接沟通。" },
          { role: "assistant", text: "我会优先保护本地数据边界。" },
        ],
        identityMarkdown: expect.any(String),
        soulMarkdown: expect.any(String),
        userMarkdown: expect.any(String),
      });
    });

    await waitFor(() => {
      expect((screen.getByTestId("bootstrap-identity-input") as HTMLTextAreaElement).value).toContain("Name: Koda");
      expect((screen.getByTestId("bootstrap-soul-input") as HTMLTextAreaElement).value).toContain("protect local trust");
      expect((screen.getByTestId("bootstrap-user-input") as HTMLTextAreaElement).value).toContain("Working style: direct");
      expect(screen.getByTestId("bootstrap-draft-summary")).toHaveTextContent("Captured identity, soul, and user collaboration style");
    });
  });

  it("prefers desktop runtime config and reacts to launch target events", async () => {
    let launchTargetListener: ((target: DesktopLaunchTarget) => void) | null = null;

    window.kodaClawDesktop = {
      getRuntimeConfig: vi.fn().mockResolvedValue({
        gatewayUrl: "http://127.0.0.1:5076",
        gatewayToken: "desktop-token",
        platform: "darwin",
        desktopMode: true,
        gatewayLifecycleMode: "ManagedChild",
        initialTarget: {
          desk: "inbox",
          reason: "notification",
        },
      }),
      onLaunchTarget: (listener) => {
        launchTargetListener = listener;
        return () => {
          launchTargetListener = null;
        };
      },
    };

    await initializeRuntimeConfig();

    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url =
        typeof input === "string"
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url;
      const authorizationHeader = new Headers(init?.headers).get("Authorization");

      expect(authorizationHeader).toBe("Bearer desktop-token");

      if (url === "http://127.0.0.1:5076/api/system/health") {
        return jsonResponse({ name: "KodaClaw Gateway", status: "healthy", mode: "Normal" });
      }

      if (url === "http://127.0.0.1:5076/api/system/bootstrap-state") {
        return jsonResponse({
          workspaceRootPath: "/tmp/.kodaclaw",
          workspaceVersion: 1,
          workspaceInitialized: true,
          requiresBootstrap: false,
          activeMainSessionId: "main-001",
          mode: "Normal",
        });
      }

      if (url.startsWith("http://127.0.0.1:5076/api/inbox")) {
        return jsonResponse({ items: [] });
      }

      if (url.startsWith("http://127.0.0.1:5076/api/approvals")) {
        return jsonResponse({ items: [] });
      }

      if (url === "http://127.0.0.1:5076/api/channels/connectors") {
        return jsonResponse(channelConnectorFixture);
      }

      if (url.startsWith("http://127.0.0.1:5076/api/channels/accounts")) {
        return jsonResponse([channelAccountFixture]);
      }

      if (url.startsWith("http://127.0.0.1:5076/api/channels/threads?")) {
        return jsonResponse({ items: [channelThreadFixture] });
      }

      if (url === "http://127.0.0.1:5076/api/channels/threads/binding-telegram-001") {
        return jsonResponse(channelThreadDetailFixture);
      }

      if (url.startsWith("http://127.0.0.1:5076/api/channels/threads/binding-telegram-001/audit")) {
        return jsonResponse([]);
      }

      throw new Error(`Unexpected fetch request: ${url}`);
    });

    renderApp();

    await waitFor(() => {
      expect(screen.getByTestId("inbox-approval-desk")).toBeInTheDocument();
    });

    if (launchTargetListener) {
      await act(async () => {
        (launchTargetListener as (target: DesktopLaunchTarget) => void)({
          desk: "channels",
          reason: "tray-shortcut",
        });
      });
    }

    await waitFor(() => {
      expect(screen.getByTestId("channels-desk")).toBeInTheDocument();
    });
  });
});
