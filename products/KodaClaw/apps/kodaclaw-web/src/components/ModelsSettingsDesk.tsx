import React, { FormEvent, useEffect, useMemo, useState } from "react";
import { Skeleton } from "./ui/Skeleton";
import { EmptyState } from "./ui/EmptyState";
import { Button } from "./ui/Button";
import { Select } from "./ui/Select";
import { Tooltip } from "./ui/Tooltip";
import { Cpu, Eye, ImageIcon, Layers, MessageSquare, Mic, Plus, RefreshCw, Volume2, Wrench } from "lucide-react";
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
const CAP_TEXT  = 1 << 0; // 1
const CAP_IMAGE = 1 << 1; // 2
const CAP_VIDEO = 1 << 2; // 4
const CAP_FILE  = 1 << 3; // 8
const CAP_AUDIO = 1 << 4; // 16

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
  contextWindowSize: number;
  maxOutputTokens: number;
  isReasoning: boolean;
  customHeaders: Record<string, string>;
};

const DEFAULT_MODEL_DRAFT: ModelDraft = {
  displayName: "",
  provider: "OpenAI",
  modelId: "",
  baseUrl: PROVIDER_DEFAULT_BASE_URLS["OpenAI"] ?? "",
  apiKeyEnvironmentVariable: "",
  apiKeyValue: "",
  enabled: true,
  capabilities: CAP_TEXT,
  contextWindowSize: 128000,
  maxOutputTokens: 8192,
  isReasoning: false,
  customHeaders: {},
};

/** Format large token counts as "128K" / "200K" */
function fmtK(n: number): string {
  return n >= 1000 ? `${Math.round(n / 1000)}K` : String(n);
}

