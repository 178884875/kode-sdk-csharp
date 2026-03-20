import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import {
  disablePlugin,
  discoverPlugins,
  enablePlugin,
  fetchPlugin,
  fetchPluginLogs,
  fetchPlugins,
  installLocalPlugin,
  startPlugin,
  stopPlugin,
  trustPlugin,
} from "../lib/api";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import type {
  PluginDetail,
  PluginLogEntry,
  PluginRuntimeState,
  PluginSummary,
  PluginTrustState,
  PluginType,
} from "../types/contracts";
import "./ControlPlaneDesk.css";

type PluginTypeFilter = PluginType | "all";
type PluginTrustFilter = PluginTrustState | "all";
type PluginRuntimeFilter = PluginRuntimeState | "all";

const PLUGIN_TYPES: PluginType[] = ["Tool", "Channel", "Memory", "Ui"];
const PLUGIN_TRUST_STATES: PluginTrustState[] = ["Signed", "Trusted", "Untrusted"];
const PLUGIN_RUNTIME_STATES: PluginRuntimeState[] = ["Running", "Starting", "Stopped", "Degraded"];
const PLUGIN_LIST_LIMIT = 80;
const PLUGIN_LOG_LIMIT = 200;

function formatDigest(value?: string | null): string {
  if (!value) {
    return "n/a";
  }

  return value.length <= 20 ? value : `${value.slice(0, 16)}...${value.slice(-12)}`;
}

