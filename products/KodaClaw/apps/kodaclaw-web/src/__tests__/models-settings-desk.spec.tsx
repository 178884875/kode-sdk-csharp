import "@testing-library/jest-dom";
import React from "react";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ModelsSettingsDesk } from "../components/ModelsSettingsDesk";
import { renderWithI18n } from "./test-utils";
import {
  createModelEndpoint,
  deleteModelEndpoint,
  fetchModels,
  fetchSandboxRiskOverview,
  fetchSettings,
  runUpdateCheck,
  saveSettings,
  setDefaultModelEndpoint,
  updateModelEndpoint,
} from "../lib/api";
import type { KodaClawSettings, ModelEndpoint, SandboxRiskOverviewResponse, UpdateStateResponse } from "../types/contracts";

vi.mock("../lib/api", () => ({
  fetchModels: vi.fn(),
  fetchSettings: vi.fn(),
  fetchSandboxRiskOverview: vi.fn(),
  runUpdateCheck: vi.fn(),
  createModelEndpoint: vi.fn(),
  updateModelEndpoint: vi.fn(),
  deleteModelEndpoint: vi.fn(),
  setDefaultModelEndpoint: vi.fn(),
  saveSettings: vi.fn(),
}));

const modelsApi = vi.mocked(fetchModels);
const settingsApi = vi.mocked(fetchSettings);
const riskOverviewApi = vi.mocked(fetchSandboxRiskOverview);
const updateCheckApi = vi.mocked(runUpdateCheck);
const createApi = vi.mocked(createModelEndpoint);
const updateApi = vi.mocked(updateModelEndpoint);
const deleteApi = vi.mocked(deleteModelEndpoint);
const setDefaultApi = vi.mocked(setDefaultModelEndpoint);
const saveSettingsApi = vi.mocked(saveSettings);