const CAP_ICON_MAP: [number, React.ReactNode, string, string][] = [
  [CAP_TEXT,  <MessageSquare size={12} />, '文本', 'Text'],
  [CAP_IMAGE, <Eye size={12} />,           '图像', 'Image'],
  [CAP_VIDEO, <Layers size={12} />,        '视频', 'Video'],
  [CAP_FILE,  <ImageIcon size={12} />,     '文件', 'File'],
  [CAP_AUDIO, <Mic size={12} />,           '音频', 'Audio'],
];

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
    contextWindowSize: draft.contextWindowSize,
    maxOutputTokens: draft.maxOutputTokens,
    isReasoning: draft.isReasoning,
    customHeaders: Object.keys(draft.customHeaders).length > 0 ? draft.customHeaders : null,
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
    contextWindowSize: draft.contextWindowSize,
    maxOutputTokens: draft.maxOutputTokens,
    isReasoning: draft.isReasoning,
    customHeaders: Object.keys(draft.customHeaders).length > 0 ? draft.customHeaders : null,
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
    contextWindowSize: endpoint.contextWindowSize ?? 128000,
    maxOutputTokens: endpoint.maxOutputTokens ?? 8192,
    isReasoning: endpoint.isReasoning ?? false,
    customHeaders: endpoint.customHeaders ?? {},
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
        capText: "文本对话",
        capImage: "图像输入",
        capVideo: "视频输入",
        capFile: "文件输入",
        capAudio: "音频输入",
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
        errorCodes: {
          authentication_error: "API Key 无效或未授权",
          permission_denied: "权限不足，请检查 API Key 的访问权限",
          rate_limit_exceeded: "触发速率限制，请稍后重试",
          model_not_found: "模型不存在，请确认 Model ID 是否正确",
          model_id_required: "未填写 Model ID",
          timeout: "请求超时（15s），请检查网络或 Base URL",
          network_error: "无法连接到服务器，请检查 Base URL 和网络",
        } as Record<string, string>,
        contextWindowSize: "上下文窗口大小（token）",
        maxOutputTokens: "最大输出 Token",
        isReasoning: "推理模型（禁用工具调用）",
        advancedOptions: "高级选项",
        customHeadersLabel: "自定义请求头",
        customHeadersKeyPlaceholder: "User-Agent",
        customHeadersValuePlaceholder: "claude-code/0.1.0",
        addHeader: "+ 添加请求头",
        removeHeader: "删除",
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
        capText: "Text chat",
        capImage: "Image input",
        capVideo: "Video input",
        capFile: "File input",
        capAudio: "Audio input",
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
        connectionFail: "Connection failed",
        errorCodes: {
          authentication_error: "Invalid or unauthorized API key",
          permission_denied: "Permission denied — check API key access scope",
          rate_limit_exceeded: "Rate limit exceeded, please retry later",
          model_not_found: "Model not found — verify the Model ID",
          model_id_required: "Model ID is required",
          timeout: "Request timed out (15s) — check Base URL and network",
          network_error: "Cannot reach server — check Base URL and network",
        } as Record<string, string>,
        contextWindowSize: "Context window size (tokens)",
        maxOutputTokens: "Max output tokens",
        isReasoning: "Reasoning model (no tool calls)",
        advancedOptions: "Advanced options",
        customHeadersLabel: "Custom request headers",
        customHeadersKeyPlaceholder: "User-Agent",
        customHeadersValuePlaceholder: "claude-code/0.1.0",
        addHeader: "+ Add header",
        removeHeader: "Remove",
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

  const isZh = locale === 'zh-CN';

  return (
    <section data-testid="models-settings-desk">
      <h2 className="desk-section-title">{text.title}</h2>
      <p className="desk-section-desc">{text.intro}</p>

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
              <h3 className="desk-section-title">{text.sections.modelRegistryTitle}</h3>
              <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
                <span className="composer__status">{isLoading ? text.common.loading : models.length}</span>
                <Button
                  variant="ghost"
                  data-testid="models-settings-refresh"
                  onClick={() => void refreshDesk()}
                  disabled={isLoading || isMutating}
                  title={text.common.refresh}
                  style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--space-1)' }}
                >
                  <RefreshCw size={13} strokeWidth={2} className={isLoading ? 'icon-spinning' : ''} />
                </Button>
                <Button
                  variant="primary"
                  data-testid="model-create"
                  onClick={() => { resetModelComposer(); setEndpointModalOpen(true); }}
                  disabled={isLoading}
                  style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--space-2)' }}
                >
                  <Plus size={14} strokeWidth={2.5} />
                  {text.sections.modelComposerCreate}
                </Button>
              </div>
            </div>
            <div className="timeline__body">
              {isLoading ? <Skeleton height={40} count={4} /> : null}
              {!isLoading && models.length === 0 ? (
                <EmptyState icon={<Cpu size={28} strokeWidth={1.5} />} title={text.sections.modelRegistryEmpty}>
                  <Button
                    variant="primary"
                    onClick={() => { resetModelComposer(); setEndpointModalOpen(true); }}
                    style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--space-2)' }}
                  >
                    <Plus size={14} strokeWidth={2.5} />
                    {text.sections.modelComposerCreate}
                  </Button>
                </EmptyState>
              ) : null}
              <ul className="control-plane-list control-plane-session-list">
                {models.map((item) => {
                  const ctxK = item.contextWindowSize ? fmtK(item.contextWindowSize) : null;
                  const maxOutK = item.maxOutputTokens ? fmtK(item.maxOutputTokens) : null;
                  const enabledCaps = CAP_ICON_MAP.filter(([flag]) => item.capabilities & flag);
                  return (
                    <li key={item.id}>
                      <article
                        className={`control-plane-session-card model-endpoint-card ${!item.enabled ? "control-plane-session-card--disabled" : ""}`}
                        data-testid={`model-item-${item.id}`}
                      >
                        {/* Top: provider label + timestamp + badges */}
                        <div className="control-plane-session-card__topline">
                          <span className="metric-label model-endpoint-card__provider">{text.providerLabels[item.provider]}</span>
                          <span className="model-endpoint-card__updated">{formatTimestamp(item.updatedAt)}</span>
                          <div className="control-plane-chip-row">
                            {item.isDefault && (
                              <span className="control-plane-chip control-plane-chip--active">{text.common.defaultBadge}</span>
                            )}
                            {item.isReasoning && (
                              <span className="control-plane-chip">{isZh ? '推理' : 'Reasoning'}</span>
                            )}
                            {!item.enabled && (
                              <span className="control-plane-chip control-plane-chip--inactive">{text.detail.disabled}</span>
                            )}
                          </div>
                        </div>

                        {/* Title + model id */}
                        <div className="model-endpoint-card__title-row">
                          <strong className="control-plane-session-card__title">{item.displayName}</strong>
                          <code className="model-endpoint-card__model-id">{item.modelId}</code>
                        </div>

                        {/* Context / output tokens + capability icons */}
                        <div className="model-endpoint-card__meta-row">
                          {(ctxK || maxOutK) && (
                            <div className="model-endpoint-card__tokens">
                              {ctxK && <span>{ctxK} {isZh ? '上下文' : 'ctx'}</span>}
                              {ctxK && maxOutK && <span className="model-endpoint-card__sep">·</span>}
                              {maxOutK && <span>{maxOutK} {isZh ? '最大输出' : 'max out'}</span>}
                            </div>
                          )}
                          {enabledCaps.length > 0 && (
                            <div className="model-endpoint-card__cap-icons">
                              {enabledCaps.map(([flag, icon, labelZh, labelEn]) => (
                                <Tooltip key={flag} content={isZh ? labelZh : labelEn}>
                                  <span className="model-cap-icon">{icon}</span>
                                </Tooltip>
                              ))}
                            </div>
                          )}
                        </div>

                        {/* Footer: actions only */}
                        <div className="model-endpoint-card__footer">
                          <div className="control-plane-item-actions">
                            <Button
                              variant="secondary"
                              size="sm"
                              onClick={() => { handleSelectModel(item); setEndpointModalOpen(true); }}
                              disabled={isMutating}
                            >
                              {text.modelList.edit}
                            </Button>
                            {!item.isDefault && (
                              <Button
                                variant="ghost"
                                size="sm"
                                data-testid="model-default"
                                onClick={() => void handleSetDefault(item.id)}
                                disabled={isMutating || !item.enabled}
                              >
                                {text.modelList.setDefault}
                              </Button>
                            )}
                            <Button
                              variant="danger"
                              size="sm"
                              data-testid={`model-delete-${item.id}`}
                              onClick={() => setDeleteConfirm({ id: item.id, name: item.displayName })}
                              disabled={isMutating}
                            >
                              {text.modelList.delete}
                            </Button>
                          </div>
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
            <Button variant="ghost" onClick={() => { setEndpointModalOpen(false); setTestResult(null); setSelectedPresetForFill(null); }} disabled={isMutating}>
              {text.composer.cancel}
            </Button>
            {selectedModelId ? (
              <Button
                variant="primary"
                data-testid="model-update-submit"
                onClick={() => void handleUpdateModel()}
                disabled={isMutating || modelDraft.displayName.trim().length === 0 || modelDraft.modelId.trim().length === 0}
              >
                {isMutating ? (text.common.loading) : text.composer.save}
              </Button>
            ) : (
              <Button
                type="submit"
                form="model-endpoint-form"
                variant="primary"
                data-testid="model-create-submit"
                disabled={isMutating || modelDraft.displayName.trim().length === 0 || modelDraft.modelId.trim().length === 0}
              >
                {isMutating ? (text.common.loading) : text.composer.create}
              </Button>
            )}
          </>
        }
      >
        <div data-testid="model-detail">
            <form id="model-endpoint-form" className="bootstrap-form" onSubmit={handleCreateModel}>
            {/* 1. Provider */}
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.provider}</span>
              <Select
                data-testid="model-provider"
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
              </Select>
            </label>
            {/* 2. Preset (filtered by provider) */}
            <div className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.presetSection}</span>
              <Select
                data-testid="preset-model-select"
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
                      contextWindowSize: preset.contextWindowSize ?? current.contextWindowSize,
                      maxOutputTokens: preset.maxOutputTokens ?? current.maxOutputTokens,
                      isReasoning: preset.isReasoning ?? current.isReasoning,
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
              </Select>
            </div>
            {/* 3. Display Name */}
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.displayName}</span>
              <input
                data-testid="model-display-name"
                className="kc-input"
                value={modelDraft.displayName}
                onChange={(event) => setModelDraft((current) => ({ ...current, displayName: event.target.value }))}
              />
            </label>
            {/* 4. Model ID */}
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.modelId}</span>
              <input
                data-testid="model-id"
                className="kc-input"
                value={modelDraft.modelId}
                onChange={(event) => setModelDraft((current) => ({ ...current, modelId: event.target.value }))}
              />
            </label>
            {/* 5. Base URL */}
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
            {/* 6. API Key */}
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.apiKey}</span>
              {selectedModel?.apiKeySecretRef?.startsWith("keychain:models:") && !modelDraft.apiKeyValue ? (
                <div className="api-key-configured-row">
                  <span className="api-key-configured-badge" data-testid="model-api-key-configured">
                    {text.composer.apiKeyConfigured}
                  </span>
                  <Button
                    variant="link"
                    onClick={() => setModelDraft((current) => ({ ...current, apiKeyValue: " " }))}
                  >
                    {text.composer.replaceKey}
                  </Button>
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
            {/* 7. Advanced: API Key env var + Custom Headers */}
            <details className="bootstrap-form__advanced">
              <summary className="bootstrap-form__advanced-toggle">{text.composer.advancedOptions}</summary>
              <label className="bootstrap-form__field" style={{ marginTop: 'var(--space-2)' }}>
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
              <div className="bootstrap-form__field" style={{ marginTop: 'var(--space-2)' }}>
                <span className="bootstrap-form__label">{text.composer.customHeadersLabel}</span>
                {Object.entries(modelDraft.customHeaders).map(([key, value]) => (
                  <div key={key} style={{ display: 'flex', gap: 'var(--space-1)', marginBottom: 'var(--space-1)' }}>
                    <input
                      className="kc-input"
                      style={{ flex: 1 }}
                      placeholder={text.composer.customHeadersKeyPlaceholder}
                      value={key}
                      onChange={(event) => {
                        const newKey = event.target.value;
                        setModelDraft((current) => {
                          const next = { ...current.customHeaders };
                          delete next[key];
                          if (newKey) next[newKey] = value;
                          return { ...current, customHeaders: next };
                        });
                      }}
                    />
                    <input
                      className="kc-input"
                      style={{ flex: 1 }}
                      placeholder={text.composer.customHeadersValuePlaceholder}
                      value={value}
                      onChange={(event) => {
                        const newValue = event.target.value;
                        setModelDraft((current) => ({
                          ...current,
                          customHeaders: { ...current.customHeaders, [key]: newValue },
                        }));
                      }}
                    />
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => {
                        setModelDraft((current) => {
                          const next = { ...current.customHeaders };
                          delete next[key];
                          return { ...current, customHeaders: next };
                        });
                      }}
                    >
                      {text.composer.removeHeader}
                    </Button>
                  </div>
                ))}
                <Button
                  variant="ghost"
                  size="sm"
                  data-testid="add-custom-header"
                  onClick={() => {
                    setModelDraft((current) => ({
                      ...current,
                      customHeaders: { ...current.customHeaders, '': '' },
                    }));
                  }}
                >
                  {text.composer.addHeader}
                </Button>
              </div>
            </details>
            {/* 8. Test connection */}
            <div className="bootstrap-form__field">
              <Button
                variant="secondary"
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
              </Button>
              {testResult && (
                <div
                  className={`desk-feedback ${testResult.ok ? 'desk-feedback--success' : 'desk-feedback--error'}`}
                  data-testid="connection-result"
                >
                  {testResult.ok ? (
                    <span>{text.composer.connectionOk}（{testResult.latencyMs}ms）</span>
                  ) : (
                    <>
                      <span>
                        {text.composer.connectionFail}
                        {testResult.error
                          ? `：${text.composer.errorCodes[testResult.error] ?? testResult.error}`
                          : ''}
                      </span>
                      {testResult.errorMessage && (
                        <pre className="connection-error-detail">{testResult.errorMessage}</pre>
                      )}
                    </>
                  )}
                </div>
              )}
            </div>
            {/* 9. Enabled */}
            <label className="bootstrap-form__toggle">
              <input
                type="checkbox"
                checked={modelDraft.enabled}
                onChange={(event) => setModelDraft((current) => ({ ...current, enabled: event.target.checked }))}
              />
              <span>{text.composer.enabled}</span>
            </label>
            {/* 10. Context window + Max output tokens */}
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.contextWindowSize}</span>
              <input
                data-testid="model-context-window-size"
                type="number"
                min={1}
                className="kc-input"
                value={modelDraft.contextWindowSize}
                onChange={(event) => setModelDraft((current) => ({ ...current, contextWindowSize: Number(event.target.value) || current.contextWindowSize }))}
              />
            </label>
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.maxOutputTokens}</span>
              <input
                data-testid="model-max-output-tokens"
                type="number"
                min={1}
                className="kc-input"
                value={modelDraft.maxOutputTokens}
                onChange={(event) => setModelDraft((current) => ({ ...current, maxOutputTokens: Number(event.target.value) || current.maxOutputTokens }))}
              />
            </label>
            {/* 11. isReasoning */}
            <label className="bootstrap-form__toggle">
              <input
                data-testid="model-is-reasoning"
                type="checkbox"
                checked={modelDraft.isReasoning}
                onChange={(event) => setModelDraft((current) => ({ ...current, isReasoning: event.target.checked }))}
              />
              <span>{text.composer.isReasoning}</span>
            </label>
            {/* 12. Capabilities */}
            <div className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.composer.capabilitiesTitle}</span>
              {(
                [
                  [CAP_TEXT,  text.composer.capText],
                  [CAP_IMAGE, text.composer.capImage],
                  [CAP_VIDEO, text.composer.capVideo],
                  [CAP_FILE,  text.composer.capFile],
                  [CAP_AUDIO, text.composer.capAudio],
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