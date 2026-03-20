import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
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
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import { getRuntimeConfig } from "../lib/config";
import type {
  ChannelRiskItem,
  CreateModelEndpointRequest,
  KodaClawSettings,
  ModelEndpoint,
  ModelProviderKind,
  PluginRiskItem,
  SandboxExecutionProfile,
  SandboxRiskOverviewResponse,
  ThemeMode,
  UpdateAvailability,
  UpdateCheckRequest,
  UpdateStateResponse,
  UpdateModelEndpointRequest,
} from "../types/contracts";
import "./ControlPlaneDesk.css";

type ModelDraft = {
  displayName: string;
  provider: ModelProviderKind;
  modelId: string;
  baseUrl: string;
  apiKeyEnvironmentVariable: string;
  enabled: boolean;
  supportsToolCalling: boolean;
};

type DeskStage = "model" | "runtime" | "updates" | "risk";

const DEFAULT_MODEL_DRAFT: ModelDraft = {
  displayName: "",
  provider: "OpenAI",
  modelId: "",
  baseUrl: "",
  apiKeyEnvironmentVariable: "",
  enabled: true,
  supportsToolCalling: true,
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
    enabled: draft.enabled,
    supportsToolCalling: draft.supportsToolCalling,
  };
}

function toUpdateRequest(draft: ModelDraft): UpdateModelEndpointRequest {
  return {
    displayName: draft.displayName.trim(),
    provider: draft.provider,
    modelId: draft.modelId.trim(),
    baseUrl: toOptionalText(draft.baseUrl),
    apiKeyEnvironmentVariable: toOptionalText(draft.apiKeyEnvironmentVariable),
    enabled: draft.enabled,
    supportsToolCalling: draft.supportsToolCalling,
  };
}

function toDraft(endpoint: ModelEndpoint): ModelDraft {
  return {
    displayName: endpoint.displayName,
    provider: endpoint.provider,
    modelId: endpoint.modelId,
    baseUrl: endpoint.baseUrl ?? "",
    apiKeyEnvironmentVariable: endpoint.apiKeyEnvironmentVariable ?? "",
    enabled: endpoint.enabled,
    supportsToolCalling: endpoint.supportsToolCalling,
  };
}

function buildUpdateCheckRequest(): UpdateCheckRequest {
  const runtimeConfig = getRuntimeConfig();
  if (!runtimeConfig.desktopMode || !runtimeConfig.appVersion) {
    return {};
  }

  return {
    desktopCurrentVersion: runtimeConfig.appVersion,
    desktopReleaseChannel: runtimeConfig.releaseChannel,
  };
}

function toSafeExternalUrl(url: string): string | null {
  try {
    const parsed = new URL(url);
    const protocol = parsed.protocol.toLowerCase();
    if (protocol !== "http:" && protocol !== "https:") {
      return null;
    }

    return parsed.toString();
  } catch {
    return null;
  }
}

function openExternalUrl(url: string) {
  const safeUrl = toSafeExternalUrl(url);
  if (!safeUrl) {
    return;
  }

  window.open(safeUrl, "_blank", "noopener,noreferrer");
}

