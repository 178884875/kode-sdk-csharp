import { FormEvent, useEffect, useMemo, useState } from "react";
import { Skeleton } from "./ui/Skeleton";
import { EmptyState } from "./ui/EmptyState";
import { Cpu } from "lucide-react";
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
import "./ControlPlaneDesk.css";

// ModelCapabilitySet bitmask constants (mirrors C# enum)
const CAP_TEXT_CHAT        = 1 << 0; // 1
const CAP_TOOL_CALLING     = 1 << 1; // 2
const CAP_VISION           = 1 << 2; // 4
const CAP_IMAGE_GENERATION = 1 << 3; // 8
const CAP_TTS              = 1 << 4; // 16
const CAP_STT              = 1 << 5; // 32
const CAP_EMBEDDINGS       = 1 << 6; // 64

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
  baseUrl: "",
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

  // Preset selector state
  const [presets, setPresets] = useState<ModelPreset[]>([]);
  const [selectedPresetProvider, setSelectedPresetProvider] = useState<string>('');
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
          className="secondary-button"
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

      <div className="control-plane-pane-shell">
        <div className="control-plane-pane-rail control-plane-stack">
          <section className="timeline" data-testid="models-list">
            <div className="timeline__header">
              <div>
                <h3 className="desk-section-title">{text.sections.modelRegistryTitle}</h3>
                <p className="desk-section-desc">{text.stageNav.modelSummary}</p>
              </div>
              <span className="composer__status">{isLoading ? text.common.loading : models.length}</span>
            </div>
            <div className="timeline__body">
              {isLoading ? <Skeleton height={40} count={4} /> : null}
              {!isLoading && models.length === 0 ? (
                <EmptyState icon={<Cpu size={28} strokeWidth={1.5} />} title={text.sections.modelRegistryEmpty} />
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
                            className="secondary-button"
                            type="button"
                            onClick={() => handleSelectModel(item)}
                            disabled={isMutating}
                          >
                            {text.modelList.edit}
                          </button>
                          <button
                            className="secondary-button"
                            type="button"
                            data-testid="model-default"
                            onClick={() => void handleSetDefault(item.id)}
                            disabled={isMutating || item.isDefault || !item.enabled}
                          >
                            {text.modelList.setDefault}
                          </button>
                          <button
                            className="secondary-button"
                            type="button"
                            data-testid={`model-delete-${item.id}`}
                            onClick={() => void handleDeleteModel(item.id)}
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

        </div>

        <div className="control-plane-pane-stage control-plane-stack">
          <section
            className="status-card status-card--normal control-plane-stage-hero"
            data-testid="model-create"
          >
            <div data-testid="model-detail" className="control-plane-stack">
              <div className="control-plane-stage-hero__header">
                <div>
                  <h3 className="desk-section-title">
                    {selectedModel ? selectedModel.displayName : text.sections.modelComposerCreate}
                  </h3>
                  <p className="desk-section-desc">
                    {selectedModel
                      ? `${text.providerLabels[selectedModel.provider]} · ${selectedModel.modelId}`
                      : text.detail.createHint}
                  </p>
                </div>
                <div className="control-plane-chip-row">
                  {selectedModel?.isDefault ? (
                    <span className="control-plane-chip control-plane-chip--active">{text.common.defaultBadge}</span>
                  ) : null}
                  <span className="control-plane-chip">
                    {selectedModel?.enabled ? text.detail.enabled : text.detail.disabled}
                  </span>
                </div>
              </div>

              {selectedModel ? (
                <div className="control-plane-summary-grid">
                  <div className="metric-item">
                    <span className="metric-label">{text.detail.providerModel}</span>
                    <span className="metric-value">
                      {text.providerLabels[selectedModel.provider]} · {selectedModel.modelId}
                    </span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.detail.lastUpdated}</span>
                    <span className="metric-value">{formatTimestamp(selectedModel.updatedAt)}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.composer.baseUrl}</span>
                    <span className="metric-value metric-value--path">
                      {selectedModel.baseUrl ?? text.common.none}
                    </span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.composer.apiKeyEnv}</span>
                    <span className="metric-value metric-value--path">
                      {selectedModel.apiKeyEnvironmentVariable ?? text.common.none}
                    </span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.detail.endpointState}</span>
                    <span className="metric-value">
                      {selectedModel.enabled ? text.detail.enabled : text.detail.disabled}
                    </span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.detail.capabilities}</span>
                    <span className="metric-value">
                      {[
                        selectedModel.capabilities & CAP_TEXT_CHAT ? text.composer.capTextChat : null,
                        selectedModel.capabilities & CAP_TOOL_CALLING ? text.composer.capToolCalling : null,
                        selectedModel.capabilities & CAP_VISION ? text.composer.capVision : null,
                        selectedModel.capabilities & CAP_IMAGE_GENERATION ? text.composer.capImageGeneration : null,
                        selectedModel.capabilities & CAP_TTS ? text.composer.capTts : null,
                        selectedModel.capabilities & CAP_STT ? text.composer.capStt : null,
                        selectedModel.capabilities & CAP_EMBEDDINGS ? text.composer.capEmbeddings : null,
                      ].filter(Boolean).join(", ") || text.common.none}
                    </span>
                  </div>
                </div>
              ) : null}
            </div>

            <form className="bootstrap-form" onSubmit={handleCreateModel}>
            <div className="bootstrap-form__field">
              <span className="bootstrap-form__label">从预设选择（可选）</span>
              <select
                data-testid="preset-provider-select"
                className="bootstrap-form__textarea control-plane-select"
                value={selectedPresetProvider}
                onChange={e => {
                  setSelectedPresetProvider(e.target.value);
                  setSelectedPresetForFill(null);
                }}
              >
                <option value="">选择 Provider（可选）</option>
                {['Anthropic', 'OpenAI', 'OpenAICompatible', 'AnthropicCompatible'].map(p => (
                  <option key={p} value={p}>{p}</option>
                ))}
              </select>
              {selectedPresetProvider && (
                <select
                  data-testid="preset-model-select"
                  className="bootstrap-form__textarea control-plane-select control-plane-select--mt"
                  value={selectedPresetForFill?.presetId ?? ''}
                  onChange={e => {
                    const preset = presets.find(p => p.presetId === e.target.value);
                    if (preset) {
                      setSelectedPresetForFill(preset);
                      setModelDraft(current => ({
                        ...current,
                        displayName: current.displayName || preset.displayName,
                        modelId: preset.modelId,
                        baseUrl: preset.baseUrl ?? '',
                      }));
                      setTestResult(null);
                    }
                  }}
                >
                  <option value="">选择预设模型</option>
                  {presets
                    .filter(p => p.provider === selectedPresetProvider)
                    .map(p => (
                      <option key={p.presetId} value={p.presetId}>
                        {p.displayName} ({p.tier})
                      </option>
                    ))}
                </select>
              )}
            </div>
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.displayName}</span>
              <input
                data-testid="model-display-name"
                className="bootstrap-form__textarea control-plane-input"
                value={modelDraft.displayName}
                onChange={(event) => setModelDraft((current) => ({ ...current, displayName: event.target.value }))}
              />
            </label>
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.provider}</span>
              <select
                data-testid="model-provider"
                className="bootstrap-form__textarea control-plane-select"
                value={modelDraft.provider}
                onChange={(event) =>
                  setModelDraft((current) => ({
                    ...current,
                    provider: event.target.value as ModelProviderKind,
                  }))
                }
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
                className="bootstrap-form__textarea control-plane-input"
                value={modelDraft.modelId}
                onChange={(event) => setModelDraft((current) => ({ ...current, modelId: event.target.value }))}
              />
            </label>
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.baseUrl}</span>
              <input
                data-testid="model-base-url"
                className="bootstrap-form__textarea control-plane-input"
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
                    className="link-button"
                    onClick={() => setModelDraft((current) => ({ ...current, apiKeyValue: " " }))}
                  >
                    {locale === "zh-CN" ? "重新输入" : "Replace"}
                  </button>
                </div>
              ) : (
                <input
                  data-testid="model-api-key-value"
                  type="password"
                  autoComplete="new-password"
                  className="bootstrap-form__textarea control-plane-input"
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
                className="bootstrap-form__textarea control-plane-input"
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
                className="secondary-button"
                data-testid="test-connection-btn"
                onClick={() => {
                  setTestingConnection(true);
                  setTestResult(null);
                  testModelConnection({
                    presetId: selectedPresetForFill?.presetId,
                    modelId: modelDraft.modelId || undefined,
                    apiKey: '',
                    baseUrl: modelDraft.baseUrl || undefined,
                  })
                    .then(result => { setTestResult(result); })
                    .catch(() => { setTestResult({ ok: false, latencyMs: 0, error: 'network_error' }); })
                    .finally(() => { setTestingConnection(false); });
                }}
                disabled={testingConnection || !modelDraft.modelId}
              >
                {testingConnection ? '测试中...' : '测试连接'}
              </button>
              {testResult && (
                <p
                  className={`desk-feedback ${testResult.ok ? 'desk-feedback--success' : 'desk-feedback--error'}`}
                  data-testid="connection-result"
                >
                  {testResult.ok
                    ? `连接成功（${testResult.latencyMs}ms）`
                    : `连接失败：${testResult.error ?? 'unknown'}`}
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

            <div className="control-plane-inline-actions">
              <button
                className="bootstrap-form__submit"
                data-testid="model-create-submit"
                type="submit"
                disabled={isMutating || modelDraft.displayName.trim().length === 0 || modelDraft.modelId.trim().length === 0}
              >
                {text.composer.create}
              </button>
              <button
                className="secondary-button"
                data-testid="model-update-submit"
                type="button"
                disabled={!selectedModelId || isMutating}
                onClick={() => void handleUpdateModel()}
              >
                {text.composer.save}
              </button>
              <button
                className="secondary-button"
                type="button"
                disabled={isMutating}
                onClick={resetModelComposer}
              >
                {text.composer.reset}
              </button>
            </div>
            </form>
          </section>

        </div>
      </div>
    </section>
  );
}