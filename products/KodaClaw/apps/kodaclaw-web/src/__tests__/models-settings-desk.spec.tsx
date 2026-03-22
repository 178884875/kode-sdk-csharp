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
  fetchModelPresets,
  fetchModels,
  fetchSettings,
  saveSettings,
  setDefaultModelEndpoint,
  testModelConnection,
  updateModelEndpoint,
} from "../lib/api";
import type { KodaClawSettings, ModelEndpoint } from "../types/contracts";

vi.mock("../lib/api", () => ({
  fetchModels: vi.fn(),
  fetchModelPresets: vi.fn(),
  fetchSettings: vi.fn(),
  fetchSandboxRiskOverview: vi.fn(),
  runUpdateCheck: vi.fn(),
  createModelEndpoint: vi.fn(),
  updateModelEndpoint: vi.fn(),
  deleteModelEndpoint: vi.fn(),
  setDefaultModelEndpoint: vi.fn(),
  saveSettings: vi.fn(),
  testModelConnection: vi.fn(),
}));

const modelsApi = vi.mocked(fetchModels);
const presetsApi = vi.mocked(fetchModelPresets);
const settingsApi = vi.mocked(fetchSettings);
const createApi = vi.mocked(createModelEndpoint);
const updateApi = vi.mocked(updateModelEndpoint);
const deleteApi = vi.mocked(deleteModelEndpoint);
const setDefaultApi = vi.mocked(setDefaultModelEndpoint);
const saveSettingsApi = vi.mocked(saveSettings);
const testConnectionApi = vi.mocked(testModelConnection);

const defaultModels: ModelEndpoint[] = [
  {
    id: "model-a",
    displayName: "OpenAI Core",
    provider: "OpenAI",
    modelId: "gpt-4.1",
    baseUrl: null,
    apiKeyEnvironmentVariable: "OPENAI_API_KEY",
    enabled: true,
    capabilities: 7, // TextChat | ToolCalling | Vision
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
    capabilities: 3, // TextChat | ToolCalling
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


describe("ModelsSettingsDesk", () => {
  beforeEach(() => {
    modelsApi.mockResolvedValue({ items: defaultModels });
    presetsApi.mockResolvedValue([]);
    settingsApi.mockResolvedValue(defaultSettings);
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
    testConnectionApi.mockResolvedValue({ ok: true, latencyMs: 42 });
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
  });

  it("saves settings payload", async () => {
    const user = userEvent.setup();
    renderWithI18n(<ModelsSettingsDesk />);

    await screen.findByTestId("settings-form");
    await user.selectOptions(screen.getByTestId("settings-theme"), "Dark");
    await user.click(screen.getByTestId("settings-save"));

    await waitFor(() => {
      expect(saveSettingsApi).toHaveBeenCalledTimes(1);
    });
    expect(saveSettingsApi.mock.calls[0][0]).toMatchObject({
      theme: "Dark",
      defaultLandingRoute: "/chat",
    });
    expect(screen.getByText("设置已保存。")).toBeInTheDocument();
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

});
