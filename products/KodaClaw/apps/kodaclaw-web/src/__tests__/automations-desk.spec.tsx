import "@testing-library/jest-dom";
import React from "react";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AutomationsDesk } from "../components/AutomationsDesk";
import { renderWithI18n } from "./test-utils";
import { fetchAutomationRuns, fetchAutomations, updateAutomationDefinition } from "../lib/api";
import type { AutomationDefinition, AutomationDefinitionSource, AutomationRunRecord } from "../types/contracts";

vi.mock("../lib/api", () => ({
  fetchAutomations: vi.fn(),
  fetchAutomationRuns: vi.fn(),
  updateAutomationDefinition: vi.fn(),
}));

const automationsApi = vi.mocked(fetchAutomations);
const runsApi = vi.mocked(fetchAutomationRuns);
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

describe("AutomationsDesk", () => {
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

    expect(screen.getAllByText("正在加载自动化...")).toHaveLength(2);

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

    renderWithI18n(<AutomationsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("automations-error")).toHaveTextContent("automations unavailable");
    });
  });
});
