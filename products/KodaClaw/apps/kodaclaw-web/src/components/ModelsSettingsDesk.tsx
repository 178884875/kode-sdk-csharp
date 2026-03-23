import { FormEvent, useEffect, useMemo, useState } from "react";
import { Skeleton } from "./ui/Skeleton";
import { EmptyState } from "./ui/EmptyState";
import { Cpu, Plus } from "lucide-react";
import {
  createModelEndpoint,
  deleteModelEndpoint,
  fetchModelPresets,
  fetchModels,
  setDefaultModelEndpoint,
  testModelConnection,
  updateModelEndpoint,
} from "../lib/api";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import type {
  CreateModelEndpointRequest,
  ModelConnectionTestResponse,
  ModelEndpoint,
  ModelPreset,
  ModelProviderKind,
  UpdateModelEndpointRequest,
} from "../types/contracts";
import { Modal } from "./ui/Modal";
import { ConfirmModal } from "./ui/ConfirmModal";
import "./ui/Modal.css";
import "./ControlPlaneDesk.css";

// ModelCapabilitySet bitmask constants (mirrors C# enum)
const CAP_TEXT_CHAT        = 1 << 0; // 1
const CAP_TOOL_CALLING     = 1 << 1; // 2
const CAP_VISION           = 1 << 2; // 4
const CAP_IMAGE_GENERATION = 1 << 3; // 8
const CAP_TTS              = 1 << 4; // 16
const CAP_STT              = 1 << 5; // 32
const CAP_EMBEDDINGS       = 1 << 6; // 64

const PROVIDER_DEFAULT_BASE_URLS: Partial<Record<ModelProviderKind, string>> = {
  OpenAI: 'https://api.openai.com/v1',
  Anthropic: 'https://api.anthropic.com',
};
const KNOWN_DEFAULT_URLS = new Set(
  Object.values(PROVIDER_DEFAULT_BASE_URLS).filter(Boolean) as string[]
);

type ModelDraft = {
  displayName: string;
  provider: ModelProviderKind;
  modelId: string;
  baseUrl: string;
  apiKeyEnvironmentVariable: string;
  apiKeyValue: string;
  enabled: boolean;
  capabilities: number;
};

const DEFAULT_MODEL_DRAFT: ModelDraft = {
  displayName: "",
  provider: "OpenAI",
  modelId: "",
  baseUrl: PROVIDER_DEFAULT_BASE_URLS["OpenAI"] ?? "",
  apiKeyEnvironmentVariable: "",
  apiKeyValue: "",
  enabled: true,
  capabilities: CAP_TEXT_CHAT | CAP_TOOL_CALLING,
};

function toOptionalText(value: string): string | null {
  const next = value.trim();
  return next.length > 0 ? next : null;
}

function toCreateRequest(draft: ModelDraft): CreateModelEndpointRequest {
  return {
    displayName: draft.displayName.trim(),
    provider: draft.provider,
    modelId: draft.modelId.trim(),
    baseUrl: toOptionalText(draft.baseUrl),
    apiKeyEnvironmentVariable: toOptionalText(draft.apiKeyEnvironmentVariable),
    apiKeyValue: toOptionalText(draft.apiKeyValue),
    enabled: draft.enabled,
    capabilities: draft.capabilities,
  };
}

function toUpdateRequest(draft: ModelDraft): UpdateModelEndpointRequest {
  return {
    displayName: draft.displayName.trim(),
    provider: draft.provider,
    modelId: draft.modelId.trim(),
    baseUrl: toOptionalText(draft.baseUrl),
    apiKeyEnvironmentVariable: toOptionalText(draft.apiKeyEnvironmentVariable),
    apiKeyValue: toOptionalText(draft.apiKeyValue),
    enabled: draft.enabled,
    capabilities: draft.capabilities,
  };
}

function toDraft(endpoint: ModelEndpoint): ModelDraft {
  return {
    displayName: endpoint.displayName,
    provider: endpoint.provider,
    modelId: endpoint.modelId,
    baseUrl: endpoint.baseUrl ?? "",
    apiKeyEnvironmentVariable: endpoint.apiKeyEnvironmentVariable ?? "",
    apiKeyValue: "",
    enabled: endpoint.enabled,
    capabilities: endpoint.capabilities,
  };
}