export function PluginsDesk() {
  const { formatDateTime } = useI18n();
  const text = useLocaleText({
    zh: {
      common: {
        controlPlane: "控制平面",
        loading: "加载中…",
        none: "暂无",
        refresh: "刷新工作台",
        refreshing: "正在刷新…",
        allTypes: "全部类型",
        allStates: "全部状态",
      },
      title: "插件指挥台",
      intro: "在插件工具进入新会话前，先检查信任状态、启动姿态、权限范围与运行证据。",
      errors: {
        loadDetail: "加载插件详情失败。",
        loadRegistry: "加载插件注册表失败。",
        actionFailed: "插件操作失败。",
        installPathRequired: "安装前请先提供本地插件目录。",
        selectPluginRequired: "发送生命周期指令前，请先选择一个插件。",
      },
      notes: {
        discoveryCompleted: "发现扫描已完成。",
        installed: "已安装",
        trustVerified: "插件信任已授予，并验证了签名证据。",
        trustDigest: "插件信任已授予，并记录了本地摘要证据。",
        enabled: "插件已启用。",
        disabled: "插件已停用。",
        started: "插件运行时已启动。",
        stopped: "插件运行时已停止。",
      },
      filters: {
        discover: "扫描插件根目录",
        type: "类型",
        trust: "信任",
        runtime: "运行时",
        enabledOnly: "仅看已启用",
      },
      install: {
        label: "安装本地插件",
        placeholder: "/插件目录的绝对路径",
        submit: "安装",
      },
      list: {
        title: "注册表索引",
        loading: "正在加载插件…",
        empty: "当前筛选条件下没有插件。",
        pluginsSuffix: "个插件",
      },
      detail: {
        eyebrow: "插件详情",
        empty: "尚未选择插件",
        idle: "空闲",
        pluginId: "插件 ID",
        installSource: "安装来源",
        runtimeState: "运行状态",
        health: "健康状态",
        trust: "信任状态",
        verification: "校验结果",
        trustSource: "信任来源",
        enablement: "启用状态",
        lastHealth: "最近健康检查",
        restartCount: "重启次数",
        noHealthSummary: "暂无健康摘要。",
        enabled: "已启用",
        disabled: "未启用",
      },
      actions: {
        trust: "信任",
        enable: "启用",
        disable: "停用",
        start: "启动",
        stop: "停止",
      },
      trustEvidence: {
        title: "信任证据",
        verification: "校验",
        launchGate: "启动门控",
        digest: "证据摘要",
        signerUnavailable: "签名者信息不可用",
        unavailable: "不可用",
      },
      permissions: {
        title: "权限姿态",
        reviewRequired: "需要复核",
        lowFriction: "低摩擦",
        declaredScope: "声明范围",
        capabilities: "能力声明",
      },
      tools: {
        title: "工具暴露",
        namespacedTool: "命名空间工具",
        empty: "这个插件当前没有暴露任何运行时工具。",
        toolsSuffix: "个工具",
      },
      logs: {
        title: "最近日志",
        copy: "调整启动姿态前，先把宿主证据放在手边。",
        rawLogs: "原始日志",
        empty: "还没有记录到插件日志。",
      },
      future: {
        eyebrow: "未来设置",
        title: "面板挂载位已预留",
        copy: "Iteration 4 暂时只保留插件设置占位面。等 UI 插件到来后，这里会成为安全的宿主容器。",
      },
      labels: {
        noExtraScopes: "没有声明额外的文件系统、网络或 Secrets 范围。",
        noCapabilities: "尚未加载能力元数据。",
        noCapabilityFragments: "Manifest 没有声明额外能力。",
        trustAwaiting: "等待建立信任",
        trustTrusted: "允许启动",
        trustSigned: "已验证签名证据",
        runtimeRunning: "运行中",
        runtimeStarting: "启动中",
        runtimeDegraded: "需要操作员恢复",
        runtimeStopped: "已停止",
        verificationVerified: "签名证据与当前插件内容一致。",
        verificationMismatch: "签名证据与当前插件内容已经不匹配。",
        verificationInvalid: "信任证据不完整或不可读。",
        verificationDigestOnly: "已记录本地摘要，但没有发现签名 sidecar。",
        verificationNone: "尚未记录任何信任证据。",
        gateEmpty: "选择一个插件后，可在这里检查信任与权限门控。",
        gateSignedMismatch: "签名插件必须先恢复到可验证状态，才能再次启动。",
        gateHighRisk: "信任这个插件会解锁高风险权限；启动前请复查每一项声明。",
        gateDefault: "启动门控取决于信任、启用状态、运行健康，以及最新校验证据。",
        noLogsMessage: "暂无",
      },
      typeLabels: {
        Tool: "工具",
        Channel: "渠道",
        Memory: "记忆",
        Ui: "界面",
      },
      trustStateLabels: {
        Signed: "已签名",
        Trusted: "已信任",
        Untrusted: "未信任",
      },
      runtimeStateLabels: {
        Running: "运行中",
        Starting: "启动中",
        Stopped: "已停止",
        Degraded: "降级",
      },
    },
    en: {
      common: {
        controlPlane: "Control Plane",
        loading: "Loading...",
        none: "n/a",
        refresh: "Refresh deck",
        refreshing: "Refreshing...",
        allTypes: "All types",
        allStates: "All states",
      },
      title: "Plugin Command Deck",
      intro: "Inspect trust, launch posture, permissions, and runtime evidence before plugin tools enter a fresh operator session.",
      errors: {
        loadDetail: "Failed to load plugin detail.",
        loadRegistry: "Failed to load plugin registry.",
        actionFailed: "Plugin action failed.",
        installPathRequired: "Provide a local plugin directory before installing.",
        selectPluginRequired: "Select a plugin before sending lifecycle commands.",
      },
      notes: {
        discoveryCompleted: "Discovery sweep completed.",
        installed: "Installed",
        trustVerified: "Plugin trust granted with verified signature evidence.",
        trustDigest: "Plugin trust granted with local digest evidence.",
        enabled: "Plugin enabled.",
        disabled: "Plugin disabled.",
        started: "Plugin runtime started.",
        stopped: "Plugin runtime stopped.",
      },
      filters: {
        discover: "Discover roots",
        type: "Type",
        trust: "Trust",
        runtime: "Runtime",
        enabledOnly: "Enabled only",
      },
      install: {
        label: "Install local plugin",
        placeholder: "/absolute/path/to/plugin",
        submit: "Install",
      },
      list: {
        title: "Registry index",
        loading: "Loading plugins...",
        empty: "No plugins match the current filters.",
        pluginsSuffix: "plugins",
      },
      detail: {
        eyebrow: "Plugin detail",
        empty: "No plugin selected",
        idle: "Idle",
        pluginId: "Plugin id",
        installSource: "Install source",
        runtimeState: "Runtime state",
        health: "Health",
        trust: "Trust",
        verification: "Verification",
        trustSource: "Trust source",
        enablement: "Enablement",
        lastHealth: "Last health",
        restartCount: "Restart count",
        noHealthSummary: "No health summary yet.",
        enabled: "Enabled",
        disabled: "Disabled",
      },
      actions: {
        trust: "Trust",
        enable: "Enable",
        disable: "Disable",
        start: "Start",
        stop: "Stop",
      },
      trustEvidence: {
        title: "Trust evidence",
        verification: "Verification",
        launchGate: "Launch gate",
        digest: "Evidence digest",
        signerUnavailable: "Signer unavailable",
        unavailable: "Unavailable",
      },
      permissions: {
        title: "Permission posture",
        reviewRequired: "Review required",
        lowFriction: "Low friction",
        declaredScope: "Declared scope",
        capabilities: "Capabilities",
      },
      tools: {
        title: "Tool exposure",
        namespacedTool: "Namespaced tool",
        empty: "No runtime tools are currently exposed for this plugin.",
        toolsSuffix: "tool(s)",
      },
      logs: {
        title: "Recent logs",
        copy: "Keep the host evidence close before changing launch posture.",
        rawLogs: "Raw logs",
        empty: "No plugin logs have been recorded yet.",
      },
      future: {
        eyebrow: "Future settings",
        title: "Panel mount is reserved",
        copy: "Iteration 4 keeps plugin settings as a placeholder surface. Once UI plugins arrive, this card becomes the safe host container.",
      },
      labels: {
        noExtraScopes: "No extra filesystem, network, or secret scopes were declared.",
        noCapabilities: "No capability metadata loaded.",
        noCapabilityFragments: "Manifest does not advertise extra capabilities.",
        trustAwaiting: "Awaiting trust",
        trustTrusted: "Trusted for launch",
        trustSigned: "Signed evidence verified",
        runtimeRunning: "Runtime active",
        runtimeStarting: "Starting",
        runtimeDegraded: "Needs operator recovery",
        runtimeStopped: "Stopped",
        verificationVerified: "Signature evidence matches the current plugin contents.",
        verificationMismatch: "Signature evidence no longer matches the plugin contents.",
        verificationInvalid: "Trust evidence is incomplete or unreadable.",
        verificationDigestOnly: "Local digest captured; no signature sidecar was found.",
        verificationNone: "No trust evidence recorded yet.",
        gateEmpty: "Select a plugin to inspect its trust and permission gates.",
        gateSignedMismatch: "Signed plugins cannot launch until signature evidence verifies cleanly again.",
        gateHighRisk: "Trusting this plugin unlocks high-risk scopes; review every declared permission before launch.",
        gateDefault: "Launch stays gated by trust, enablement, runtime health, and the latest verification evidence.",
        noLogsMessage: "n/a",
      },
      typeLabels: {
        Tool: "Tool",
        Channel: "Channel",
        Memory: "Memory",
        Ui: "UI",
      },
      trustStateLabels: {
        Signed: "Signed",
        Trusted: "Trusted",
        Untrusted: "Untrusted",
      },
      runtimeStateLabels: {
        Running: "Running",
        Starting: "Starting",
        Stopped: "Stopped",
        Degraded: "Degraded",
      },
    },
  });

  const [plugins, setPlugins] = useState<PluginSummary[]>([]);
  const [selectedPluginId, setSelectedPluginId] = useState<string | null>(null);
  const [detail, setDetail] = useState<PluginDetail | null>(null);
  const [logs, setLogs] = useState<PluginLogEntry[]>([]);
  const [installPath, setInstallPath] = useState("");
  const [typeFilter, setTypeFilter] = useState<PluginTypeFilter>("all");
  const [trustFilter, setTrustFilter] = useState<PluginTrustFilter>("all");
  const [runtimeFilter, setRuntimeFilter] = useState<PluginRuntimeFilter>("all");
  const [enabledOnly, setEnabledOnly] = useState(false);
  const [isLoadingList, setIsLoadingList] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isLoadingDetail, setIsLoadingDetail] = useState(false);
  const [isMutating, setIsMutating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [note, setNote] = useState<string | null>(null);

  const listRequestIdRef = useRef(0);
  const detailRequestIdRef = useRef(0);

  const selectedSummary = useMemo(
    () => plugins.find((item) => item.id === selectedPluginId) ?? null,
    [plugins, selectedPluginId],
  );

  const describeTrust = (trustState: PluginTrustState): string => {
    switch (trustState) {
      case "Signed":
        return text.labels.trustSigned;
      case "Trusted":
        return text.labels.trustTrusted;
      default:
        return text.labels.trustAwaiting;
    }
  };

  const describeRuntime = (runtimeState: PluginRuntimeState): string => {
    switch (runtimeState) {
      case "Running":
        return text.labels.runtimeRunning;
      case "Starting":
        return text.labels.runtimeStarting;
      case "Degraded":
        return text.labels.runtimeDegraded;
      default:
        return text.labels.runtimeStopped;
    }
  };

  const summarizeTypes = (types: PluginType[]): string => {
    return types.map((type) => text.typeLabels[type] ?? type).join(" · ");
  };

  const summarizePermissions = (pluginDetail: PluginDetail | null): string[] => {
    if (!pluginDetail) {
      return [];
    }

    const high = pluginDetail.permissionSummary.highRiskReasons;
    const medium = pluginDetail.permissionSummary.mediumRiskReasons;
    if (high.length > 0 || medium.length > 0) {
      return [...high, ...medium];
    }

    return [text.labels.noExtraScopes];
  };

  const summarizeCapabilities = (pluginDetail: PluginDetail | null): string => {
    if (!pluginDetail) {
      return text.labels.noCapabilities;
    }

    const capabilities = pluginDetail.record.manifest.capabilities;
    const fragments = [
      capabilities.tools && capabilities.tools.length > 0 ? `${capabilities.tools.length} ${text.tools.toolsSuffix}` : null,
      capabilities.channels && capabilities.channels.length > 0 ? `${capabilities.channels.length} ${text.typeLabels.Channel}` : null,
      capabilities.memoryProviders && capabilities.memoryProviders.length > 0
        ? `${capabilities.memoryProviders.length} ${text.typeLabels.Memory}`
        : null,
      capabilities.uiPanels && capabilities.uiPanels.length > 0 ? `${capabilities.uiPanels.length} ${text.typeLabels.Ui}` : null,
    ].filter(Boolean);

    return fragments.length > 0 ? fragments.join(" · ") : text.labels.noCapabilityFragments;
  };

  const describeVerificationState = (pluginDetail: PluginDetail | null): string => {
    const verificationState = pluginDetail?.record.trustEvidence?.verificationState;
    switch (verificationState) {
      case "Verified":
        return text.labels.verificationVerified;
      case "Mismatch":
        return text.labels.verificationMismatch;
      case "Invalid":
        return text.labels.verificationInvalid;
      case "DigestOnly":
        return text.labels.verificationDigestOnly;
      default:
        return text.labels.verificationNone;
    }
  };

  const describeTrustGate = (pluginDetail: PluginDetail | null): string => {
    if (!pluginDetail) {
      return text.labels.gateEmpty;
    }

    if (pluginDetail.record.trustState === "Signed" && pluginDetail.record.trustEvidence?.verificationState !== "Verified") {
      return text.labels.gateSignedMismatch;
    }

    if (pluginDetail.permissionSummary.hasHighRisk) {
      return text.labels.gateHighRisk;
    }

    return text.labels.gateDefault;
  };

  const permissionSummary = useMemo(() => summarizePermissions(detail), [detail]);
  const toolNames = detail?.availableTools ?? [];
  const currentEnabled = detail?.record.enabled ?? selectedSummary?.enabled ?? false;
  const currentRestartCount = detail?.record.restartCount ?? 0;
  const hasVerifiedSignature =
    detail?.record.trustState !== "Signed" || detail?.record.trustEvidence?.verificationState === "Verified";

  async function loadPluginDetail(pluginId: string | null) {
    const requestId = ++detailRequestIdRef.current;

    if (!pluginId) {
      setDetail(null);
      setLogs([]);
      setIsLoadingDetail(false);
      return;
    }

    setIsLoadingDetail(true);

    try {
      const [detailPayload, logsPayload] = await Promise.all([
        fetchPlugin(pluginId),
        fetchPluginLogs(pluginId, { limit: PLUGIN_LOG_LIMIT }),
      ]);

      if (detailRequestIdRef.current !== requestId) {
        return;
      }

      setDetail(detailPayload);
      setLogs(logsPayload);
    } catch (nextError) {
      if (detailRequestIdRef.current !== requestId) {
        return;
      }

      setDetail(null);
      setLogs([]);
      setError(nextError instanceof Error ? nextError.message : text.errors.loadDetail);
    } finally {
      if (detailRequestIdRef.current === requestId) {
        setIsLoadingDetail(false);
      }
    }
  }

  async function loadPlugins(mode: "initial" | "refresh", preferredPluginId?: string | null) {
    const requestId = ++listRequestIdRef.current;

    if (mode === "initial") {
      setIsLoadingList(true);
    } else {
      setIsRefreshing(true);
    }

    setError(null);

    try {
      const payload = await fetchPlugins({
        type: typeFilter === "all" ? undefined : typeFilter,
        trustState: trustFilter === "all" ? undefined : trustFilter,
        runtimeState: runtimeFilter === "all" ? undefined : runtimeFilter,
        enabled: enabledOnly ? true : undefined,
        limit: PLUGIN_LIST_LIMIT,
      });

      if (listRequestIdRef.current !== requestId) {
        return;
      }

      setPlugins(payload.items);
      const nextSelectedPluginId = payload.items.some((item) => item.id === preferredPluginId)
        ? preferredPluginId ?? null
        : payload.items[0]?.id ?? null;
      setSelectedPluginId(nextSelectedPluginId);
      await loadPluginDetail(nextSelectedPluginId);
    } catch (nextError) {
      if (listRequestIdRef.current !== requestId) {
        return;
      }

      setPlugins([]);
      setSelectedPluginId(null);
      setDetail(null);
      setLogs([]);
      setError(nextError instanceof Error ? nextError.message : text.errors.loadRegistry);
    } finally {
      if (listRequestIdRef.current === requestId) {
        if (mode === "initial") {
          setIsLoadingList(false);
        } else {
          setIsRefreshing(false);
        }
      }
    }
  }

  useEffect(() => {
    void loadPlugins("initial", selectedPluginId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [typeFilter, trustFilter, runtimeFilter, enabledOnly]);

  async function handleRefresh() {
    await loadPlugins("refresh", selectedPluginId);
  }

  async function handleSelectPlugin(pluginId: string) {
    setSelectedPluginId(pluginId);
    setError(null);
    await loadPluginDetail(pluginId);
  }

  async function handleAction(action: "trust" | "enable" | "disable" | "start" | "stop" | "discover" | "install") {
    setIsMutating(true);
    setError(null);
    setNote(null);

    try {
      if (action === "discover") {
        await discoverPlugins();
        setNote(text.notes.discoveryCompleted);
        await loadPlugins("refresh", selectedPluginId);
        return;
      }

      if (action === "install") {
        const normalizedPath = installPath.trim();
        if (!normalizedPath) {
          setError(text.errors.installPathRequired);
          return;
        }

        const installed = await installLocalPlugin({ path: normalizedPath });
        setInstallPath("");
        setNote(`${text.notes.installed} ${installed.record.manifest.name}。`);
        await loadPlugins("refresh", installed.record.id);
        return;
      }

      if (!selectedPluginId) {
        setError(text.errors.selectPluginRequired);
        return;
      }

      if (action === "trust") {
        const trusted = await trustPlugin(selectedPluginId);
        setNote(trusted.record.trustState === "Signed" ? text.notes.trustVerified : text.notes.trustDigest);
      } else if (action === "enable") {
        await enablePlugin(selectedPluginId);
        setNote(text.notes.enabled);
      } else if (action === "disable") {
        await disablePlugin(selectedPluginId);
        setNote(text.notes.disabled);
      } else if (action === "start") {
        await startPlugin(selectedPluginId);
        setNote(text.notes.started);
      } else {
        await stopPlugin(selectedPluginId);
        setNote(text.notes.stopped);
      }

      await loadPlugins("refresh", selectedPluginId);
    } catch (nextError) {
      setError(nextError instanceof Error ? nextError.message : text.errors.actionFailed);
    } finally {
      setIsMutating(false);
    }
  }

  const trustButtonEnabled = !isMutating && !!detail && detail.record.trustState === "Untrusted";
  const enableButtonEnabled = !isMutating && !!detail && (detail.record.trustState === "Trusted" || detail.record.trustState === "Signed") && !detail.record.enabled;
  const disableButtonEnabled = !isMutating && !!detail && detail.record.enabled;
  const startButtonEnabled = !isMutating && !!detail && (detail.record.trustState === "Trusted" || detail.record.trustState === "Signed") && hasVerifiedSignature && detail.record.enabled && detail.record.runtimeState !== "Running";
  const stopButtonEnabled = !isMutating && !!detail && detail.record.runtimeState === "Running";

  return (
    <section className="bootstrap-panel" data-testid="plugins-desk">
      <div className="section-eyebrow">{text.common.controlPlane}</div>
      <h2 className="section-title">{text.title}</h2>
      <p className="section-copy">{text.intro}</p>

      <div className="control-plane-toolbar">
        <button
          type="button"
          className="secondary-button"
          data-testid="plugins-refresh"
          disabled={isLoadingList || isRefreshing || isMutating}
          onClick={() => {
            void handleRefresh();
          }}
        >
          {isRefreshing ? text.common.refreshing : text.common.refresh}
        </button>
        <button
          type="button"
          className="secondary-button"
          data-testid="plugins-discover"
          disabled={isMutating}
          onClick={() => {
            void handleAction("discover");
          }}
        >
          {text.filters.discover}
        </button>
        <label className="metric-label" htmlFor="plugins-type-filter">
          {text.filters.type}
        </label>
        <select
          id="plugins-type-filter"
          data-testid="plugins-type-filter"
          className="bootstrap-form__textarea control-plane-filter control-plane-select"
          value={typeFilter}
          onChange={(event) => setTypeFilter(event.target.value as PluginTypeFilter)}
        >
          <option value="all">{text.common.allTypes}</option>
          {PLUGIN_TYPES.map((type) => (
            <option key={type} value={type}>
              {text.typeLabels[type]}
            </option>
          ))}
        </select>
        <label className="metric-label" htmlFor="plugins-trust-filter">
          {text.filters.trust}
        </label>
        <select
          id="plugins-trust-filter"
          data-testid="plugins-trust-filter"
          className="bootstrap-form__textarea control-plane-filter control-plane-select"
          value={trustFilter}
          onChange={(event) => setTrustFilter(event.target.value as PluginTrustFilter)}
        >
          <option value="all">{text.common.allStates}</option>
          {PLUGIN_TRUST_STATES.map((state) => (
            <option key={state} value={state}>
              {text.trustStateLabels[state]}
            </option>
          ))}
        </select>
        <label className="metric-label" htmlFor="plugins-runtime-filter">
          {text.filters.runtime}
        </label>
        <select
          id="plugins-runtime-filter"
          data-testid="plugins-runtime-filter"
          className="bootstrap-form__textarea control-plane-filter control-plane-filter--wide control-plane-select"
          value={runtimeFilter}
          onChange={(event) => setRuntimeFilter(event.target.value as PluginRuntimeFilter)}
        >
          <option value="all">{text.common.allStates}</option>
          {PLUGIN_RUNTIME_STATES.map((state) => (
            <option key={state} value={state}>
              {text.runtimeStateLabels[state]}
            </option>
          ))}
        </select>
        <label className="bootstrap-form__toggle" htmlFor="plugins-enabled-only">
          <input
            id="plugins-enabled-only"
            data-testid="plugins-enabled-only"
            type="checkbox"
            checked={enabledOnly}
            onChange={(event) => setEnabledOnly(event.target.checked)}
          />
          {text.filters.enabledOnly}
        </label>
      </div>

      <form
        data-testid="plugin-install-form"
        onSubmit={(event: FormEvent<HTMLFormElement>) => {
          event.preventDefault();
          void handleAction("install");
        }}
        className="control-plane-install-form"
      >
        <label className="bootstrap-form__field">
          <span className="bootstrap-form__label">{text.install.label}</span>
          <input
            data-testid="plugin-install-path"
            className="bootstrap-form__textarea control-plane-input"
            value={installPath}
            onChange={(event) => setInstallPath(event.target.value)}
            placeholder={text.install.placeholder}
          />
        </label>
        <button
          type="submit"
          className="secondary-button control-plane-install-submit"
          data-testid="plugin-install-submit"
          disabled={isMutating}
        >
          {text.install.submit}
        </button>
      </form>

      {error ? (
        <p className="bootstrap-panel__feedback bootstrap-panel__feedback--error" data-testid="plugins-error">
          {error}
        </p>
      ) : null}
      {note ? (
        <p className="bootstrap-panel__feedback bootstrap-panel__feedback--success" data-testid="plugins-note">
          {note}
        </p>
      ) : null}

      <div className="control-plane-split-pane">
        <section className="timeline" data-testid="plugins-list">
          <div className="timeline__header">
            <h3 className="section-title">{text.list.title}</h3>
            <span className="composer__status">{isLoadingList ? text.common.loading : `${plugins.length} ${text.list.pluginsSuffix}`}</span>
          </div>

          <div className="timeline__body" style={{ maxHeight: "min(52vh, 680px)" }}>
            {isLoadingList ? <p className="timeline__empty">{text.list.loading}</p> : null}
            {!isLoadingList && plugins.length === 0 ? (
              <p className="timeline__empty">{text.list.empty}</p>
            ) : null}

            {plugins.map((plugin) => {
              const isSelected = plugin.id === selectedPluginId;
              return (
                <button
                  type="button"
                  key={plugin.id}
                  className={`${plugin.runtimeState === "Running" ? "message message--assistant" : "message message--system"} control-plane-list-button ${isSelected ? "control-plane-list-button--selected" : ""}`}
                  data-testid={`plugin-item-${plugin.id}`}
                  aria-pressed={isSelected}
                  onClick={() => {
                    void handleSelectPlugin(plugin.id);
                  }}
                >
                  <div className="message__meta">
                    <span className="message__role">{summarizeTypes(plugin.types)}</span>
                    <span>{describeRuntime(plugin.runtimeState)}</span>
                  </div>
                  <strong>{plugin.name}</strong>
                  <span>{plugin.id}</span>
                  <div className="control-plane-chip-row">
                    <span className={`stream-indicator ${plugin.trustState === "Trusted" || plugin.trustState === "Signed" ? "is-live" : ""}`}>
                      {describeTrust(plugin.trustState)}
                    </span>
                    <span className={`stream-indicator ${plugin.enabled ? "is-live" : ""}`}>
                      {plugin.enabled ? text.detail.enabled : text.detail.disabled}
                    </span>
                  </div>
                  {plugin.lastError ? (
                    <span className="control-plane-text-danger">{plugin.lastError}</span>
                  ) : (
                    <span className="metric-value metric-value--path">{plugin.rootPath}</span>
                  )}
                </button>
              );
            })}
          </div>
        </section>

        <div className="desk-column control-plane-stack">
          <section className="status-card status-card--warning" data-testid="plugin-detail">
            <div className="timeline__header">
              <div>
                <p className="section-eyebrow control-plane-compact-copy">{text.detail.eyebrow}</p>
                <h3 className="section-title control-plane-card-title">{detail?.record.manifest.name ?? selectedSummary?.name ?? text.detail.empty}</h3>
              </div>
              <span className={`stream-indicator ${detail?.healthSummary.isHealthy ? "is-live" : ""}`}>
                {detail?.healthSummary.status ?? text.detail.idle}
              </span>
            </div>

            <div data-testid="plugin-detail-core" className="control-plane-detail-grid">
              <div className="metric-item">
                <span className="metric-label">{text.detail.pluginId}</span>
                <span className="metric-value metric-value--path">{detail?.record.id ?? selectedSummary?.id ?? text.common.none}</span>
                <span className="metric-label">{text.detail.installSource}</span>
                <span className="metric-value">{detail?.record.installSource ?? selectedSummary?.installSource ?? text.common.none}</span>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.detail.runtimeState}</span>
                <span className="metric-value">{detail?.record.runtimeState ?? selectedSummary?.runtimeState ?? text.common.none}</span>
                <span className="metric-label">{text.detail.health}</span>
                <span className="metric-value">{detail?.healthSummary.message ?? text.detail.noHealthSummary}</span>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.detail.trust}</span>
                <span className="metric-value">{detail?.record.trustState ?? selectedSummary?.trustState ?? text.common.none}</span>
                <span className="metric-label">{text.detail.verification}</span>
                <span className="metric-value">{detail?.record.trustEvidence?.verificationState ?? text.common.none}</span>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.detail.trustSource}</span>
                <span className="metric-value">{detail?.record.trustEvidence?.source ?? text.common.none}</span>
                <span className="metric-label">{text.detail.enablement}</span>
                <span className="metric-value" data-testid="plugin-enabled-chip">{currentEnabled ? text.detail.enabled : text.detail.disabled}</span>
              </div>
              <div className="metric-item">
                <span className="metric-label">{text.detail.lastHealth}</span>
                <span className="metric-value">{formatDateTime(detail?.healthSummary.lastHealthAt, text.common.none)}</span>
                <span className="metric-label">{text.detail.restartCount}</span>
                <span className="metric-value">{currentRestartCount}</span>
              </div>
            </div>

            <div className="control-plane-detail-actions">
              <button type="button" className="secondary-button" data-testid="plugin-action-trust" disabled={!trustButtonEnabled} onClick={() => { void handleAction("trust"); }}>
                {text.actions.trust}
              </button>
              <button type="button" className="secondary-button" data-testid="plugin-action-enable" disabled={!enableButtonEnabled} onClick={() => { void handleAction("enable"); }}>
                {text.actions.enable}
              </button>
              <button type="button" className="secondary-button" data-testid="plugin-action-disable" disabled={!disableButtonEnabled} onClick={() => { void handleAction("disable"); }}>
                {text.actions.disable}
              </button>
              <button type="button" className="secondary-button" data-testid="plugin-action-start" disabled={!startButtonEnabled} onClick={() => { void handleAction("start"); }}>
                {text.actions.start}
              </button>
              <button type="button" className="secondary-button" data-testid="plugin-action-stop" disabled={!stopButtonEnabled} onClick={() => { void handleAction("stop"); }}>
                {text.actions.stop}
              </button>
            </div>

            <div className="timeline control-plane-panel" data-testid="plugin-trust-evidence">
              <div className="timeline__header">
                <h3 className="section-title">{text.trustEvidence.title}</h3>
                <span className={`composer__status ${detail?.record.trustEvidence?.verificationState === "Verified" ? "is-live" : ""}`}>
                  {detail?.record.trustEvidence?.verificationState ?? text.trustEvidence.unavailable}
                </span>
              </div>
              <div className="timeline__body">
                <article className="message message--assistant">
                  <div className="message__meta">
                    <span className="message__role">{text.trustEvidence.verification}</span>
                    <span>{formatDateTime(detail?.record.trustEvidence?.verifiedAt, text.common.none)}</span>
                  </div>
                  <p className="message__text">{describeVerificationState(detail)}</p>
                </article>
                <article className="message message--system">
                  <div className="message__meta">
                    <span className="message__role">{text.trustEvidence.launchGate}</span>
                  </div>
                  <p className="message__text">{describeTrustGate(detail)}</p>
                </article>
                <article className="message message--assistant">
                  <div className="message__meta">
                    <span className="message__role">{text.trustEvidence.digest}</span>
                    <span>{detail?.record.trustEvidence?.signer ?? text.trustEvidence.signerUnavailable}</span>
                  </div>
                  <p className="message__text">
                    Manifest {formatDigest(detail?.record.trustEvidence?.manifestDigestSha256)} · Package {formatDigest(detail?.record.trustEvidence?.packageDigestSha256)}
                  </p>
                  {detail?.record.trustEvidence?.signatureFilePath ? (
                    <p className="message__text">{detail.record.trustEvidence.signatureFilePath}</p>
                  ) : null}
                  {detail?.record.trustEvidence?.summary ? (
                    <p className="message__text">{detail.record.trustEvidence.summary}</p>
                  ) : null}
                </article>
              </div>
            </div>
          </section>

          <section className="timeline" data-testid="plugin-permissions">
            <div className="timeline__header">
              <h3 className="section-title">{text.permissions.title}</h3>
              <span className={`composer__status ${detail?.permissionSummary.hasHighRisk ? "" : "is-live"}`}>
                {detail?.permissionSummary.hasHighRisk ? text.permissions.reviewRequired : text.permissions.lowFriction}
              </span>
            </div>
            <div className="timeline__body">
              {permissionSummary.map((item, index) => (
                <article className="message message--assistant" key={`${item}-${index}`}>
                  <div className="message__meta">
                    <span className="message__role">{text.permissions.declaredScope}</span>
                    <span>{index + 1}</span>
                  </div>
                  <p className="message__text">{item}</p>
                </article>
              ))}
              <article className="message message--system">
                <div className="message__meta">
                  <span className="message__role">{text.permissions.capabilities}</span>
                </div>
                <p className="message__text">{summarizeCapabilities(detail)}</p>
              </article>
            </div>
          </section>

          <section className="timeline" data-testid="plugin-tools">
            <div className="timeline__header">
              <h3 className="section-title">{text.tools.title}</h3>
              <span className="composer__status">{isLoadingDetail ? text.common.loading : `${toolNames.length} ${text.tools.toolsSuffix}`}</span>
            </div>
            <div className="timeline__body">
              {toolNames.length === 0 ? (
                <article className="message message--system">
                  <p className="message__text">{text.tools.empty}</p>
                </article>
              ) : (
                toolNames.map((toolName) => (
                  <article className="message message--assistant" key={toolName}>
                    <div className="message__meta">
                      <span className="message__role">{text.tools.namespacedTool}</span>
                    </div>
                    <p className="message__text">{toolName}</p>
                  </article>
                ))
              )}
            </div>
          </section>

          <section className="timeline" data-testid="plugin-logs">
            <div className="timeline__header">
              <div>
                <h3 className="section-title">{text.logs.title}</h3>
                <p className="section-copy control-plane-compact-copy">{text.logs.copy}</p>
              </div>
              <a data-testid="plugin-logs-link" className="secondary-button" href={selectedPluginId ? `/api/plugins/${selectedPluginId}/logs?limit=${PLUGIN_LOG_LIMIT}` : "#"}>
                {text.logs.rawLogs}
              </a>
            </div>
            <div className="timeline__body">
              {logs.length === 0 ? (
                <article className="message message--system">
                  <p className="message__text">{text.logs.empty}</p>
                </article>
              ) : (
                logs.map((entry) => (
                  <article className="message message--assistant" key={entry.entryId}>
                    <div className="message__meta">
                      <span className="message__role">{entry.source}</span>
                      <span>{formatDateTime(entry.timestamp, text.common.none)}</span>
                    </div>
                    <p className="message__text">{entry.message}</p>
                  </article>
                ))
              )}
            </div>
          </section>

          <section className="status-card status-card--normal" data-testid="plugin-settings-placeholder">
            <p className="section-eyebrow">{text.future.eyebrow}</p>
            <h3 className="section-title">{text.future.title}</h3>
            <p className="section-copy">{text.future.copy}</p>
          </section>
        </div>
      </div>
    </section>
  );
}
