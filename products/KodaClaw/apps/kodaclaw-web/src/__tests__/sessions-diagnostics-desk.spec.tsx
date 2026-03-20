import "@testing-library/jest-dom";
import React from "react";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SessionsDiagnosticsDesk } from "../components/SessionsDiagnosticsDesk";
import type { DiagnosticBundleExportResponse } from "../types/contracts";
import { renderWithI18n } from "./test-utils";

vi.mock("../lib/api", () => {
  return {
    exportDiagnosticBundle: vi.fn(),
    fetchSessions: vi.fn(),
    fetchSessionDetail: vi.fn(),
    fetchDiagnosticsTimeline: vi.fn(),
  };
});

vi.mock("../lib/config", () => {
  return {
    getRuntimeConfig: vi.fn(() => ({
      gatewayUrl: "",
      gatewayToken: "",
      platform: "darwin",
      appVersion: "0.1.0",
      releaseChannel: "Stable",
      desktopMode: true,
      initialTarget: null,
      gatewayLifecycleMode: "ManagedChild",
    })),
  };
});

import {
  exportDiagnosticBundle,
  fetchDiagnosticsTimeline,
  fetchSessionDetail,
  fetchSessions,
} from "../lib/api";

const defaultDiagnosticBundleExport: DiagnosticBundleExportResponse = {
  generatedAt: "2026-03-19T10:06:00Z",
  workspaceRootPath: "/tmp/.kodaclaw",
  bundlePath: "/tmp/.kodaclaw/cache/diagnostics/kodaclaw-diagnostic-bundle-20260319-100600.zip",
  manifest: {
    product: "KodaClaw",
    formatVersion: 1,
    generatedAt: "2026-03-19T10:06:00Z",
    archiveName: "kodaclaw-diagnostic-bundle-20260319-100600.zip",
    sourceWorkspaceRoot: "/tmp/.kodaclaw",
    requestedSessionId: "main-001",
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
        "Timeline export is scoped to session 'main-001'.",
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
};

describe("SessionsDiagnosticsDesk", () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it("loads sessions and renders default detail with timeline", async () => {
    vi.mocked(fetchSessions).mockResolvedValue({
      sessions: [
        {
          sessionId: "main-001",
          sessionKind: "Main",
          status: {
            isActiveMainSession: true,
            breakpointState: null,
            messageCount: 8,
            pendingApprovalCount: 1,
          },
          createdAt: "2026-03-18T10:00:00.0000000+00:00",
          lastEventAt: "2026-03-18T10:01:00.0000000+00:00",
        },
      ],
    });

    vi.mocked(fetchSessionDetail).mockResolvedValue({
      sessionId: "main-001",
      sessionKind: "Main",
      status: {
        isActiveMainSession: true,
        breakpointState: "waiting_approval",
        messageCount: 8,
        pendingApprovalCount: 1,
      },
      createdAt: "2026-03-18T10:00:00.0000000+00:00",
      lastEventAt: "2026-03-18T10:01:00.0000000+00:00",
      userMessageCount: 3,
      assistantMessageCount: 3,
      toolCallCount: 2,
      lastSfpIndex: 12,
      pendingApprovalCallIds: ["call-001"],
      promptReport: {
        profileId: "Main",
        systemPrompt: "You are KodaClaw main assistant.\n\nPrompt Profile\nId: Main",
        characterCount: 58,
        loadedContextFiles: ["workspace/IDENTITY.md", "workspace/SOUL.md"],
        generatedAt: "2026-03-18T10:00:30.0000000+00:00",
        characterBudget: 16000,
        remainingCharacterBudget: 15942,
        wasTruncated: false,
        truncatedContextFiles: [],
        truncationNotes: [],
      },
      promptReportDelta: {
        previousGeneratedAt: "2026-03-18T09:59:30.0000000+00:00",
        characterCountDelta: 12,
        truncationStateChanged: false,
        addedContextFiles: ["workspace/SOUL.md"],
        removedContextFiles: [],
      },
      recentPromptReports: [
        {
          profileId: "Main",
          systemPrompt: "You are KodaClaw main assistant.\n\nPrompt Profile\nId: Main",
          characterCount: 58,
          loadedContextFiles: ["workspace/IDENTITY.md", "workspace/SOUL.md"],
          generatedAt: "2026-03-18T10:00:30.0000000+00:00",
          characterBudget: 16000,
          remainingCharacterBudget: 15942,
          wasTruncated: false,
          truncatedContextFiles: [],
          truncationNotes: [],
        },
        {
          profileId: "Main",
          systemPrompt: "You are KodaClaw main assistant.\n\nPrompt Profile\nId: Main",
          characterCount: 46,
          loadedContextFiles: ["workspace/IDENTITY.md"],
          generatedAt: "2026-03-18T09:59:30.0000000+00:00",
          characterBudget: 16000,
          remainingCharacterBudget: 15954,
          wasTruncated: false,
          truncatedContextFiles: [],
          truncationNotes: [],
        },
      ],
    });

    vi.mocked(fetchDiagnosticsTimeline).mockResolvedValue({
      events: [
        {
          id: "evt-001",
          source: "gateway.sessions",
          eventType: "gateway.sessions.fetched",
          level: "info",
          message: "Fetched session detail.",
          timestamp: "2026-03-18T10:01:20.0000000+00:00",
          sessionId: "main-001",
          correlationId: "corr-001",
        },
      ],
    });

    renderWithI18n(<SessionsDiagnosticsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("sessions-list")).toBeInTheDocument();
    });
    await waitFor(() => {
      expect(screen.getByTestId("session-detail")).toHaveTextContent("main-001");
    });
    expect(screen.getByTestId("session-detail")).toHaveTextContent("3 / 3 / 2");
    expect(screen.getByTestId("session-detail")).toHaveTextContent("12");
    expect(screen.getByTestId("session-prompt-report")).toHaveTextContent("Main");
    expect(screen.getByTestId("session-prompt-report")).toHaveTextContent("58 字符");
    expect(screen.getByTestId("session-prompt-report")).toHaveTextContent("58 / 16000 字符，剩余 15942");
    expect(screen.getByTestId("session-prompt-report")).toHaveTextContent("未裁剪");
    expect(screen.getByTestId("session-prompt-report")).toHaveTextContent("+12 字符");
    expect(screen.getByTestId("session-prompt-report")).toHaveTextContent("workspace/SOUL.md");
    expect(screen.getByTestId("session-prompt-report")).toHaveTextContent("最近版本");
    expect(screen.getByTestId("session-prompt-report")).toHaveTextContent("workspace/IDENTITY.md");
    expect(screen.getByTestId("session-prompt-report")).toHaveTextContent("You are KodaClaw main assistant.");
    await waitFor(() => {
      expect(screen.getByTestId("diagnostics-timeline")).toHaveTextContent(
        "Fetched session detail.",
      );
    });
    expect(fetchDiagnosticsTimeline).toHaveBeenCalledWith(
      { sessionId: "main-001", limit: 60 },
    );
  });

  it("switches session and reloads detail/timeline", async () => {
    vi.mocked(fetchSessions).mockResolvedValue({
      sessions: [
        {
          sessionId: "main-001",
          sessionKind: "Main",
          status: {
            isActiveMainSession: true,
            breakpointState: null,
            messageCount: 2,
            pendingApprovalCount: 0,
          },
          createdAt: null,
          lastEventAt: null,
        },
        {
          sessionId: "auto-002",
          sessionKind: "Automation",
          status: {
            isActiveMainSession: false,
            breakpointState: "paused",
            messageCount: 4,
            pendingApprovalCount: 2,
          },
          createdAt: null,
          lastEventAt: null,
        },
      ],
    });

    vi.mocked(fetchSessionDetail).mockImplementation(async (id: string) => {
      return {
        sessionId: id,
        sessionKind: id === "main-001" ? "Main" : "Automation",
        status: {
          isActiveMainSession: id === "main-001",
          breakpointState: id === "main-001" ? null : "paused",
          messageCount: id === "main-001" ? 2 : 4,
          pendingApprovalCount: id === "main-001" ? 0 : 2,
        },
        createdAt: null,
        lastEventAt: null,
        userMessageCount: 0,
        assistantMessageCount: 0,
        toolCallCount: 0,
        lastSfpIndex: 0,
        pendingApprovalCallIds: [],
      };
    });

    vi.mocked(fetchDiagnosticsTimeline).mockImplementation(async (query) => {
      const sessionId = query?.sessionId ?? null;
      return {
        events: [
          {
            id: `evt-${sessionId ?? "none"}`,
            source: "gateway.diagnostics",
            eventType: "timeline.loaded",
            level: "info",
            message: `timeline-${sessionId}`,
            timestamp: "2026-03-18T10:01:20.0000000+00:00",
            sessionId: sessionId ?? null,
            correlationId: null,
          },
        ],
      };
    });

    renderWithI18n(<SessionsDiagnosticsDesk />);

    await screen.findByTestId("session-select-main-001");
    const user = userEvent.setup();
    await user.click(screen.getByTestId("session-select-auto-002"));

    await waitFor(() => {
      expect(screen.getByTestId("session-detail")).toHaveTextContent("auto-002");
    });
    expect(screen.getByTestId("diagnostics-timeline")).toHaveTextContent(
      "timeline-auto-002",
    );
    expect(fetchSessionDetail).toHaveBeenCalledWith("auto-002");
    expect(fetchDiagnosticsTimeline).toHaveBeenCalledWith(
      { sessionId: "auto-002", limit: 60 },
    );
  });

  it("consumes a focus request and opens the requested session detail", async () => {
    const onFocusRequestConsumed = vi.fn();
    vi.mocked(fetchSessions).mockResolvedValue({
      sessions: [
        {
          sessionId: "main-001",
          sessionKind: "Main",
          status: {
            isActiveMainSession: true,
            breakpointState: null,
            messageCount: 2,
            pendingApprovalCount: 0,
          },
          createdAt: null,
          lastEventAt: null,
        },
        {
          sessionId: "channel-002",
          sessionKind: "ChannelDirectMessage",
          status: {
            isActiveMainSession: false,
            breakpointState: null,
            messageCount: 5,
            pendingApprovalCount: 1,
          },
          createdAt: null,
          lastEventAt: null,
        },
      ],
    });

    vi.mocked(fetchSessionDetail).mockImplementation(async (id: string) => ({
      sessionId: id,
      sessionKind: id === "main-001" ? "Main" : "ChannelDirectMessage",
      status: {
        isActiveMainSession: id === "main-001",
        breakpointState: null,
        messageCount: id === "main-001" ? 2 : 5,
        pendingApprovalCount: id === "main-001" ? 0 : 1,
      },
      createdAt: null,
      lastEventAt: null,
      userMessageCount: 0,
      assistantMessageCount: 0,
      toolCallCount: 0,
      lastSfpIndex: 0,
      pendingApprovalCallIds: [],
    }));

    vi.mocked(fetchDiagnosticsTimeline).mockImplementation(async (query) => ({
      events: [
        {
          id: `evt-${query?.sessionId ?? "none"}`,
          source: "gateway.diagnostics",
          eventType: "timeline.loaded",
          level: "info",
          message: `timeline-${query?.sessionId ?? "none"}`,
          timestamp: "2026-03-18T10:01:20.0000000+00:00",
          sessionId: query?.sessionId ?? null,
          correlationId: null,
        },
      ],
    }));

    renderWithI18n(
      <SessionsDiagnosticsDesk
        focusRequest={{ sessionId: "channel-002", requestId: 1 }}
        onFocusRequestConsumed={onFocusRequestConsumed}
      />,
    );

    await waitFor(() => {
      expect(screen.getByTestId("session-detail")).toHaveTextContent("channel-002");
    });
    expect(screen.getByTestId("diagnostics-timeline")).toHaveTextContent("timeline-channel-002");
    expect(onFocusRequestConsumed).toHaveBeenCalledTimes(1);
  });

  it("renders error state when session list request fails", async () => {
    vi.mocked(fetchSessions).mockRejectedValue(new Error("sessions list failed"));

    renderWithI18n(<SessionsDiagnosticsDesk />);

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent("sessions list failed");
    });
    expect(screen.getByTestId("sessions-list")).toBeInTheDocument();
  });

  it("exports a redacted diagnostic bundle with desktop context for the selected session", async () => {
    vi.mocked(fetchSessions).mockResolvedValue({
      sessions: [
        {
          sessionId: "main-001",
          sessionKind: "Main",
          status: {
            isActiveMainSession: true,
            breakpointState: null,
            messageCount: 8,
            pendingApprovalCount: 1,
          },
          createdAt: "2026-03-18T10:00:00.0000000+00:00",
          lastEventAt: "2026-03-18T10:01:00.0000000+00:00",
        },
      ],
    });
    vi.mocked(fetchSessionDetail).mockResolvedValue({
      sessionId: "main-001",
      sessionKind: "Main",
      status: {
        isActiveMainSession: true,
        breakpointState: null,
        messageCount: 8,
        pendingApprovalCount: 1,
      },
      createdAt: "2026-03-18T10:00:00.0000000+00:00",
      lastEventAt: "2026-03-18T10:01:00.0000000+00:00",
      userMessageCount: 3,
      assistantMessageCount: 3,
      toolCallCount: 2,
      lastSfpIndex: 12,
      pendingApprovalCallIds: ["call-001"],
    });
    vi.mocked(fetchDiagnosticsTimeline).mockResolvedValue({ events: [] });
    vi.mocked(exportDiagnosticBundle).mockResolvedValue(defaultDiagnosticBundleExport);

    const user = userEvent.setup();
    renderWithI18n(<SessionsDiagnosticsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("diagnostic-bundle-export-button")).toBeEnabled();
    });
    await user.click(screen.getByTestId("diagnostic-bundle-export-button"));

    await waitFor(() => {
      expect(exportDiagnosticBundle).toHaveBeenCalledTimes(1);
    });
    expect(exportDiagnosticBundle).toHaveBeenCalledWith({
      sessionId: "main-001",
      timelineLimit: 120,
      desktopContext: {
        desktopMode: true,
        platform: "darwin",
        appVersion: "0.1.0",
        releaseChannel: "Stable",
        gatewayLifecycleMode: "ManagedChild",
      },
    });
    expect(screen.getByTestId("diagnostic-bundle-export-note")).toHaveTextContent(
      "诊断包已为 main-001 导出。",
    );
    expect(screen.getByTestId("diagnostic-bundle-export-result")).toHaveTextContent(
      "/tmp/.kodaclaw/cache/diagnostics/kodaclaw-diagnostic-bundle-20260319-100600.zip",
    );
    expect(screen.getByTestId("diagnostic-bundle-redaction-notes")).toHaveTextContent(
      "Timeline export is scoped to session 'main-001'.",
    );
  });
});
