import "@testing-library/jest-dom";
import React from "react";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AutomationsDesk } from "../components/AutomationsDesk";
import { renderWithI18n } from "./test-utils";
import {
  fetchAutomationRuns,
  fetchAutomations,
  fetchSessionDetail,
  fetchSettings,
  updateAutomationDefinition,
} from "../lib/api";
import type {
  AutomationDefinition,
  AutomationDefinitionSource,
  AutomationRunRecord,
  SessionDetail,
} from "../types/contracts";

vi.mock("../lib/api", () => ({
  fetchAutomations: vi.fn(),
  fetchAutomationRuns: vi.fn(),
  fetchSessionDetail: vi.fn(),
  fetchSettings: vi.fn(),
  updateAutomationDefinition: vi.fn(),
}));

const automationsApi = vi.mocked(fetchAutomations);
const runsApi = vi.mocked(fetchAutomationRuns);
const sessionDetailApi = vi.mocked(fetchSessionDetail);
const settingsApi = vi.mocked(fetchSettings);
const updateApi = vi.mocked(updateAutomationDefinition);

function buildAutomation(overrides: Partial<AutomationDefinition> = {}): AutomationDefinition {
  return {
    id: "auto-a",
    title: "Daily digest",
    prompt: "Summarize inbox events and produce an operator digest.",
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
    ...overrides,
  };
}

function buildRun(overrides: Partial<AutomationRunRecord> = {}): AutomationRunRecord {
  return {
    runId: "run-a-1",
    automationId: "auto-a",
    status: "Succeeded",
    trigger: "schedule",
    attempt: 1,
    sessionId: "auto-session-a",
    startedAt: "2026-03-18T09:30:00.000Z",
    completedAt: "2026-03-18T09:31:00.000Z",
    summary: "Digest generated.",
    errorMessage: null,
    ...overrides,
  };
}

function buildSessionDetail(overrides: Partial<SessionDetail> = {}): SessionDetail {
  return {
    sessionId: "auto-session-a",
    sessionKind: "Automation",
    status: {
      isActiveMainSession: false,
      breakpointState: "Ready",
      messageCount: 4,
      pendingApprovalCount: 0,
    },
    createdAt: "2026-03-18T09:30:00.000Z",
    lastEventAt: "2026-03-18T09:31:00.000Z",
    userMessageCount: 1,
    assistantMessageCount: 2,
    toolCallCount: 1,
    lastSfpIndex: 3,
    pendingApprovalCallIds: [],
    promptReport: {
      profileId: "Automation",
      systemPrompt: "Automation system prompt",
      characterCount: 348,
      loadedContextFiles: ["workspace/IDENTITY.md", "workspace/tasks/daily-note.md"],
      generatedAt: "2026-03-18T09:30:00.000Z",
      characterBudget: 1200,
      remainingCharacterBudget: 852,
      wasTruncated: false,
      truncatedContextFiles: [],
      truncationNotes: [],
    },
    promptReportDelta: {
      previousGeneratedAt: "2026-03-18T08:30:00.000Z",
      characterCountDelta: 24,
      truncationStateChanged: false,
      addedContextFiles: ["workspace/tasks/daily-note.md"],
      removedContextFiles: [],
    },
    recentPromptReports: [],
    ...overrides,
  };
}

function filterAutomations(
  items: AutomationDefinition[],
  query?: { enabled?: boolean; source?: AutomationDefinitionSource; limit?: number },
): AutomationDefinition[] {
  const enabled = query?.enabled;
  const source = query?.source;

  return items
    .filter((item) => (enabled === undefined ? true : item.enabled === enabled))
    .filter((item) => (source ? item.source === source : true));
}

const defaultSettings = {
  defaultLandingRoute: "/chat",
  theme: "System" as const,
  requireApprovalForExternalActions: true,
  notificationsEnabled: true,
  quietHoursEnabled: false,
  quietHoursStartLocalTime: null,
  quietHoursEndLocalTime: null,
  updatedAt: "2026-03-18T10:00:00Z",
  automationsEnabled: true,
};

