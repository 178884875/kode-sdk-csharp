import "@testing-library/jest-dom";
import React from "react";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CanvasDesk } from "../components/CanvasDesk";
import { renderWithI18n } from "./test-utils";
import {
  buildCanvasEntryUrl,
  fetchCanvasArtifact,
  fetchCanvasArtifactEntry,
  fetchCanvasArtifacts,
  fetchCanvasDefaultEntry,
} from "../lib/api";
import type { CanvasArtifact, CanvasQueryResponse } from "../types/contracts";

vi.mock("../lib/api", () => ({
  fetchCanvasArtifacts: vi.fn(),
  fetchCanvasDefaultEntry: vi.fn(),
  fetchCanvasArtifact: vi.fn(),
  fetchCanvasArtifactEntry: vi.fn(),
  buildCanvasEntryUrl: vi.fn(),
}));

const artifactsApi = vi.mocked(fetchCanvasArtifacts);
const defaultEntryApi = vi.mocked(fetchCanvasDefaultEntry);
const artifactApi = vi.mocked(fetchCanvasArtifact);
const artifactEntryApi = vi.mocked(fetchCanvasArtifactEntry);
const buildEntryUrl = vi.mocked(buildCanvasEntryUrl);

const reportArtifact: CanvasArtifact = {
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

const dashboardArtifact: CanvasArtifact = {
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

const defaultEntryResponse = {
  entryUrl: "/api/canvas/preview/default-token/canvas/default/index.html",
  entryPath: "canvas/default/index.html",
  artifactId: null,
  route: "/canvas/default",
  title: "Canvas default entry",
};

const reportEntryResponse = {
  entryUrl: "/api/canvas/preview/report-token/canvas/launch/index.html",
  entryPath: reportArtifact.entryPath,
  artifactId: reportArtifact.id,
  route: reportArtifact.route,
  title: reportArtifact.title,
};

const dashboardEntryResponse = {
  entryUrl: "/api/canvas/preview/dashboard-token/canvas/ops/index.html",
  entryPath: dashboardArtifact.entryPath,
  artifactId: dashboardArtifact.id,
  route: dashboardArtifact.route,
  title: dashboardArtifact.title,
};

const defaultArtifactsPayload: CanvasQueryResponse = {
  items: [reportArtifact, dashboardArtifact],
  defaultEntryPath: "canvas/default/index.html",
  defaultArtifactId: null,
};

describe("CanvasDesk", () => {
  beforeEach(() => {
    artifactsApi.mockResolvedValue(defaultArtifactsPayload);
    defaultEntryApi.mockResolvedValue(defaultEntryResponse);
    artifactApi.mockImplementation(async (id: string) => {
      if (id === reportArtifact.id) {
        return reportArtifact;
      }

      return dashboardArtifact;
    });
    artifactEntryApi.mockImplementation(async (id: string) => {
      if (id === reportArtifact.id) {
        return reportEntryResponse;
      }

      return dashboardEntryResponse;
    });
    buildEntryUrl.mockImplementation((entryPath: string) => `/api/canvas/fs/${entryPath}`);
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it("loads default entry iframe before any artifact is selected", async () => {
    renderWithI18n(<CanvasDesk />);

    const iframe = (await screen.findByTestId("canvas-entry-frame")) as HTMLIFrameElement;
    expect(iframe.src).toContain("/api/canvas/preview/default-token/canvas/default/index.html");
    expect(screen.getByTestId("canvas-metadata")).toHaveTextContent("/canvas/default");
    expect(screen.getByTestId("canvas-metadata")).toHaveTextContent("暂无");
  });

  it("supports kind filtering and refresh", async () => {
    renderWithI18n(<CanvasDesk />);
    const user = userEvent.setup();

    await screen.findByTestId("canvas-kind-filter");
    await user.selectOptions(screen.getByTestId("canvas-kind-filter"), "Dashboard");

    await waitFor(() => {
      expect(artifactsApi).toHaveBeenLastCalledWith({ kind: "Dashboard", limit: 60 });
    });

    await user.click(screen.getByTestId("canvas-refresh"));
    await waitFor(() => {
      expect(artifactsApi).toHaveBeenCalledTimes(3);
    });
  });

  it("updates iframe src and metadata after selecting artifact", async () => {
    renderWithI18n(<CanvasDesk />);
    const user = userEvent.setup();

    await screen.findByTestId(`canvas-artifact-select-${dashboardArtifact.id}`);
    await user.click(screen.getByTestId(`canvas-artifact-select-${dashboardArtifact.id}`));

    await waitFor(() => {
      expect(artifactApi).toHaveBeenCalledWith(dashboardArtifact.id);
      expect(artifactEntryApi).toHaveBeenCalledWith(dashboardArtifact.id);
    });

    const iframe = screen.getByTestId("canvas-entry-frame") as HTMLIFrameElement;
    expect(iframe.src).toContain("/api/canvas/preview/dashboard-token/canvas/ops/index.html");
    expect(screen.getByTestId("canvas-metadata")).toHaveTextContent("canvas/ops");
    expect(screen.getByTestId("canvas-metadata")).toHaveTextContent("Realtime operations dashboard");
  });

  it("shows fallback content when no artifacts are available", async () => {
    artifactsApi.mockResolvedValueOnce({
      items: [],
      defaultEntryPath: "canvas/fallback/index.html",
      defaultArtifactId: null,
    });
    defaultEntryApi.mockResolvedValueOnce({
      entryUrl: "",
      entryPath: "canvas/fallback/index.html",
      artifactId: null,
      route: "/canvas/fallback",
      title: "Fallback canvas entry",
    });

    renderWithI18n(<CanvasDesk />);

    await screen.findByTestId("canvas-empty-state");
    const iframe = screen.getByTestId("canvas-entry-frame") as HTMLIFrameElement;
    expect(iframe.src).toContain("/api/canvas/fs/canvas/fallback/index.html");
    expect(screen.getByTestId("canvas-empty-state")).toHaveTextContent("当前筛选下暂无制品。预览仍固定在默认画布入口。");
  });
});