export function ModelsSettingsDesk() {
  const { formatDateTime, locale } = useI18n();
  const text = useLocaleText({
    zh: {
      common: {
        none: "暂无",
        notCheckedYet: "尚未检查",
        loading: "加载中…",
        controlPlane: "控制平面",
        defaultBadge: "默认",
        refresh: "刷新面板",
        refreshing: "正在刷新…",
      },
      title: "模型工坊",
      intro: "在可审计工作台中管理模型端点——注册、切换默认、配置 API Key 与能力标签。",
      notes: {
        modelCreated: "模型端点已创建。",
        modelUpdated: "模型端点已更新。",
        modelDefaultSwitched: "默认模型已切换。",
        modelDeleted: "模型端点已删除。",
      },
      errors: {
        loadDesk: "加载模型端点失败。",
        createModel: "创建模型端点失败。",
        updateModel: "更新模型端点失败。",
        setDefault: "设置默认模型端点失败。",
        deleteModel: "删除模型端点失败。",
      },
      sections: {
        modelRegistryEyebrow: "模型注册表",
        modelRegistryTitle: "端点编组",
        modelRegistryLoading: "正在加载模型端点…",
        modelRegistryEmpty: "还没有模型端点，可先在右侧创建。",
        modelComposerEyebrow: "模型编辑器",
        modelComposerCreate: "创建端点",
        modelComposerEdit: "编辑端点",
      },
      stageNav: {
        modelSummary: "创建端点、切换默认模型，或编辑选中的 endpoint。",
      },
      detail: {
        modelFocus: "模型焦点",
        createHint: "选择左侧端点进入编辑，或重置编辑器后创建新端点。",
        providerModel: "提供商 / 模型",
        endpointState: "端点状态",
        capabilities: "支持能力",
        lastUpdated: "最近更新",
        enabled: "已启用",
        disabled: "已停用",
      },
      modelList: {
        edit: "编辑",
        setDefault: "设为默认",
        delete: "删除",
      },
      composer: {
        displayName: "显示名称",
        provider: "提供商",
        modelId: "模型 ID",
        baseUrl: "Base URL（可选）",
        apiKey: "API Key",
        apiKeyConfigured: "已配置",
        apiKeyPlaceholder: "输入后将加密存入本机 Keychain",
        apiKeyEnv: "API Key 环境变量（可选，高级）",
        enabled: "端点已启用",
        capabilitiesTitle: "支持能力",
        capTextChat: "文字对话",
        capToolCalling: "工具调用",
        capVision: "视觉（图片输入）",
        capImageGeneration: "图片生成",
        capTts: "文字转语音",
        capStt: "语音转文字",
        capEmbeddings: "向量嵌入",
        create: "创建模型端点",
        save: "保存端点修改",
        reset: "重置编辑器",
        presetSection: "从预设选择（可选）",
        presetPlaceholder: "选择预设模型",
        baseUrlRequired: "Base URL（必填）",
        testConnection: "测试连接",
        testingConnection: "测试中…",
        cancel: "取消",
        replaceKey: "重新输入",
        connectionOk: "连接成功",
        connectionFail: "连接失败",
      },
      providerLabels: {
        OpenAI: "OpenAI",
        Anthropic: "Anthropic",
        OpenAICompatible: "OpenAI 兼容",
        AnthropicCompatible: "Anthropic 兼容",
      },
    },
    en: {
      common: {
        none: "n/a",
        notCheckedYet: "Not checked yet",
        loading: "Loading...",
        controlPlane: "Control Plane",
        defaultBadge: "Default",
        refresh: "Refresh desk",
        refreshing: "Refreshing...",
      },
      title: "Models Atelier",
      intro: "Manage model endpoints — register, set defaults, configure API keys and capability flags.",
      notes: {
        modelCreated: "Model endpoint created.",
        modelUpdated: "Model endpoint updated.",
        modelDefaultSwitched: "Default model switched.",
        modelDeleted: "Model endpoint deleted.",
      },
      errors: {
        loadDesk: "Failed to load model endpoints.",
        createModel: "Failed to create model endpoint.",
        updateModel: "Failed to update model endpoint.",
        setDefault: "Failed to set default model endpoint.",
        deleteModel: "Failed to delete model endpoint.",
      },
      sections: {
        modelRegistryEyebrow: "Model Registry",
        modelRegistryTitle: "Endpoint Fleet",
        modelRegistryLoading: "Loading model endpoints...",
        modelRegistryEmpty: "No model endpoints yet. Seed one from the composer.",
        modelComposerEyebrow: "Model Composer",
        modelComposerCreate: "Create Endpoint",
        modelComposerEdit: "Edit Endpoint",
      },
      stageNav: {
        modelSummary: "Create endpoints, switch the default, or edit the selected endpoint.",
      },
      detail: {
        modelFocus: "Model focus",
        createHint: "Pick an endpoint from the left to edit it, or reset the composer to create a new one.",
        providerModel: "Provider / Model",
        endpointState: "Endpoint state",
        capabilities: "Capabilities",
        lastUpdated: "Last updated",
        enabled: "Enabled",
        disabled: "Disabled",
      },
      modelList: {
        edit: "Edit",
        setDefault: "Set default",
        delete: "Delete",
      },
      composer: {
        displayName: "Display name",
        provider: "Provider",
        modelId: "Model id",
        baseUrl: "Base URL (optional)",
        apiKey: "API Key",
        apiKeyConfigured: "Configured",
        apiKeyPlaceholder: "Stored encrypted in local Keychain",
        apiKeyEnv: "API key env var (optional, advanced)",
        enabled: "Endpoint enabled",
        capabilitiesTitle: "Capabilities",
        capTextChat: "Text chat",
        capToolCalling: "Tool calling",
        capVision: "Vision (image input)",
        capImageGeneration: "Image generation",
        capTts: "Text-to-speech",
        capStt: "Speech-to-text",
        capEmbeddings: "Embeddings",
        create: "Create model endpoint",
        save: "Save endpoint edits",
        reset: "Reset composer",
        presetSection: "Quick-start from preset (optional)",
        presetPlaceholder: "Choose a preset model",
        baseUrlRequired: "Base URL (required)",
        testConnection: "Test connection",
        testingConnection: "Testing...",
        cancel: "Cancel",
        replaceKey: "Replace",
        connectionOk: "Connected",
        connectionFail: "Failed",
      },
      providerLabels: {
        OpenAI: "OpenAI",
        Anthropic: "Anthropic",
        OpenAICompatible: "OpenAI Compatible",
        AnthropicCompatible: "Anthropic Compatible",
      },
    },
  });

  const [models, setModels] = useState<ModelEndpoint[]>([]);
  const [modelDraft, setModelDraft] = useState<ModelDraft>(DEFAULT_MODEL_DRAFT);
  const [selectedModelId, setSelectedModelId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isMutating, setIsMutating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [note, setNote] = useState<string | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<{ id: string; name: string } | null>(null);
  const [endpointModalOpen, setEndpointModalOpen] = useState(false);

  // Preset selector state
  const [presets, setPresets] = useState<ModelPreset[]>([]);
  const [selectedPresetForFill, setSelectedPresetForFill] = useState<ModelPreset | null>(null);
  const [testResult, setTestResult] = useState<ModelConnectionTestResponse | null>(null);
  const [testingConnection, setTestingConnection] = useState(false);

  const selectedModel = useMemo(
    () => models.find((item) => item.id === selectedModelId) ?? null,
    [models, selectedModelId],
  );

  const formatTimestamp = (value?: string | null): string => {
    return formatDateTime(value, text.common.notCheckedYet);
  };

  async function refreshDesk() {
    setIsLoading(true);
    setError(null);

    try {
      const { items: nextModels } = await fetchModels();
      const nextSelectedModelId =
        selectedModelId && nextModels.some((item) => item.id === selectedModelId)
          ? selectedModelId
          : nextModels.find((item) => item.isDefault)?.id ?? nextModels[0]?.id ?? null;

      setModels(nextModels);
      setSelectedModelId(nextSelectedModelId);

      const nextSelectedModel = nextModels.find((item) => item.id === nextSelectedModelId) ?? null;
      setModelDraft(nextSelectedModel ? toDraft(nextSelectedModel) : DEFAULT_MODEL_DRAFT);

    } catch (nextError) {
      const detail = nextError instanceof Error ? nextError.message : text.errors.loadDesk;
      setError(detail);
    } finally {
      setIsLoading(false);
    }
  }

  useEffect(() => {
    void refreshDesk();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    fetchModelPresets().then(setPresets).catch(() => {});
  }, []);

  useEffect(() => {
    setSelectedModelId((current) => {
      if (current && models.some((item) => item.id === current)) {
        return current;
      }

      return models.find((item) => item.isDefault)?.id ?? models[0]?.id ?? null;
    });
  }, [models]);

  useEffect(() => {
    if (!selectedModel) {
      return;
    }

    setModelDraft(toDraft(selectedModel));
  }, [selectedModel]);

  async function handleCreateModel(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setNote(null);
    setError(null);
    setIsMutating(true);

    try {
      const created = await createModelEndpoint(toCreateRequest(modelDraft));
      const nextModels = [created, ...models];
      setModels(nextModels);
      setSelectedModelId(created.id);
      setModelDraft(toDraft(created));
      setNote(text.notes.modelCreated);
      setEndpointModalOpen(false);
    } catch (nextError) {
      const detail = nextError instanceof Error ? nextError.message : text.errors.createModel;
      setError(detail);
    } finally {
      setIsMutating(false);
    }
  }

  async function handleUpdateModel() {
    if (!selectedModelId) {
      return;
    }

    setNote(null);
    setError(null);
    setIsMutating(true);

    try {
      const updated = await updateModelEndpoint(selectedModelId, toUpdateRequest(modelDraft));
      setModels((current) => current.map((item) => (item.id === updated.id ? updated : item)));
      setModelDraft(toDraft(updated));
      setNote(text.notes.modelUpdated);
      setEndpointModalOpen(false);
    } catch (nextError) {
      const detail = nextError instanceof Error ? nextError.message : text.errors.updateModel;
      setError(detail);
    } finally {
      setIsMutating(false);
    }
  }

  async function handleSetDefault(id: string) {
    setNote(null);
    setError(null);
    setIsMutating(true);

    try {
      const updatedDefault = await setDefaultModelEndpoint(id);
      setModels((current) =>
        current.map((item) =>
          item.id === updatedDefault.id
            ? updatedDefault
            : {
                ...item,
                isDefault: false,
              },
        ),
      );
      if (selectedModelId === updatedDefault.id) {
        setModelDraft(toDraft(updatedDefault));
      }
      setNote(text.notes.modelDefaultSwitched);
    } catch (nextError) {
      const detail = nextError instanceof Error ? nextError.message : text.errors.setDefault;
      setError(detail);
    } finally {
      setIsMutating(false);
    }
  }

  async function handleDeleteModel(id: string) {
    setNote(null);
    setError(null);
    setIsMutating(true);

    try {
      await deleteModelEndpoint(id);
      const nextModels = models.filter((item) => item.id !== id);
      setModels(nextModels);
      if (selectedModelId === id) {
        setSelectedModelId(null);
        setModelDraft(DEFAULT_MODEL_DRAFT);
      }
      setNote(text.notes.modelDeleted);
    } catch (nextError) {
      const detail = nextError instanceof Error ? nextError.message : text.errors.deleteModel;
      setError(detail);
    } finally {
      setIsMutating(false);
    }
  }

  function handleSelectModel(endpoint: ModelEndpoint) {
    setSelectedModelId(endpoint.id);
    setModelDraft(toDraft(endpoint));
    setNote(null);
    setError(null);
  }

  function resetModelComposer() {
    setSelectedModelId(null);
    setModelDraft(DEFAULT_MODEL_DRAFT);
  }

  return (
    <section data-testid="models-settings-desk">
      <h2 className="desk-section-title">{text.title}</h2>
      <p className="desk-section-desc">{text.intro}</p>

      <div className="control-plane-toolbar">
        <button
          className="btn btn--secondary"
          type="button"
          data-testid="models-settings-refresh"
          onClick={() => void refreshDesk()}
          disabled={isLoading || isMutating}
        >
          {isLoading ? text.common.refreshing : text.common.refresh}
        </button>
      </div>

      {error ? (
        <p className="desk-feedback desk-feedback--error" data-testid="models-settings-error">
          {error}
        </p>
      ) : null}
      {note ? (
        <p className="desk-feedback desk-feedback--success" data-testid="models-settings-note">
          {note}
        </p>
      ) : null}

      <section className="timeline" data-testid="models-list">
            <div className="timeline__header">
              <div>
                <h3 className="desk-section-title">{text.sections.modelRegistryTitle}</h3>
                <p className="desk-section-desc">{text.stageNav.modelSummary}</p>
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-3)' }}>
                <span className="composer__status">{isLoading ? text.common.loading : models.length}</span>
                <button
                  className="btn btn--primary"
                  type="button"
                  data-testid="model-create"
                  onClick={() => { resetModelComposer(); setEndpointModalOpen(true); }}
                  disabled={isLoading}
                  style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--space-2)' }}
                >
                  <Plus size={14} strokeWidth={2.5} />
                  {text.sections.modelComposerCreate}
                </button>
              </div>
            </div>
            <div className="timeline__body">
              {isLoading ? <Skeleton height={40} count={4} /> : null}
              {!isLoading && models.length === 0 ? (
                <EmptyState icon={<Cpu size={28} strokeWidth={1.5} />} title={text.sections.modelRegistryEmpty}>
                  <button
                    className="btn btn--primary"
                    type="button"
                    onClick={() => { resetModelComposer(); setEndpointModalOpen(true); }}
                    style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--space-2)' }}
                  >
                    <Plus size={14} strokeWidth={2.5} />
                    {text.sections.modelComposerCreate}
                  </button>
                </EmptyState>
              ) : null}
              <ul className="control-plane-list control-plane-session-list">
                {models.map((item) => {
                  const isSelected = selectedModelId === item.id;
                  return (
                    <li key={item.id}>
                      <article
                        className={`control-plane-session-card control-plane-stack ${isSelected ? "control-plane-list-button--selected" : ""}`}
                        data-testid={`model-item-${item.id}`}
                      >
                        <div className="control-plane-session-card__topline">
                          <span className="metric-label">{text.providerLabels[item.provider]}</span>
                          {item.isDefault ? (
                            <span className="control-plane-chip control-plane-chip--active">{text.common.defaultBadge}</span>
                          ) : null}
                        </div>
                        <strong className="control-plane-session-card__title">{item.displayName}</strong>
                        <div className="control-plane-session-card__meta">
                          <span>{item.modelId}</span>
                          <span>
                            {text.detail.lastUpdated}: {formatTimestamp(item.updatedAt)}
                          </span>
                        </div>
                        <div className="control-plane-item-actions">
                          <button
                            className="btn btn--secondary"
                            type="button"
                            onClick={() => { handleSelectModel(item); setEndpointModalOpen(true); }}
                            disabled={isMutating}
                          >
                            {text.modelList.edit}
                          </button>
                          <button
                            className="btn btn--secondary"
                            type="button"
                            data-testid="model-default"
                            onClick={() => void handleSetDefault(item.id)}
                            disabled={isMutating || item.isDefault || !item.enabled}
                          >
                            {text.modelList.setDefault}
                          </button>
                          <button
                            className="btn btn--danger"
                            type="button"
                            data-testid={`model-delete-${item.id}`}
                            onClick={() => setDeleteConfirm({ id: item.id, name: item.displayName })}
                            disabled={isMutating}
                          >
                            {text.modelList.delete}
                          </button>
                        </div>
                      </article>
                    </li>
                  );
                })}
              </ul>
            </div>
      </section>

      <Modal
        open={endpointModalOpen}
        title={selectedModelId
          ? `${text.modelList.edit}${selectedModel?.displayName ? ` · ${selectedModel.displayName}` : ''}`
          : text.sections.modelComposerCreate}
        onClose={() => { setEndpointModalOpen(false); setTestResult(null); setSelectedPresetForFill(null); }}
        width={560}
        footer={
          <>
            <button type="button" className="btn btn--ghost" onClick={() => { setEndpointModalOpen(false); setTestResult(null); setSelectedPresetForFill(null); }} disabled={isMutating}>
              {text.composer.cancel}
            </button>
            {selectedModelId ? (
              <button
                type="button"
                className="btn btn--primary"
                data-testid="model-update-submit"
                onClick={() => void handleUpdateModel()}
                disabled={isMutating || modelDraft.displayName.trim().length === 0 || modelDraft.modelId.trim().length === 0}
              >
                {isMutating ? (text.common.loading) : text.composer.save}
              </button>
            ) : (
              <button
                type="submit"
                form="model-endpoint-form"
                className="btn btn--primary"
                data-testid="model-create-submit"
                disabled={isMutating || modelDraft.displayName.trim().length === 0 || modelDraft.modelId.trim().length === 0}
              >
                {isMutating ? (text.common.loading) : text.composer.create}
              </button>
            )}
          </>
        }
      >
        <div data-testid="model-detail">
            <form id="model-endpoint-form" className="bootstrap-form" onSubmit={handleCreateModel}>
            <div className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.presetSection}</span>
              <select
                data-testid="preset-model-select"
                className="kc-select"
                value={selectedPresetForFill?.presetId ?? ''}
                onChange={e => {
                  const preset = presets.find(p => p.presetId === e.target.value);
                  if (preset) {
                    setSelectedPresetForFill(preset);
                    setModelDraft(current => ({
                      ...current,
                      displayName: current.displayName || preset.displayName,
                      provider: preset.provider as ModelProviderKind,
                      modelId: preset.modelId,
                      baseUrl: preset.baseUrl ?? PROVIDER_DEFAULT_BASE_URLS[preset.provider as ModelProviderKind] ?? '',
                      capabilities: preset.defaultCapabilities,
                    }));
                    setTestResult(null);
                  } else {
                    setSelectedPresetForFill(null);
                  }
                }}
              >
                <option value="">{text.composer.presetPlaceholder}</option>
                {presets
                  .filter(p => p.provider === modelDraft.provider)
                  .map(p => (
                    <option key={p.presetId} value={p.presetId}>
                      {p.displayName} ({p.tier})
                    </option>
                  ))}
              </select>
            </div>
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.displayName}</span>
              <input
                data-testid="model-display-name"
                className="kc-input"
                value={modelDraft.displayName}
                onChange={(event) => setModelDraft((current) => ({ ...current, displayName: event.target.value }))}
              />
            </label>
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.provider}</span>
              <select
                data-testid="model-provider"
                className="kc-select"
                value={modelDraft.provider}
                onChange={(event) => {
                  const next = event.target.value as ModelProviderKind;
                  const defaultUrl = PROVIDER_DEFAULT_BASE_URLS[next] ?? '';
                  setModelDraft((current) => ({
                    ...current,
                    provider: next,
                    baseUrl:
                      current.baseUrl === '' || KNOWN_DEFAULT_URLS.has(current.baseUrl)
                        ? defaultUrl
                        : current.baseUrl,
                  }));
                  setSelectedPresetForFill(null);
                }}
              >
                <option value="OpenAI">{text.providerLabels.OpenAI}</option>
                <option value="Anthropic">{text.providerLabels.Anthropic}</option>
                <option value="OpenAICompatible">{text.providerLabels.OpenAICompatible}</option>
                <option value="AnthropicCompatible">{text.providerLabels.AnthropicCompatible}</option>
              </select>
            </label>
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.modelId}</span>
              <input
                data-testid="model-id"
                className="kc-input"
                value={modelDraft.modelId}
                onChange={(event) => setModelDraft((current) => ({ ...current, modelId: event.target.value }))}
              />
            </label>
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">
                {modelDraft.provider.includes('Compatible') ? text.composer.baseUrlRequired : text.composer.baseUrl}
              </span>
              <input
                data-testid="model-base-url"
                className="kc-input"
                value={modelDraft.baseUrl}
                onChange={(event) => setModelDraft((current) => ({ ...current, baseUrl: event.target.value }))}
              />
            </label>
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.apiKey}</span>
              {selectedModel?.apiKeySecretRef?.startsWith("platform:models:") && !modelDraft.apiKeyValue ? (
                <div className="api-key-configured-row">
                  <span className="api-key-configured-badge" data-testid="model-api-key-configured">
                    {text.composer.apiKeyConfigured}
                  </span>
                  <button
                    type="button"
                    className="btn btn--link"
                    onClick={() => setModelDraft((current) => ({ ...current, apiKeyValue: " " }))}
                  >
                    {text.composer.replaceKey}
                  </button>
                </div>
              ) : (
                <input
                  data-testid="model-api-key-value"
                  type="password"
                  autoComplete="new-password"
                  className="kc-input"
                  placeholder={text.composer.apiKeyPlaceholder}
                  value={modelDraft.apiKeyValue}
                  onChange={(event) =>
                    setModelDraft((current) => ({ ...current, apiKeyValue: event.target.value }))
                  }
                />
              )}
            </label>
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.apiKeyEnv}</span>
              <input
                data-testid="model-api-key-env"
                className="kc-input"
                value={modelDraft.apiKeyEnvironmentVariable}
                onChange={(event) =>
                  setModelDraft((current) => ({
                    ...current,
                    apiKeyEnvironmentVariable: event.target.value,
                  }))
                }
              />
            </label>
            <div className="bootstrap-form__field">
              <button
                type="button"
                className="btn btn--secondary"
                data-testid="test-connection-btn"
                onClick={() => {
                  setTestingConnection(true);
                  setTestResult(null);
                  testModelConnection({
                    presetId: selectedPresetForFill?.presetId,
                    modelId: modelDraft.modelId || undefined,
                    apiKey: modelDraft.apiKeyValue.trim(),
                    baseUrl: modelDraft.baseUrl || undefined,
                  })
                    .then(result => { setTestResult(result); })
                    .catch(() => { setTestResult({ ok: false, latencyMs: 0, error: 'network_error' }); })
                    .finally(() => { setTestingConnection(false); });
                }}
                disabled={testingConnection || !modelDraft.modelId}
              >
                {testingConnection ? text.composer.testingConnection : text.composer.testConnection}
              </button>
              {testResult && (
                <p
                  className={`desk-feedback ${testResult.ok ? 'desk-feedback--success' : 'desk-feedback--error'}`}
                  data-testid="connection-result"
                >
                  {testResult.ok
                    ? `${text.composer.connectionOk}（${testResult.latencyMs}ms）`
                    : `${text.composer.connectionFail}：${testResult.error ?? 'unknown'}`}
                </p>
              )}
            </div>
            <label className="bootstrap-form__toggle">
              <input
                type="checkbox"
                checked={modelDraft.enabled}
                onChange={(event) => setModelDraft((current) => ({ ...current, enabled: event.target.checked }))}
              />
              <span>{text.composer.enabled}</span>
            </label>
            <div className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.capabilitiesTitle}</span>
              {(
                [
                  [CAP_TEXT_CHAT,        text.composer.capTextChat],
                  [CAP_TOOL_CALLING,     text.composer.capToolCalling],
                  [CAP_VISION,           text.composer.capVision],
                  [CAP_IMAGE_GENERATION, text.composer.capImageGeneration],
                  [CAP_TTS,              text.composer.capTts],
                  [CAP_STT,              text.composer.capStt],
                  [CAP_EMBEDDINGS,       text.composer.capEmbeddings],
                ] as [number, string][]
              ).map(([flag, label]) => (
                <label key={flag} className="bootstrap-form__toggle">
                  <input
                    type="checkbox"
                    checked={Boolean(modelDraft.capabilities & flag)}
                    onChange={(event) =>
                      setModelDraft((current) => ({
                        ...current,
                        capabilities: event.target.checked
                          ? current.capabilities | flag
                          : current.capabilities & ~flag,
                      }))
                    }
                  />
                  <span>{label}</span>
                </label>
              ))}
            </div>

            </form>
        </div>
      </Modal>

      <ConfirmModal
        open={deleteConfirm !== null}
        title={text.modelList.delete}
        description={`确认删除模型端点 "${deleteConfirm?.name}" 吗？此操作不可撤销。`}
        confirmLabel={text.modelList.delete}
        variant="danger"
        busy={isMutating}
        onConfirm={() => { if (deleteConfirm) void handleDeleteModel(deleteConfirm.id); setDeleteConfirm(null); }}
        onCancel={() => setDeleteConfirm(null)}
      />
    </section>
  );
}