describe("AutomationsDesk", () => {
  beforeEach(() => {
    settingsApi.mockResolvedValue(defaultSettings);
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it("loads automations and supports enabled/source filtering with refresh", async () => {
    const allAutomations = [
      buildAutomation({ id: "auto-a", title: "Daily digest", source: "Manual", enabled: true }),
      buildAutomation({
        id: "auto-b",
        title: "Weekly heartbeat check",
        source: "Heartbeat",
        enabled: false,
        schedule: {
          kind: "Weekly",
          daysOfWeek: ["Monday", "Wednesday", "Friday"],
          localTime: "11:15",
        },
      }),
    ];

    automationsApi.mockImplementation(async (query) => ({
      items: filterAutomations(allAutomations, query),
    }));
    runsApi.mockResolvedValue({ items: [] });

    renderWithI18n(<AutomationsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("automations-list")).toBeInTheDocument();
    });
    const list = screen.getByTestId("automations-list");
    expect(within(list).getByText("Daily digest")).toBeInTheDocument();
    expect(within(list).getByText("Weekly heartbeat check")).toBeInTheDocument();

    const user = userEvent.setup();

    await user.selectOptions(screen.getByTestId("automations-enabled-filter"), "enabled");
    await waitFor(() => {
      expect(automationsApi).toHaveBeenLastCalledWith({
        limit: 60,
        enabled: true,
        source: undefined,
      });
    });
    expect(within(list).queryByText("Weekly heartbeat check")).not.toBeInTheDocument();

    await user.selectOptions(screen.getByTestId("automations-enabled-filter"), "all");
    await user.selectOptions(screen.getByTestId("automations-source-filter"), "Heartbeat");
    await waitFor(() => {
      expect(automationsApi).toHaveBeenLastCalledWith({
        limit: 60,
        enabled: undefined,
        source: "Heartbeat",
      });
    });
    expect(within(list).getByText("Weekly heartbeat check")).toBeInTheDocument();
    expect(within(list).queryByText("Daily digest")).not.toBeInTheDocument();

    await user.click(screen.getByTestId("automations-refresh"));
    await waitFor(() => {
      expect(automationsApi).toHaveBeenCalledTimes(5);
    });
  });

  it("selects automation detail and renders prompt/input paths/error/runs", async () => {
    const automations = [
      buildAutomation({ id: "auto-a", title: "Daily digest" }),
      buildAutomation({
        id: "auto-b",
        title: "Pipeline drift watcher",
        source: "Heartbeat",
        prompt:
          "Check pipeline drift for all critical services, summarize risk delta, and emit remediation hints for operators.",
        inputPaths: ["/workspace/pipelines", "/workspace/runbooks"],
        lastError: null,
      }),
    ];

    const runsByAutomation: Record<string, AutomationRunRecord[]> = {
      "auto-a": [buildRun()],
      "auto-b": [
        buildRun({
          runId: "run-b-1",
          automationId: "auto-b",
          status: "Failed",
          summary: "Pipeline drift exceeded threshold.",
          errorMessage: "drift threshold exceeded",
          startedAt: "2026-03-18T10:10:00.000Z",
          completedAt: "2026-03-18T10:12:00.000Z",
        }),
      ],
    };

    automationsApi.mockResolvedValue({ items: automations });
    runsApi.mockImplementation(async (automationId) => ({
      items: runsByAutomation[automationId] ?? [],
    }));
    sessionDetailApi.mockResolvedValue(buildSessionDetail());

    renderWithI18n(<AutomationsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("automation-detail")).toHaveTextContent("Daily digest");
    });

    const user = userEvent.setup();
    await user.click(screen.getByTestId("automation-select-auto-b"));

    await waitFor(() => {
      expect(screen.getByTestId("automation-detail")).toHaveTextContent("Pipeline drift watcher");
    });
    expect(screen.getByTestId("automation-detail-prompt")).toHaveTextContent("Check pipeline drift");
    expect(screen.getByTestId("automation-detail-input-paths")).toHaveTextContent("/workspace/pipelines");
    expect(screen.getByTestId("automation-detail-last-error")).toHaveTextContent("drift threshold exceeded");
    expect(screen.getByTestId("automation-runs")).toHaveTextContent("Pipeline drift exceeded threshold.");
    expect(runsApi).toHaveBeenCalledWith("auto-b", { limit: 12 });
    expect(sessionDetailApi).toHaveBeenCalledWith("auto-session-a");
    expect(screen.getByTestId("automation-prompt-diagnostics")).toHaveTextContent("Automation");
    expect(screen.getByTestId("automation-prompt-diagnostics")).toHaveTextContent("workspace/tasks/daily-note.md");
    expect(screen.getByTestId("automation-prompt-diagnostics")).toHaveTextContent("默认不读取长期记忆");
  });

  it("applies enable/disable toggle and refreshes detail state", async () => {
    let automations = [
      buildAutomation({
        id: "auto-a",
        title: "Daily digest",
        enabled: true,
      }),
    ];

    automationsApi.mockImplementation(async (query) => ({
      items: filterAutomations(automations, query),
    }));
    runsApi.mockResolvedValue({ items: [buildRun()] });
    sessionDetailApi.mockResolvedValue(buildSessionDetail());
    updateApi.mockImplementation(async (id, enabled) => {
      automations = automations.map((item) =>
        item.id === id
          ? {
              ...item,
              enabled,
              updatedAt: "2026-03-18T11:00:00.000Z",
            }
          : item,
      );

      const updated = automations.find((item) => item.id === id);
      if (!updated) {
        throw new Error(`missing automation ${id}`);
      }

      return updated;
    });

    renderWithI18n(<AutomationsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("automation-enabled-chip")).toHaveTextContent("已启用");
    });

    const user = userEvent.setup();
    await user.click(screen.getByTestId("automation-toggle-auto-a"));

    await waitFor(() => {
      expect(updateApi).toHaveBeenCalledWith("auto-a", false);
    });
    await waitFor(() => {
      expect(screen.getByTestId("automation-enabled-chip")).toHaveTextContent("已停用");
    });
    expect(screen.getByTestId("automation-item-auto-a")).toHaveTextContent("已停用");
  });

  it("renders error state when automations request fails", async () => {
    automationsApi.mockRejectedValueOnce(new Error("automations unavailable"));
    runsApi.mockResolvedValue({ items: [] });
    sessionDetailApi.mockResolvedValue(buildSessionDetail());

    renderWithI18n(<AutomationsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("automations-error")).toHaveTextContent("automations unavailable");
    });
  });

  it("shows engine-disabled banner when automationsEnabled is false", async () => {
    settingsApi.mockResolvedValue({ ...defaultSettings, automationsEnabled: false });
    automationsApi.mockResolvedValue({ items: [] });
    runsApi.mockResolvedValue({ items: [] });

    renderWithI18n(<AutomationsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("automations-engine-banner")).toBeInTheDocument();
    });
    expect(screen.getByTestId("automations-engine-banner")).toHaveTextContent("自动化引擎当前已关闭");
  });

  it("does not show engine-disabled banner when automationsEnabled is true", async () => {
    settingsApi.mockResolvedValue({ ...defaultSettings, automationsEnabled: true });
    automationsApi.mockResolvedValue({ items: [] });
    runsApi.mockResolvedValue({ items: [] });

    renderWithI18n(<AutomationsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("automations-desk")).toBeInTheDocument();
    });
    expect(screen.queryByTestId("automations-engine-banner")).not.toBeInTheDocument();
  });
});