const defaultModels: ModelEndpoint[] = [
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

const defaultSettings: KodaClawSettings = {
  defaultLandingRoute: "/chat",
  theme: "System",
  requireApprovalForExternalActions: true,
  notificationsEnabled: true,
  quietHoursEnabled: false,
  quietHoursStartLocalTime: null,
  quietHoursEndLocalTime: null,
  updatedAt: "2026-03-18T10:00:00Z",
  automationsEnabled: false,
};

const defaultRiskOverview: SandboxRiskOverviewResponse = {
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

const defaultUpdateState: UpdateStateResponse = {
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

describe("ModelsSettingsDesk", () => {
  beforeEach(() => {
    modelsApi.mockResolvedValue({ items: defaultModels });
    settingsApi.mockResolvedValue(defaultSettings);
    riskOverviewApi.mockResolvedValue(defaultRiskOverview);
    updateCheckApi.mockResolvedValue(defaultUpdateState);
    createApi.mockResolvedValue(defaultModels[1]);
    updateApi.mockResolvedValue(defaultModels[1]);
    deleteApi.mockResolvedValue();
    setDefaultApi.mockResolvedValue({
      ...defaultModels[1],
      isDefault: true,
      updatedAt: "2026-03-18T12:00:00Z",
    });
    saveSettingsApi.mockResolvedValue({
      ...defaultSettings,
      theme: "Dark",
      updatedAt: "2026-03-18T12:00:00Z",
    });
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it("switches default model endpoint", async () => {
    const user = userEvent.setup();
    renderWithI18n(<ModelsSettingsDesk />);

    await screen.findByText("端点编组");
    const buttons = screen.getAllByTestId("model-default");
    await user.click(buttons[1]);

    await waitFor(() => {
      expect(setDefaultApi).toHaveBeenCalledWith("model-b");
    });
    expect(screen.getByText("默认模型已切换。")).toBeInTheDocument();
  });

  it("keeps a focused model detail stage in sync with the selected endpoint", async () => {
    const user = userEvent.setup();
    renderWithI18n(<ModelsSettingsDesk />);

    await waitFor(() => {
      expect(screen.getByTestId("model-detail")).toHaveTextContent("OpenAI Core");
    });

    await user.click(
      within(screen.getByTestId("model-item-model-b")).getByRole("button", { name: "编辑" }),
    );

    await waitFor(() => {
      expect(screen.getByTestId("model-detail")).toHaveTextContent("Anthropic Draft");
    });

    expect(screen.getByTestId("model-id")).toHaveValue("claude-3-7-sonnet");
    expect(screen.getByTestId("settings-form")).toBeInTheDocument();
    expect(screen.getByTestId("settings-update-watch")).toBeInTheDocument();
    expect(screen.getByTestId("settings-risk-briefing")).toBeInTheDocument();
  });

  it("saves settings payload", async () => {
    const user = userEvent.setup();
    renderWithI18n(<ModelsSettingsDesk />);

    await screen.findByTestId("settings-form");
    expect(await screen.findByTestId("settings-risk-briefing")).toBeInTheDocument();
    expect(await screen.findByTestId("settings-update-watch")).toBeInTheDocument();
    expect(within(screen.getByTestId("settings-risk-briefing")).getByText("沙箱与风险简报")).toBeInTheDocument();
    expect(within(screen.getByTestId("settings-update-watch")).getByText("更新观察台")).toBeInTheDocument();
    expect(within(screen.getByTestId("settings-risk-briefing")).getByText("Fixture Plugin")).toBeInTheDocument();
    expect(screen.getByTestId("update-component-gateway")).toBeInTheDocument();
    await user.selectOptions(screen.getByTestId("settings-theme"), "Dark");
    await user.click(screen.getByTestId("settings-save"));

    await waitFor(() => {
      expect(saveSettingsApi).toHaveBeenCalledTimes(1);
    });
    expect(saveSettingsApi.mock.calls[0][0]).toMatchObject({
      theme: "Dark",
      defaultLandingRoute: "/chat",
    });
    expect(riskOverviewApi).toHaveBeenCalledTimes(1);
    expect(updateCheckApi).toHaveBeenCalledTimes(1);
    expect(screen.getByText("设置已保存。")).toBeInTheDocument();
  });

  it("runs a manual update check from the operator surface", async () => {
    const user = userEvent.setup();
    renderWithI18n(<ModelsSettingsDesk />);

    await screen.findByTestId("settings-update-watch");
    await user.click(screen.getByTestId("settings-update-check"));

    await waitFor(() => {
      expect(updateCheckApi).toHaveBeenCalledTimes(2);
    });
    expect(screen.getByText("更新检查已完成。")).toBeInTheDocument();
  });

  it("does not open external window when update links use disallowed schemes", async () => {
    updateCheckApi.mockResolvedValueOnce({
      ...defaultUpdateState,
      components: [
        {
          ...defaultUpdateState.components[0],
          releaseNotesUrl: "javascript:alert(1)",
          downloadUrl: "file:///tmp/kodaclaw.pkg",
        },
      ],
    });

    const openSpy = vi.spyOn(window, "open").mockImplementation(() => null);
    const user = userEvent.setup();
    renderWithI18n(<ModelsSettingsDesk />);

    await screen.findByTestId("settings-update-watch");
    await user.click(screen.getByRole("button", { name: "打开发布说明" }));
    await user.click(screen.getByRole("button", { name: "打开手动升级" }));

    expect(openSpy).not.toHaveBeenCalled();
    openSpy.mockRestore();
  });

  it("renders automationsEnabled toggle and includes it in save payload", async () => {
    const user = userEvent.setup();
    renderWithI18n(<ModelsSettingsDesk />);

    await screen.findByTestId("settings-form");
    const toggle = screen.getByTestId("settings-automations-enabled-toggle");
    expect(toggle).toBeInTheDocument();
    expect(toggle).not.toBeChecked();

    await user.click(toggle);
    await user.click(screen.getByTestId("settings-save"));

    await waitFor(() => {
      expect(saveSettingsApi).toHaveBeenCalledTimes(1);
    });
    expect(saveSettingsApi.mock.calls[0][0]).toMatchObject({ automationsEnabled: true });
  });

  it("renders load error state", async () => {
    modelsApi.mockRejectedValueOnce(new Error("models unavailable"));
    renderWithI18n(<ModelsSettingsDesk />);

    await waitFor(() => {
      expect(screen.getByText("models unavailable")).toBeInTheDocument();
    });
    expect(screen.getByTestId("models-settings-desk")).toBeInTheDocument();
  });

  it("keeps settings UI available when risk overview fails", async () => {
    riskOverviewApi.mockRejectedValueOnce(new Error("risk endpoint offline"));
    renderWithI18n(<ModelsSettingsDesk />);

    await screen.findByTestId("settings-form");
    expect(screen.getByTestId("settings-risk-error")).toHaveTextContent("risk endpoint offline");
    expect(screen.getByTestId("settings-risk-briefing")).toBeInTheDocument();
    expect(within(screen.getByTestId("settings-form")).getByText("运行时偏好")).toBeInTheDocument();
  });

  it("keeps the desk available when update check fails", async () => {
    updateCheckApi.mockRejectedValueOnce(new Error("manifest unavailable"));
    renderWithI18n(<ModelsSettingsDesk />);

    await screen.findByTestId("settings-form");
    expect(screen.getByTestId("settings-update-error")).toHaveTextContent("manifest unavailable");
    expect(screen.getByTestId("settings-update-watch")).toBeInTheDocument();
    expect(within(screen.getByTestId("settings-update-watch")).getByText("更新观察台")).toBeInTheDocument();
  });
});