export function ModelsSettingsDesk() {
  const { formatDateTime } = useI18n();
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
      title: "模型与设置工坊",
      intro: "在同一块可审计工作台中维护模型端点、运行时偏好、升级提示与风险简报。",
      notes: {
        updateCheckCompleted: "更新检查已完成。",
        modelCreated: "模型端点已创建。",
        modelUpdated: "模型端点已更新。",
        modelDefaultSwitched: "默认模型已切换。",
        modelDeleted: "模型端点已删除。",
        settingsSaved: "设置已保存。",
      },
      errors: {
        loadDesk: "加载模型/设置失败。",
        loadRisk: "加载沙箱/风险简报失败。",
        loadUpdate: "刷新更新信号失败。",
        createModel: "创建模型端点失败。",
        updateModel: "更新模型端点失败。",
        setDefault: "设置默认模型端点失败。",
        deleteModel: "删除模型端点失败。",
        saveSettings: "保存设置失败。",
      },
      sections: {
        modelRegistryEyebrow: "模型注册表",
        modelRegistryTitle: "端点编组",
        modelRegistryLoading: "正在加载模型端点…",
        modelRegistryEmpty: "还没有模型端点，可先在右侧创建。",
        modelComposerEyebrow: "模型编辑器",
        modelComposerCreate: "创建端点",
        modelComposerEdit: "编辑端点",
        settingsEyebrow: "工作区设置",
        settingsTitle: "运行时偏好",
        settingsLoading: "正在加载设置…",
        updateEyebrow: "发布姿态",
        updateTitle: "更新观察台",
        updateIntro: "跟踪 Gateway / Desktop 当前版本、发布通道与手动升级交接。本面板不会执行静默安装。",
        riskEyebrow: "操作员简报",
        riskTitle: "沙箱与风险简报",
        riskIntro: "诚实展示当前执行边界：现阶段默认是主机本地沙箱 + 边界约束，Docker 仍只是 SDK 支持但未激活的更强选项，插件与渠道能力仍可能扩大外发范围。",
      },
      stageNav: {
        eyebrow: "对象导航",
        title: "右侧舞台",
        model: "模型编辑器",
        modelSummary: "创建端点、切换默认模型，或编辑选中的 endpoint。",
        runtime: "运行时偏好",
        runtimeSummary: "维护默认路由、主题、审批与通知偏好。",
        updates: "更新观察台",
        updatesSummary: "跟踪 Gateway / Desktop 发布信号与手动升级交接。",
        risk: "风险简报",
        riskSummary: "查看沙箱姿态、插件权限与渠道外发风险。",
      },
      detail: {
        modelFocus: "模型焦点",
        createHint: "选择左侧端点进入编辑，或重置编辑器后创建新端点。",
        providerModel: "提供商 / 模型",
        endpointState: "端点状态",
        toolCalling: "工具调用",
        lastUpdated: "最近更新",
        enabled: "已启用",
        disabled: "已停用",
        supported: "已支持",
        unsupported: "未支持",
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
        apiKeyEnv: "API Key 环境变量（可选）",
        enabled: "端点已启用",
        supportsToolCalling: "支持工具调用",
        create: "创建模型端点",
        save: "保存端点修改",
        reset: "重置编辑器",
      },
      settings: {
        defaultLandingRoute: "默认落地路由",
        theme: "主题",
        requireApproval: "外部动作需要审批",
        notificationsEnabled: "启用通知",
        quietHoursEnabled: "启用静默时段",
        quietStart: "静默开始（HH:mm）",
        quietEnd: "静默结束（HH:mm）",
        automationsEnabled: "启用自动化引擎",
        save: "保存设置",
        lastPersisted: "最近持久化",
      },
      update: {
        checkNow: "立即检查",
        checking: "检查中…",
        unavailable: "更新观察台当前不可用：",
        manifestSource: "Manifest 来源",
        evidenceFile: "证据文件",
        generatedAt: "生成时间",
        component: "组件",
        currentVersion: "当前版本",
        latestKnown: "已知最新",
        releaseChannel: "发布通道",
        lastChecked: "最近检查",
        noPublishedVersion: "未发布",
        noReleaseNotes: "当前通道快照未附带发布说明。",
        openReleaseNotes: "打开发布说明",
        openManualUpgrade: "打开手动升级",
        loading: "正在加载更新姿态…",
      },
      risk: {
        unavailable: "沙箱/风险简报当前不可用：",
        executionBoundary: "执行边界",
        executionTitle: "当前沙箱姿态",
        guardrails: "护栏",
        residualRisk: "剩余风险",
        approvalPosture: "审批姿态",
        approvalTitle: "已持久化的操作员偏好",
        externalActions: "外部动作",
        approvalPreferred: "建议审批",
        approvalRelaxed: "审批放宽",
        quietHours: "静默时段",
        quietHoursEnabled: "已启用",
        disabled: "已关闭",
        notifications: "通知",
        enabled: "已启用",
        pluginPermissions: "插件权限",
        pluginTitle: "权限爆炸半径",
        highRiskPlugins: "高风险插件",
        trustBreakdown: "签名 / 信任 / 未信任",
        scopeBreakdown: "网络 / 后台 / Secrets",
        noPluginEscalations: "当前没有可见的插件权限升级。",
        channelOutbound: "渠道外发",
        channelTitle: "回复门控姿态",
        autoSend: "自动发送",
        draftOrApproval: "草稿 / 显式审批",
        pendingApprovals: "待处理审批",
        noChannelRisk: "当前没有活跃渠道线程在扩大外发风险。",
        outboundCapable: "具备外发能力的连接器。",
        inboundOnly: "仅支持入站；风险面主要局限于消息摄取。",
        pendingApprovalHint: "当前有审批等待操作员处理。",
      },
      labels: {
        accountState: "账户",
        trustEvidence: "信任证据",
        noExtraScopes: "没有声明额外权限范围。",
      },
      providerLabels: {
        OpenAI: "OpenAI",
        Anthropic: "Anthropic",
        OpenAICompatible: "OpenAI 兼容",
        AnthropicCompatible: "Anthropic 兼容",
      },
      themeLabels: {
        System: "跟随系统",
        Light: "浅色",
        Dark: "深色",
      },
      deliveryModeLabels: {
        AutoSend: "自动发送",
        DraftApproval: "草稿审批",
        RequireApproval: "显式审批",
      },
      trustStateLabels: {
        Signed: "已签名",
        Trusted: "已信任",
        Untrusted: "未信任",
      },
      executionStatusLabels: {
        active: "当前生效",
        supported: "SDK 支持",
        unsupported: "不可用",
      },
      updateAvailabilityLabels: {
        UpToDate: "已是最新",
        UpdateAvailable: "有可用更新",
        CheckFailed: "检查失败",
        Unknown: "状态未知",
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
      title: "Models & Settings Atelier",
      intro: "Curate model endpoints, runtime preferences, update posture, and risk signals from one inspectable desk.",
      notes: {
        updateCheckCompleted: "Update check completed.",
        modelCreated: "Model endpoint created.",
        modelUpdated: "Model endpoint updated.",
        modelDefaultSwitched: "Default model switched.",
        modelDeleted: "Model endpoint deleted.",
        settingsSaved: "Settings saved.",
      },
      errors: {
        loadDesk: "Failed to load models/settings.",
        loadRisk: "Failed to load sandbox/risk briefing.",
        loadUpdate: "Failed to refresh update signals.",
        createModel: "Failed to create model endpoint.",
        updateModel: "Failed to update model endpoint.",
        setDefault: "Failed to set default model endpoint.",
        deleteModel: "Failed to delete model endpoint.",
        saveSettings: "Failed to save settings.",
      },
      sections: {
        modelRegistryEyebrow: "Model Registry",
        modelRegistryTitle: "Endpoint Fleet",
        modelRegistryLoading: "Loading model endpoints...",
        modelRegistryEmpty: "No model endpoints yet. Seed one from the composer.",
        modelComposerEyebrow: "Model Composer",
        modelComposerCreate: "Create Endpoint",
        modelComposerEdit: "Edit Endpoint",
        settingsEyebrow: "Workspace Settings",
        settingsTitle: "Runtime Preferences",
        settingsLoading: "Loading settings...",
        updateEyebrow: "Release posture",
        updateTitle: "Update Watch",
        updateIntro: "Track the current Gateway/Desktop versions, the selected release channel, and the manual upgrade handoff. This surface never performs a silent install.",
        riskEyebrow: "Operator Briefing",
        riskTitle: "Sandbox & Risk Briefing",
        riskIntro: "Keep the blast radius honest: current sessions run in a host-local sandbox with boundary enforcement, Docker is only an SDK-supported stronger option for now, and plugin/channel surfaces can still widen outbound reach.",
      },
      stageNav: {
        eyebrow: "Object navigation",
        title: "Right-stage focus",
        model: "Model editor",
        modelSummary: "Create endpoints, switch the default, or edit the selected endpoint.",
        runtime: "Runtime preferences",
        runtimeSummary: "Maintain the default route, theme, approval, and notification posture.",
        updates: "Update watch",
        updatesSummary: "Track Gateway/Desktop release signals and the manual upgrade handoff.",
        risk: "Risk briefing",
        riskSummary: "Inspect sandbox posture, plugin permissions, and channel outbound risk.",
      },
      detail: {
        modelFocus: "Model focus",
        createHint: "Pick an endpoint from the left to edit it, or reset the composer to create a new one.",
        providerModel: "Provider / Model",
        endpointState: "Endpoint state",
        toolCalling: "Tool calling",
        lastUpdated: "Last updated",
        enabled: "Enabled",
        disabled: "Disabled",
        supported: "Supported",
        unsupported: "Unsupported",
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
        apiKeyEnv: "API key env var (optional)",
        enabled: "Endpoint enabled",
        supportsToolCalling: "Supports tool calling",
        create: "Create model endpoint",
        save: "Save endpoint edits",
        reset: "Reset composer",
      },
      settings: {
        defaultLandingRoute: "Default landing route",
        theme: "Theme",
        requireApproval: "Require approval for external actions",
        notificationsEnabled: "Notifications enabled",
        quietHoursEnabled: "Quiet hours enabled",
        quietStart: "Quiet start (HH:mm)",
        quietEnd: "Quiet end (HH:mm)",
        automationsEnabled: "Automations engine enabled",
        save: "Save settings",
        lastPersisted: "Last persisted",
      },
      update: {
        checkNow: "Check now",
        checking: "Checking...",
        unavailable: "Update watch is unavailable right now:",
        manifestSource: "Manifest source",
        evidenceFile: "Evidence file",
        generatedAt: "Generated at",
        component: "Component",
        currentVersion: "Current version",
        latestKnown: "Latest known",
        releaseChannel: "Release channel",
        lastChecked: "Last checked",
        noPublishedVersion: "Not published",
        noReleaseNotes: "No release notes were published for this channel snapshot.",
        openReleaseNotes: "Open release notes",
        openManualUpgrade: "Open manual upgrade",
        loading: "Loading current update posture...",
      },
      risk: {
        unavailable: "Sandbox/risk briefing is unavailable right now:",
        executionBoundary: "Execution boundary",
        executionTitle: "Current sandbox posture",
        guardrails: "Guardrails",
        residualRisk: "Residual risk",
        approvalPosture: "Approval posture",
        approvalTitle: "Persisted operator intent",
        externalActions: "External actions",
        approvalPreferred: "Approval preferred",
        approvalRelaxed: "Approval relaxed",
        quietHours: "Quiet hours",
        quietHoursEnabled: "Enabled",
        disabled: "Disabled",
        notifications: "Notifications",
        enabled: "Enabled",
        pluginPermissions: "Plugin permissions",
        pluginTitle: "Permission blast radius",
        highRiskPlugins: "High-risk plugins",
        trustBreakdown: "Signed / Trusted / Untrusted",
        scopeBreakdown: "Network / Background / Secrets",
        noPluginEscalations: "No plugin permission escalations are visible yet.",
        channelOutbound: "Channel outbound",
        channelTitle: "Reply gating posture",
        autoSend: "Auto-send",
        draftOrApproval: "Draft / Explicit approval",
        pendingApprovals: "Pending approvals",
        noChannelRisk: "No active channel threads are widening outbound risk right now.",
        outboundCapable: "Outbound-capable connector.",
        inboundOnly: "Inbound-only connector; blast radius is limited to ingestion.",
        pendingApprovalHint: "An approval is currently waiting on operator review.",
      },
      labels: {
        accountState: "account",
        trustEvidence: "Trust evidence",
        noExtraScopes: "No extra scopes declared.",
      },
      providerLabels: {
        OpenAI: "OpenAI",
        Anthropic: "Anthropic",
        OpenAICompatible: "OpenAI Compatible",
        AnthropicCompatible: "Anthropic Compatible",
      },
      themeLabels: {
        System: "System",
        Light: "Light",
        Dark: "Dark",
      },
      deliveryModeLabels: {
        AutoSend: "Auto-send",
        DraftApproval: "Draft approval",
        RequireApproval: "Explicit approval",
      },
      trustStateLabels: {
        Signed: "Signed",
        Trusted: "Trusted",
        Untrusted: "Untrusted",
      },
      executionStatusLabels: {
        active: "Active profile",
        supported: "Supported by SDK",
        unsupported: "Unavailable",
      },
      updateAvailabilityLabels: {
        UpToDate: "Up to date",
        UpdateAvailable: "Update available",
        CheckFailed: "Check failed",
        Unknown: "Status unknown",
      },
    },
  });

  const [models, setModels] = useState<ModelEndpoint[]>([]);
  const [settings, setSettings] = useState<KodaClawSettings | null>(null);
  const [settingsDraft, setSettingsDraft] = useState<KodaClawSettings | null>(null);
  const [riskOverview, setRiskOverview] = useState<SandboxRiskOverviewResponse | null>(null);
  const [updateState, setUpdateState] = useState<UpdateStateResponse | null>(null);
  const [modelDraft, setModelDraft] = useState<ModelDraft>(DEFAULT_MODEL_DRAFT);
  const [selectedModelId, setSelectedModelId] = useState<string | null>(null);
  const [activeStage, setActiveStage] = useState<DeskStage>("model");
  const [isLoading, setIsLoading] = useState(true);
  const [isMutating, setIsMutating] = useState(false);
  const [isRefreshingUpdate, setIsRefreshingUpdate] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [riskError, setRiskError] = useState<string | null>(null);
  const [updateError, setUpdateError] = useState<string | null>(null);
  const [note, setNote] = useState<string | null>(null);
  const modelStageRef = useRef<HTMLElement | null>(null);
  const runtimeStageRef = useRef<HTMLElement | null>(null);
  const updatesStageRef = useRef<HTMLElement | null>(null);
  const riskStageRef = useRef<HTMLElement | null>(null);

  const selectedModel = useMemo(
    () => models.find((item) => item.id === selectedModelId) ?? null,
    [models, selectedModelId],
  );

  const formatDeliveryMode = (mode: ChannelRiskItem["deliveryMode"]): string => {
    return text.deliveryModeLabels[mode] ?? mode;
  };

  const formatTrustState = (state: PluginRiskItem["trustState"]): string => {
    return text.trustStateLabels[state] ?? state;
  };

  const formatExecutionStatus = (profile: SandboxExecutionProfile): string => {
    if (profile.active) {
      return text.executionStatusLabels.active;
    }

    return profile.supported ? text.executionStatusLabels.supported : text.executionStatusLabels.unsupported;
  };

  const formatUpdateAvailability = (availability: UpdateAvailability): string => {
    return text.updateAvailabilityLabels[availability] ?? text.updateAvailabilityLabels.Unknown;
  };

  const getUpdateAvailabilityClassName = (availability: UpdateAvailability): string => {
    if (availability === "UpToDate") {
      return "risk-briefing__pill risk-briefing__pill--active";
    }

    if (availability === "UpdateAvailable" || availability === "CheckFailed") {
      return "risk-briefing__pill risk-briefing__pill--warning";
    }

    return "risk-briefing__pill";
  };

  const formatTimestamp = (value?: string | null): string => {
    return formatDateTime(value, text.common.notCheckedYet);
  };

  async function refreshDesk() {
    setIsLoading(true);
    setError(null);
    setRiskError(null);
    setUpdateError(null);

    try {
      const [modelsResult, settingsResult, riskResult, updateResult] = await Promise.allSettled([
        fetchModels(),
        fetchSettings(),
        fetchSandboxRiskOverview(),
        runUpdateCheck(buildUpdateCheckRequest()),
      ]);

      if (modelsResult.status === "rejected") {
        throw modelsResult.reason;
      }

      if (settingsResult.status === "rejected") {
        throw settingsResult.reason;
      }

      const nextModels = modelsResult.value.items;
      const nextSelectedModelId =
        selectedModelId && nextModels.some((item) => item.id === selectedModelId)
          ? selectedModelId
          : nextModels.find((item) => item.isDefault)?.id ?? nextModels[0]?.id ?? null;

      setModels(nextModels);
      setSettings(settingsResult.value);
      setSettingsDraft(settingsResult.value);
      setSelectedModelId(nextSelectedModelId);

      const nextSelectedModel = nextModels.find((item) => item.id === nextSelectedModelId) ?? null;
      setModelDraft(nextSelectedModel ? toDraft(nextSelectedModel) : DEFAULT_MODEL_DRAFT);

      if (riskResult.status === "fulfilled") {
        setRiskOverview(riskResult.value);
      } else {
        const detail = riskResult.reason instanceof Error
          ? riskResult.reason.message
          : text.errors.loadRisk;
        setRiskOverview(null);
        setRiskError(detail);
      }

      if (updateResult.status === "fulfilled") {
        setUpdateState(updateResult.value);
      } else {
        const detail = updateResult.reason instanceof Error
          ? updateResult.reason.message
          : text.errors.loadUpdate;
        setUpdateState(null);
        setUpdateError(detail);
      }

    } catch (nextError) {
      const detail = nextError instanceof Error ? nextError.message : text.errors.loadDesk;
      setError(detail);
    } finally {
      setIsLoading(false);
    }
  }

  async function refreshUpdateState() {
    setUpdateError(null);
    setIsRefreshingUpdate(true);

    try {
      const refreshed = await runUpdateCheck(buildUpdateCheckRequest());
      setUpdateState(refreshed);
      setNote(text.notes.updateCheckCompleted);
    } catch (nextError) {
      const detail = nextError instanceof Error ? nextError.message : text.errors.loadUpdate;
      setUpdateError(detail);
    } finally {
      setIsRefreshingUpdate(false);
    }
  }

  useEffect(() => {
    void refreshDesk();
    // eslint-disable-next-line react-hooks/exhaustive-deps
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

  async function handleSaveSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!settingsDraft) {
      return;
    }

    setNote(null);
    setError(null);
    setIsMutating(true);

    try {
      const saved = await saveSettings(settingsDraft);
      setSettings(saved);
      setSettingsDraft(saved);
      setNote(text.notes.settingsSaved);
    } catch (nextError) {
      const detail = nextError instanceof Error ? nextError.message : text.errors.saveSettings;
      setError(detail);
    } finally {
      setIsMutating(false);
    }
  }

  function handleSelectModel(endpoint: ModelEndpoint) {
    setSelectedModelId(endpoint.id);
    setModelDraft(toDraft(endpoint));
    setActiveStage("model");
    setNote(null);
    setError(null);
  }

  function resetModelComposer() {
    setSelectedModelId(null);
    setModelDraft(DEFAULT_MODEL_DRAFT);
    setActiveStage("model");
  }

  function focusStage(stage: DeskStage) {
    setActiveStage(stage);

    const target =
      stage === "model"
        ? modelStageRef.current
        : stage === "runtime"
          ? runtimeStageRef.current
          : stage === "updates"
            ? updatesStageRef.current
            : riskStageRef.current;

    target?.scrollIntoView?.({
      block: "nearest",
      inline: "nearest",
    });
  }

  const stageNavItems: Array<{
    id: DeskStage;
    label: string;
    summary: string;
  }> = [
    {
      id: "model",
      label: text.stageNav.model,
      summary: text.stageNav.modelSummary,
    },
    {
      id: "runtime",
      label: text.stageNav.runtime,
      summary: text.stageNav.runtimeSummary,
    },
    {
      id: "updates",
      label: text.stageNav.updates,
      summary: text.stageNav.updatesSummary,
    },
    {
      id: "risk",
      label: text.stageNav.risk,
      summary: text.stageNav.riskSummary,
    },
  ];

  return (
    <section className="bootstrap-panel" data-testid="models-settings-desk">
      <div className="section-eyebrow">{text.common.controlPlane}</div>
      <h2 className="section-title">{text.title}</h2>
      <p className="section-copy">{text.intro}</p>

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
        <p className="bootstrap-panel__feedback bootstrap-panel__feedback--error" data-testid="models-settings-error">
          {error}
        </p>
      ) : null}
      {note ? (
        <p className="bootstrap-panel__feedback bootstrap-panel__feedback--success" data-testid="models-settings-note">
          {note}
        </p>
      ) : null}

      <div className="control-plane-pane-shell">
        <div className="control-plane-pane-rail control-plane-stack">
          <section className="timeline" data-testid="models-list">
            <div className="timeline__header">
              <div>
                <h3 className="section-title">{text.sections.modelRegistryTitle}</h3>
                <p className="section-copy control-plane-compact-copy">{text.stageNav.modelSummary}</p>
              </div>
              <span className="composer__status">{isLoading ? text.common.loading : models.length}</span>
            </div>
            <div className="timeline__body">
              {isLoading ? <p className="timeline__empty">{text.sections.modelRegistryLoading}</p> : null}
              {!isLoading && models.length === 0 ? (
                <p className="timeline__empty">{text.sections.modelRegistryEmpty}</p>
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

          <section className="status-card status-card--normal" data-testid="settings-sections">
            <div className="section-eyebrow">{text.stageNav.eyebrow}</div>
            <h3 className="section-title">{text.stageNav.title}</h3>
            <ul className="control-plane-list">
              {stageNavItems.map((item) => (
                <li key={item.id}>
                  <button
                    type="button"
                    className={`control-plane-session-card control-plane-list-button ${activeStage === item.id ? "control-plane-list-button--selected" : ""}`}
                    data-testid={`stage-select-${item.id}`}
                    aria-pressed={activeStage === item.id}
                    onClick={() => focusStage(item.id)}
                  >
                    <strong className="control-plane-session-card__title">{item.label}</strong>
                    <span className="control-plane-session-card__meta">{item.summary}</span>
                  </button>
                </li>
              ))}
            </ul>
          </section>
        </div>

        <div className="control-plane-pane-stage control-plane-stack">
          <section
            ref={modelStageRef}
            className="status-card status-card--warning control-plane-stage-hero"
            data-testid="model-create"
          >
            <div data-testid="model-detail" className="control-plane-stack">
              <p className="section-eyebrow">{text.detail.modelFocus}</p>
              <div className="control-plane-stage-hero__header">
                <div>
                  <h3 className="section-title control-plane-card-title">
                    {selectedModel ? selectedModel.displayName : text.sections.modelComposerCreate}
                  </h3>
                  <p className="section-copy control-plane-compact-copy">
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
                  <span className="control-plane-chip">
                    {selectedModel?.supportsToolCalling ? text.detail.supported : text.detail.unsupported}
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
                    <span className="metric-label">{text.detail.toolCalling}</span>
                    <span className="metric-value">
                      {selectedModel.supportsToolCalling ? text.detail.supported : text.detail.unsupported}
                    </span>
                  </div>
                </div>
              ) : null}
            </div>

            <form className="bootstrap-form" onSubmit={handleCreateModel}>
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
            <label className="bootstrap-form__toggle">
              <input
                type="checkbox"
                checked={modelDraft.enabled}
                onChange={(event) => setModelDraft((current) => ({ ...current, enabled: event.target.checked }))}
              />
              <span>{text.composer.enabled}</span>
            </label>
            <label className="bootstrap-form__toggle">
              <input
                type="checkbox"
                checked={modelDraft.supportsToolCalling}
                onChange={(event) =>
                  setModelDraft((current) => ({ ...current, supportsToolCalling: event.target.checked }))
                }
              />
              <span>{text.composer.supportsToolCalling}</span>
            </label>

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

          <section
            ref={runtimeStageRef}
            className="status-card status-card--normal control-plane-stage-panel"
            data-testid="settings-form"
          >
            <div className="section-eyebrow">{text.sections.settingsEyebrow}</div>
            <h3 className="section-title control-plane-card-title">{text.sections.settingsTitle}</h3>
            {settingsDraft ? (
              <form className="bootstrap-form" onSubmit={handleSaveSettings}>
            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.settings.defaultLandingRoute}</span>
              <input
                data-testid="settings-route"
                className="bootstrap-form__textarea control-plane-input"
                value={settingsDraft.defaultLandingRoute}
                onChange={(event) =>
                  setSettingsDraft((current) =>
                    current
                      ? {
                          ...current,
                          defaultLandingRoute: event.target.value,
                        }
                      : current,
                  )
                }
              />
            </label>

            <label className="bootstrap-form__field">
              <span className="bootstrap-form__label">{text.settings.theme}</span>
              <select
                data-testid="settings-theme"
                className="bootstrap-form__textarea control-plane-select"
                value={settingsDraft.theme}
                onChange={(event) =>
                  setSettingsDraft((current) =>
                    current
                      ? {
                          ...current,
                          theme: event.target.value as ThemeMode,
                        }
                      : current,
                  )
                }
              >
                <option value="System">{text.themeLabels.System}</option>
                <option value="Light">{text.themeLabels.Light}</option>
                <option value="Dark">{text.themeLabels.Dark}</option>
              </select>
            </label>

            <label className="bootstrap-form__toggle">
              <input
                data-testid="settings-require-approval"
                type="checkbox"
                checked={settingsDraft.requireApprovalForExternalActions}
                onChange={(event) =>
                  setSettingsDraft((current) =>
                    current
                      ? {
                          ...current,
                          requireApprovalForExternalActions: event.target.checked,
                        }
                      : current,
                  )
                }
              />
              <span>{text.settings.requireApproval}</span>
            </label>

            <label className="bootstrap-form__toggle">
              <input
                data-testid="settings-notifications"
                type="checkbox"
                checked={settingsDraft.notificationsEnabled}
                onChange={(event) =>
                  setSettingsDraft((current) =>
                    current
                      ? {
                          ...current,
                          notificationsEnabled: event.target.checked,
                        }
                      : current,
                  )
                }
              />
              <span>{text.settings.notificationsEnabled}</span>
            </label>

            <label className="bootstrap-form__toggle">
              <input
                data-testid="settings-quiet-hours"
                type="checkbox"
                checked={settingsDraft.quietHoursEnabled}
                onChange={(event) =>
                  setSettingsDraft((current) =>
                    current
                      ? {
                          ...current,
                          quietHoursEnabled: event.target.checked,
                        }
                      : current,
                  )
                }
              />
              <span>{text.settings.quietHoursEnabled}</span>
            </label>

            <div className="control-plane-quiet-hours-grid">
              <label className="bootstrap-form__field">
                <span className="bootstrap-form__label">{text.settings.quietStart}</span>
                <input
                  data-testid="settings-quiet-start"
                  className="bootstrap-form__textarea control-plane-input"
                  value={settingsDraft.quietHoursStartLocalTime ?? ""}
                  onChange={(event) =>
                    setSettingsDraft((current) =>
                      current
                        ? {
                            ...current,
                            quietHoursStartLocalTime: toOptionalText(event.target.value),
                          }
                        : current,
                    )
                  }
                />
              </label>
              <label className="bootstrap-form__field">
                <span className="bootstrap-form__label">{text.settings.quietEnd}</span>
                <input
                  data-testid="settings-quiet-end"
                  className="bootstrap-form__textarea control-plane-input"
                  value={settingsDraft.quietHoursEndLocalTime ?? ""}
                  onChange={(event) =>
                    setSettingsDraft((current) =>
                      current
                        ? {
                            ...current,
                            quietHoursEndLocalTime: toOptionalText(event.target.value),
                          }
                        : current,
                    )
                  }
                />
              </label>
            </div>

            <label className="bootstrap-form__toggle">
              <input
                data-testid="settings-automations-enabled-toggle"
                type="checkbox"
                checked={settingsDraft.automationsEnabled}
                onChange={(event) =>
                  setSettingsDraft((current) =>
                    current
                      ? {
                          ...current,
                          automationsEnabled: event.target.checked,
                        }
                      : current,
                  )
                }
              />
              <span>{text.settings.automationsEnabled}</span>
            </label>

            <div className="control-plane-inline-actions">
              <button className="bootstrap-form__submit" data-testid="settings-save" type="submit" disabled={isMutating}>
                {text.settings.save}
              </button>
              <span className="metric-label">
                {text.settings.lastPersisted}: {formatDateTime(settings?.updatedAt, text.common.none)}
              </span>
            </div>
              </form>
            ) : (
              <p className="section-copy">{text.sections.settingsLoading}</p>
            )}
          </section>

          <section
            ref={updatesStageRef}
            className="status-card status-card--normal update-watch control-plane-stage-panel"
            data-testid="settings-update-watch"
          >
        <div className="update-watch__header">
          <div>
            <div className="section-eyebrow">{text.sections.updateEyebrow}</div>
            <h3 className="section-title control-plane-card-title">{text.sections.updateTitle}</h3>
            <p className="section-copy control-plane-compact-copy">
              {text.sections.updateIntro}
            </p>
          </div>
          <button
            className="secondary-button"
            type="button"
            data-testid="settings-update-check"
            onClick={() => void refreshUpdateState()}
            disabled={isLoading || isRefreshingUpdate}
          >
            {isRefreshingUpdate ? text.update.checking : text.update.checkNow}
          </button>
        </div>

        {updateError ? (
          <p className="bootstrap-panel__feedback bootstrap-panel__feedback--error" data-testid="settings-update-error">
            {text.update.unavailable} {updateError}
          </p>
        ) : null}

        {updateState ? (
          <>
            <div className="update-watch__meta">
              <div className="metric-item">
                <span className="metric-label">{text.update.manifestSource}</span>
                <strong className="metric-value metric-value--path">{updateState.manifestSource}</strong>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.update.evidenceFile}</span>
                <strong className="metric-value metric-value--path">{updateState.artifactPath}</strong>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.update.generatedAt}</span>
                <strong className="metric-value">{formatTimestamp(updateState.generatedAt)}</strong>
              </div>
            </div>

            <div className="update-watch__banner">
              {updateState.operatorNotes.map((noteItem) => (
                <div key={noteItem} className="update-watch__note">
                  {noteItem}
                </div>
              ))}
            </div>

            <div className="update-watch__grid">
              {updateState.components.map((component) => (
                <article
                  key={component.component}
                  className="update-watch__card control-plane-update-card"
                  data-testid={`update-component-${component.component}`}
                >
                  <div className="section-eyebrow">{text.update.component}</div>
                  <div className="control-plane-flex-spread control-plane-flex-spread--wrap">
                    <h4 className="update-watch__title">{component.displayName}</h4>
                    <span className={getUpdateAvailabilityClassName(component.updateAvailability)}>
                      {formatUpdateAvailability(component.updateAvailability)}
                    </span>
                  </div>

                  <div className="update-watch__metrics">
                    <div className="metric-item">
                      <span className="metric-label">{text.update.currentVersion}</span>
                      <strong className="metric-value">{component.currentVersion}</strong>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.update.latestKnown}</span>
                      <strong className="metric-value">{component.latestKnownVersion ?? text.update.noPublishedVersion}</strong>
                    </div>
                    <div className="metric-item">
                      <span className="metric-label">{text.update.releaseChannel}</span>
                      <strong className="metric-value">{component.releaseChannel}</strong>
                    </div>
                  </div>

                  <p className="section-copy">{text.update.lastChecked}: {formatTimestamp(component.lastCheckedAt)}</p>
                  <p className="section-copy control-plane-zero-margin">{component.guidance}</p>

                  {component.releaseNotes.length > 0 ? (
                    <ol className="update-watch__list">
                      {component.releaseNotes.map((item) => (
                        <li key={item}>{item}</li>
                      ))}
                    </ol>
                  ) : (
                    <p className="section-copy control-plane-zero-margin">{text.update.noReleaseNotes}</p>
                  )}

                  {(component.releaseNotesUrl || component.downloadUrl) ? (
                    <div className="update-watch__actions">
                      {component.releaseNotesUrl ? (
                        <button
                          className="secondary-button"
                          type="button"
                          onClick={() => openExternalUrl(component.releaseNotesUrl!)}
                        >
                          {text.update.openReleaseNotes}
                        </button>
                      ) : null}
                      {component.downloadUrl && component.updateAvailability === "UpdateAvailable" ? (
                        <button
                          className="bootstrap-form__submit"
                          type="button"
                          onClick={() => openExternalUrl(component.downloadUrl!)}
                        >
                          {text.update.openManualUpgrade}
                        </button>
                      ) : null}
                    </div>
                  ) : null}
                </article>
              ))}
            </div>
          </>
        ) : (
          <p className="section-copy">{text.update.loading}</p>
        )}
          </section>

          <section
            ref={riskStageRef}
            className="status-card status-card--warning risk-briefing control-plane-stage-panel"
            data-testid="settings-risk-briefing"
          >
        <div className="section-eyebrow">{text.sections.riskEyebrow}</div>
        <h3 className="section-title control-plane-card-title">{text.sections.riskTitle}</h3>
        <p className="section-copy">{text.sections.riskIntro}</p>

        {riskOverview ? (
          <div className="risk-briefing__banner">
            {riskOverview.operatorWarnings.map((warning) => (
              <div key={warning} className="risk-briefing__warning">
                {warning}
              </div>
            ))}
          </div>
        ) : null}

        {riskError ? (
          <p className="bootstrap-panel__feedback bootstrap-panel__feedback--error" data-testid="settings-risk-error">
            {text.risk.unavailable} {riskError}
          </p>
        ) : null}

        {riskOverview ? (
          <div className="risk-briefing__grid">
            <article className="risk-briefing__card control-plane-risk-card" data-testid="risk-execution-card">
              <div className="section-eyebrow">{text.risk.executionBoundary}</div>
              <h4 className="risk-briefing__title">{text.risk.executionTitle}</h4>
              <div className="risk-briefing__stack">
                {riskOverview.executionProfiles.map((profile) => (
                  <div key={profile.key} className="metric-item">
                    <div className="control-plane-flex-spread">
                      <strong>{profile.displayName}</strong>
                      <span className={`risk-briefing__pill ${profile.active ? "risk-briefing__pill--active" : ""}`}>
                        {formatExecutionStatus(profile)}
                      </span>
                    </div>
                    <span className="metric-value">{profile.summary}</span>
                    <span className="metric-label">{profile.blastRadius}</span>
                    <div className="risk-briefing__points">
                      <div>
                        <span className="metric-label">{text.risk.guardrails}</span>
                        <ul>
                          {profile.guardrails.map((item) => (
                            <li key={item}>{item}</li>
                          ))}
                        </ul>
                      </div>
                      <div>
                        <span className="metric-label">{text.risk.residualRisk}</span>
                        <ul>
                          {profile.residualRisks.map((item) => (
                            <li key={item}>{item}</li>
                          ))}
                        </ul>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </article>

            <article className="risk-briefing__card control-plane-risk-card" data-testid="risk-approval-card">
              <div className="section-eyebrow">{text.risk.approvalPosture}</div>
              <h4 className="risk-briefing__title">{text.risk.approvalTitle}</h4>
              <div className="risk-briefing__metrics">
                <div className="metric-item">
                  <span className="metric-label">{text.risk.externalActions}</span>
                  <strong className="metric-value">
                    {riskOverview.approvalPosture.requireApprovalForExternalActions ? text.risk.approvalPreferred : text.risk.approvalRelaxed}
                  </strong>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.risk.quietHours}</span>
                  <strong className="metric-value">
                    {riskOverview.approvalPosture.quietHoursEnabled
                      ? riskOverview.approvalPosture.quietHoursWindow ?? text.risk.quietHoursEnabled
                      : text.risk.disabled}
                  </strong>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.risk.notifications}</span>
                  <strong className="metric-value">
                    {riskOverview.approvalPosture.notificationsEnabled ? text.risk.enabled : text.risk.disabled}
                  </strong>
                </div>
              </div>
              <p className="section-copy control-plane-zero-margin">{riskOverview.approvalPosture.advisory}</p>
            </article>

            <article className="risk-briefing__card control-plane-risk-card" data-testid="risk-plugins-card">
              <div className="section-eyebrow">{text.risk.pluginPermissions}</div>
              <h4 className="risk-briefing__title">{text.risk.pluginTitle}</h4>
              <div className="risk-briefing__metrics">
                <div className="metric-item">
                  <span className="metric-label">{text.risk.highRiskPlugins}</span>
                  <strong className="metric-value">{riskOverview.pluginRisk.highRiskCount}</strong>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.risk.trustBreakdown}</span>
                  <strong className="metric-value">
                    {riskOverview.pluginRisk.signedCount} / {riskOverview.pluginRisk.trustedCount} / {riskOverview.pluginRisk.untrustedCount}
                  </strong>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.risk.scopeBreakdown}</span>
                  <strong className="metric-value">
                    {riskOverview.pluginRisk.networkEnabledCount} / {riskOverview.pluginRisk.backgroundCount} / {riskOverview.pluginRisk.secretAccessCount}
                  </strong>
                </div>
              </div>
              <p className="section-copy">{riskOverview.pluginRisk.advisory}</p>
              {riskOverview.pluginRisk.riskItems.length > 0 ? (
                <ul className="risk-briefing__list">
                  {riskOverview.pluginRisk.riskItems.map((item) => (
                    <li key={item.pluginId}>
                      <div className="metric-item">
                        <div className="control-plane-flex-spread">
                          <strong>{item.displayName}</strong>
                          <span className={`risk-briefing__pill ${item.highRiskReasons.length > 0 ? "risk-briefing__pill--warning" : ""}`}>
                            {formatTrustState(item.trustState)} · {item.runtimeState}
                          </span>
                        </div>
                        <span className="metric-label">{item.requestedScopes.join(" • ") || text.labels.noExtraScopes}</span>
                        {item.highRiskReasons.length > 0 ? (
                          <span className="metric-value">{item.highRiskReasons.join(" ")}</span>
                        ) : null}
                        {item.mediumRiskReasons.length > 0 ? (
                          <span className="section-copy">{item.mediumRiskReasons.join(" ")}</span>
                        ) : null}
                        {item.trustEvidenceSummary ? (
                          <span className="section-copy">{text.labels.trustEvidence}: {item.trustEvidenceSummary}</span>
                        ) : null}
                      </div>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="section-copy control-plane-zero-margin">{text.risk.noPluginEscalations}</p>
              )}
            </article>

            <article className="risk-briefing__card control-plane-risk-card" data-testid="risk-channels-card">
              <div className="section-eyebrow">{text.risk.channelOutbound}</div>
              <h4 className="risk-briefing__title">{text.risk.channelTitle}</h4>
              <div className="risk-briefing__metrics">
                <div className="metric-item">
                  <span className="metric-label">{text.risk.autoSend}</span>
                  <strong className="metric-value">{riskOverview.channelRisk.autoSendCount}</strong>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.risk.draftOrApproval}</span>
                  <strong className="metric-value">
                    {riskOverview.channelRisk.draftApprovalCount} / {riskOverview.channelRisk.requireApprovalCount}
                  </strong>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.risk.pendingApprovals}</span>
                  <strong className="metric-value">{riskOverview.channelRisk.pendingApprovalCount}</strong>
                </div>
              </div>
              <p className="section-copy">{riskOverview.channelRisk.advisory}</p>
              {riskOverview.channelRisk.riskItems.length > 0 ? (
                <ul className="risk-briefing__list">
                  {riskOverview.channelRisk.riskItems.map((item) => (
                    <li key={item.bindingId}>
                      <div className="metric-item">
                        <div className="control-plane-flex-spread">
                          <strong>{item.displayTitle}</strong>
                          <span className={`risk-briefing__pill ${item.hasPendingApproval ? "risk-briefing__pill--warning" : ""}`}>
                            {formatDeliveryMode(item.deliveryMode)}
                          </span>
                        </div>
                        <span className="metric-label">
                          {item.connectorKind} · {item.threadType} · {text.labels.accountState} {item.accountState}
                        </span>
                        <span className="section-copy">
                          {item.supportsOutbound ? text.risk.outboundCapable : text.risk.inboundOnly}
                          {item.hasPendingApproval ? ` ${text.risk.pendingApprovalHint}` : ""}
                        </span>
                      </div>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="section-copy control-plane-zero-margin">{text.risk.noChannelRisk}</p>
              )}
            </article>
          </div>
        ) : (
          <p className="section-copy">{text.common.loading}</p>
        )}
          </section>
        </div>
      </div>
    </section>
  );
}
