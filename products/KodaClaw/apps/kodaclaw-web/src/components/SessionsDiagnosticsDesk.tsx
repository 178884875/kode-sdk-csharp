import { useEffect, useMemo, useState } from "react";
import {
  exportDiagnosticBundle,
  fetchDiagnosticsTimeline,
  fetchSessionDetail,
  fetchSessions,
} from "../lib/api";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import { getRuntimeConfig } from "../lib/config";
import type {
  DiagnosticBundleExportRequest,
  DiagnosticBundleExportResponse,
  DiagnosticEvent,
  SessionDetail,
  SessionSummary,
} from "../types/contracts";
import "./ControlPlaneDesk.css";

type SessionsDiagnosticsDeskProps = {
  defaultLimit?: number;
  focusRequest?: {
    sessionId: string;
    requestId: number;
  } | null;
  onFocusRequestConsumed?: () => void;
};

const DiagnosticBundleTimelineLimit = 120;

function buildDiagnosticBundleRequest(
  sessionId: string | null,
): DiagnosticBundleExportRequest {
  const runtimeConfig = getRuntimeConfig();
  const request: DiagnosticBundleExportRequest = {
    sessionId,
    timelineLimit: DiagnosticBundleTimelineLimit,
  };

  if (runtimeConfig.desktopMode) {
    request.desktopContext = {
      desktopMode: true,
      platform: runtimeConfig.platform || "unknown",
      appVersion: runtimeConfig.appVersion || "unknown",
      releaseChannel: runtimeConfig.releaseChannel,
      gatewayLifecycleMode: runtimeConfig.gatewayLifecycleMode,
    };
  }

  return request;
}

export function SessionsDiagnosticsDesk({
  defaultLimit = 20,
  focusRequest = null,
  onFocusRequestConsumed,
}: SessionsDiagnosticsDeskProps) {
  const { formatDateTime } = useI18n();
  const text = useLocaleText({
    zh: {
      common: {
        none: "暂无",
        loading: "加载中...",
        refreshing: "正在刷新...",
        items: (count: number) => `${count} 个会话`,
      },
      eyebrow: "控制平面",
      title: "会话 / 诊断台",
      summary: {
        loading: "正在汇总活跃与最近会话。",
        empty: "还没有可检查的会话，先触发一次聊天或自动化运行。",
        ready: (count: number) => `已加载 ${count} 个会话。选择一条轨迹，查看诊断时间线或导出脱敏证据包。`,
      },
      refresh: "刷新会话",
      sections: {
        sessionIndex: "会话索引",
        sessionDetail: "会话详情",
        promptReport: "提示词报告",
        diagnosticsTimeline: "诊断时间线",
        diagnosticBundle: "诊断证据包",
      },
      sessionDetail: {
        loading: "正在加载所选会话详情...",
        empty: "选择一个会话以检查详情与时间线。",
        session: "会话",
        sessionFocus: "当前焦点",
        kind: "类型",
        createdAt: "创建",
        messagesAndApprovals: "消息 / 待审批",
        messageBreakdown: "用户 / 助手 / 工具",
        approvals: "待审批调用",
        traceIndex: "最后 SFP",
        breakpoint: "断点",
        lastEvent: "最近事件",
        promptProfile: "提示词画像",
        promptSize: "提示词大小",
        promptSizeValue: (count: number) => `${count} 字符`,
        promptBudget: "提示词预算",
        promptBudgetValue: (used: number, budget: number, remaining: number) =>
          `${used} / ${budget} 字符，剩余 ${remaining}`,
        promptGeneratedAt: "最近生成",
        loadedContextFiles: "上下文文件",
        truncationState: "裁剪状态",
        truncationOn: "已裁剪",
        truncationOff: "未裁剪",
        truncatedContextFiles: "被裁剪文件",
        truncationNotes: "裁剪说明",
        promptDelta: "最近变化",
        previousPromptGeneratedAt: "上一版生成",
        charDelta: "字符变化",
        charDeltaValue: (delta: number) => `${delta >= 0 ? "+" : ""}${delta} 字符`,
        truncationChanged: "裁剪状态已变化",
        addedContextFiles: "新增上下文",
        removedContextFiles: "移除上下文",
        noPromptDelta: "当前没有上一版提示词可供比较。",
        promptHistory: "最近版本",
        promptPreview: "系统提示词",
        promptUnavailable: "当前会话还没有可用的提示词报告。",
        none: "无",
      },
      sessionList: {
        activeMain: "主链路",
        pendingApprovals: (count: number) => `${count} 个待审批`,
        messages: (count: number) => `${count} 条消息`,
      },
      timeline: {
        loading: "正在加载时间线...",
        empty: "当前会话没有可显示的诊断事件。",
      },
      bundle: {
        requestedSession: "请求范围",
        crossSession: "跨会话诊断快照",
        timelineWindow: "时间线窗口",
        timelineWindowValue: `${DiagnosticBundleTimelineLimit} 条事件上限，默认脱敏`,
        export: "导出诊断包",
        exporting: "正在导出...",
        exportedFor: (sessionId: string) => `诊断包已为 ${sessionId} 导出。`,
        exportedGeneric: "诊断包已导出。",
        exportFailed: "导出诊断包失败。",
        bundlePath: "包路径",
        workspaceRoot: "工作区根目录",
        manifest: "Manifest",
        redactionPosture: "脱敏姿态",
        included: "包含",
        excluded: "排除",
        rawSecrets: "原始 Secrets",
        messageBodies: "消息正文",
      },
      errors: {
        sessions: "拉取会话列表失败。",
        sessionDetail: "拉取会话详情失败。",
      },
      sessionKindLabels: {
        Main: "主会话",
        Automation: "自动化",
        ChannelDirectMessage: "渠道私信",
        ChannelGroup: "渠道群聊",
        Plugin: "插件",
      },
    },
    en: {
      common: {
        none: "n/a",
        loading: "Loading...",
        refreshing: "Refreshing...",
        items: (count: number) => `${count} sessions`,
      },
      eyebrow: "Control Plane",
      title: "Sessions & Diagnostics",
      summary: {
        loading: "Collecting active and recent sessions.",
        empty: "No sessions available yet. Trigger a chat run first.",
        ready: (count: number) => `${count} sessions loaded. Pick one to inspect diagnostics timeline or export a redacted bundle.`,
      },
      refresh: "Refresh sessions",
      sections: {
        sessionIndex: "Session index",
        sessionDetail: "Session detail",
        promptReport: "Prompt report",
        diagnosticsTimeline: "Diagnostics timeline",
        diagnosticBundle: "Diagnostic bundle",
      },
      sessionDetail: {
        loading: "Loading selected session detail...",
        empty: "Select a session to inspect detail and timeline.",
        session: "Session",
        sessionFocus: "Current focus",
        kind: "Kind",
        createdAt: "Created",
        messagesAndApprovals: "Messages / Pending approvals",
        messageBreakdown: "User / Assistant / Tools",
        approvals: "Pending approval calls",
        traceIndex: "Last SFP",
        breakpoint: "Breakpoint",
        lastEvent: "Last event",
        promptProfile: "Prompt profile",
        promptSize: "Prompt size",
        promptSizeValue: (count: number) => `${count} chars`,
        promptBudget: "Prompt budget",
        promptBudgetValue: (used: number, budget: number, remaining: number) =>
          `${used} / ${budget} chars, ${remaining} remaining`,
        promptGeneratedAt: "Last generated",
        loadedContextFiles: "Context files",
        truncationState: "Truncation",
        truncationOn: "Truncated",
        truncationOff: "Not truncated",
        truncatedContextFiles: "Truncated files",
        truncationNotes: "Truncation notes",
        promptDelta: "Recent changes",
        previousPromptGeneratedAt: "Previous build",
        charDelta: "Character delta",
        charDeltaValue: (delta: number) => `${delta >= 0 ? "+" : ""}${delta} chars`,
        truncationChanged: "Truncation state changed",
        addedContextFiles: "Added context",
        removedContextFiles: "Removed context",
        noPromptDelta: "No previous prompt build is available for comparison.",
        promptHistory: "Recent builds",
        promptPreview: "System prompt",
        promptUnavailable: "No prompt report is available for this session yet.",
        none: "none",
      },
      sessionList: {
        activeMain: "Main line",
        pendingApprovals: (count: number) => `${count} pending approvals`,
        messages: (count: number) => `${count} messages`,
      },
      timeline: {
        loading: "Loading timeline...",
        empty: "No diagnostics events found for the selected session.",
      },
      bundle: {
        requestedSession: "Requested session",
        crossSession: "Cross-session diagnostics snapshot",
        timelineWindow: "Timeline window",
        timelineWindowValue: `${DiagnosticBundleTimelineLimit} events max, redacted by default`,
        export: "Export diagnostic bundle",
        exporting: "Exporting...",
        exportedFor: (sessionId: string) => `Diagnostic bundle exported for ${sessionId}.`,
        exportedGeneric: "Diagnostic bundle exported.",
        exportFailed: "Failed to export diagnostic bundle.",
        bundlePath: "Bundle path",
        workspaceRoot: "Workspace root",
        manifest: "Manifest",
        redactionPosture: "Redaction posture",
        included: "included",
        excluded: "excluded",
        rawSecrets: "Raw secrets",
        messageBodies: "Message bodies",
      },
      errors: {
        sessions: "Failed to fetch sessions.",
        sessionDetail: "Failed to fetch session detail.",
      },
      sessionKindLabels: {
        Main: "Main",
        Automation: "Automation",
        ChannelDirectMessage: "Channel direct message",
        ChannelGroup: "Channel group",
        Plugin: "Plugin",
      },
    },
  });

  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [sessionDetail, setSessionDetail] = useState<SessionDetail | null>(null);
  const [timeline, setTimeline] = useState<DiagnosticEvent[]>([]);
  const [isLoadingList, setIsLoadingList] = useState(true);
  const [isLoadingDetail, setIsLoadingDetail] = useState(false);
  const [isExportingBundle, setIsExportingBundle] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [bundleError, setBundleError] = useState<string | null>(null);
  const [bundleNote, setBundleNote] = useState<string | null>(null);
  const [bundleExport, setBundleExport] =
    useState<DiagnosticBundleExportResponse | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  const formatTimestamp = (value?: string | null): string => {
    return formatDateTime(value, text.common.none);
  };

  const formatSessionKind = (kind: string): string => {
    return text.sessionKindLabels[kind as keyof typeof text.sessionKindLabels] ?? kind;
  };

  const formatPromptSize = (count?: number | null): string => {
    if (!count || count <= 0) {
      return text.sessionDetail.none;
    }

    return text.sessionDetail.promptSizeValue(count);
  };

  const formatPromptBudget = (
    used?: number | null,
    budget?: number | null,
    remaining?: number | null,
  ): string => {
    if (!used || !budget || budget <= 0) {
      return text.sessionDetail.none;
    }

    return text.sessionDetail.promptBudgetValue(used, budget, Math.max(remaining ?? 0, 0));
  };

  const formatPromptDelta = (delta?: number | null): string => {
    if (delta == null) {
      return text.sessionDetail.none;
    }

    return text.sessionDetail.charDeltaValue(delta);
  };

  useEffect(() => {
    if (!focusRequest?.sessionId) {
      return;
    }

    setSelectedSessionId(focusRequest.sessionId);
    onFocusRequestConsumed?.();
  }, [focusRequest, onFocusRequestConsumed]);

  useEffect(() => {
    let isDisposed = false;

    async function loadList() {
      setIsLoadingList(true);
      setError(null);

      try {
        const payload = await fetchSessions(defaultLimit);
        if (isDisposed) {
          return;
        }

        setSessions(payload.sessions);
        const preferredSessionId = focusRequest?.sessionId ?? selectedSessionId;
        const hasPreferred = preferredSessionId
          ? payload.sessions.some((session) => session.sessionId === preferredSessionId)
          : false;
        if (hasPreferred && preferredSessionId) {
          setSelectedSessionId(preferredSessionId);
        } else {
          setSelectedSessionId(payload.sessions[0]?.sessionId ?? null);
        }
      } catch (nextError) {
        if (isDisposed) {
          return;
        }

        const detail =
          nextError instanceof Error
            ? nextError.message
            : text.errors.sessions;
        setError(detail);
        setSessions([]);
        setSelectedSessionId(null);
      } finally {
        if (!isDisposed) {
          setIsLoadingList(false);
        }
      }
    }

    void loadList();
    return () => {
      isDisposed = true;
    };
  }, [defaultLimit, focusRequest?.sessionId, refreshToken, selectedSessionId, text.errors.sessions]);

  useEffect(() => {
    let isDisposed = false;

    async function loadDetailAndTimeline(sessionId: string) {
      setIsLoadingDetail(true);
      setError(null);

      try {
        const [detail, timelinePayload] = await Promise.all([
          fetchSessionDetail(sessionId),
          fetchDiagnosticsTimeline({
            sessionId,
            limit: 60,
          }),
        ]);

        if (isDisposed) {
          return;
        }

        setSessionDetail(detail);
        setTimeline(timelinePayload.events);
      } catch (nextError) {
        if (isDisposed) {
          return;
        }

        const detail =
          nextError instanceof Error
            ? nextError.message
            : text.errors.sessionDetail;
        setError(detail);
        setSessionDetail(null);
        setTimeline([]);
      } finally {
        if (!isDisposed) {
          setIsLoadingDetail(false);
        }
      }
    }

    if (!selectedSessionId) {
      setSessionDetail(null);
      setTimeline([]);
      return () => {
        isDisposed = true;
      };
    }

    void loadDetailAndTimeline(selectedSessionId);
    return () => {
      isDisposed = true;
    };
  }, [refreshToken, selectedSessionId, text.errors.sessionDetail]);

  const summaryLine = useMemo(() => {
    if (isLoadingList) {
      return text.summary.loading;
    }

    if (!sessions.length) {
      return text.summary.empty;
    }

    return text.summary.ready(sessions.length);
  }, [isLoadingList, sessions.length, text.summary]);

  const selectedSessionSummary = useMemo(
    () => sessions.find((session) => session.sessionId === selectedSessionId) ?? null,
    [selectedSessionId, sessions],
  );
  const hasFreshSessionDetail =
    selectedSessionId !== null && sessionDetail?.sessionId === selectedSessionId;
  const isHydratingSelection =
    selectedSessionId !== null && !error && (isLoadingDetail || !hasFreshSessionDetail);
  const visibleTimeline = hasFreshSessionDetail ? timeline : [];

  async function handleExportBundle() {
    setBundleError(null);
    setBundleNote(null);
    setIsExportingBundle(true);

    try {
      const exported = await exportDiagnosticBundle(
        buildDiagnosticBundleRequest(selectedSessionId),
      );
      setBundleExport(exported);
      setBundleNote(
        selectedSessionId
          ? text.bundle.exportedFor(selectedSessionId)
          : text.bundle.exportedGeneric,
      );
    } catch (nextError) {
      const detail =
        nextError instanceof Error
          ? nextError.message
          : text.bundle.exportFailed;
      setBundleError(detail);
    } finally {
      setIsExportingBundle(false);
    }
  }

  return (
    <section className="bootstrap-panel control-plane-stack" data-testid="sessions-diagnostics-desk">
      <div className="section-eyebrow">{text.eyebrow}</div>
      <h2 className="section-title">{text.title}</h2>
      <p className="section-copy">{summaryLine}</p>

      <div className="control-plane-toolbar">
        <button
          type="button"
          className="secondary-button"
          data-testid="sessions-refresh"
          onClick={() => setRefreshToken((value) => value + 1)}
          disabled={isLoadingList || isLoadingDetail}
        >
          {isLoadingList || isLoadingDetail ? text.common.refreshing : text.refresh}
        </button>
      </div>

      {error ? (
        <p className="bootstrap-panel__feedback bootstrap-panel__feedback--error" role="alert">
          {error}
        </p>
      ) : null}

      <div className="control-plane-pane-shell">
        <section className="timeline control-plane-pane-rail" data-testid="sessions-list">
          <div className="timeline__header">
            <div>
              <h3 className="section-title">{text.sections.sessionIndex}</h3>
              <p className="section-copy control-plane-compact-copy">{summaryLine}</p>
            </div>
            <span className="composer__status">{isLoadingList ? text.common.loading : text.common.items(sessions.length)}</span>
          </div>
          <div className="timeline__body control-plane-session-list">
            {sessions.map((session) => (
              <button
                key={session.sessionId}
                type="button"
                className={`control-plane-session-card control-plane-list-button ${selectedSessionId === session.sessionId ? "control-plane-list-button--selected" : ""}`}
                data-testid={`session-select-${session.sessionId}`}
                onClick={() => setSelectedSessionId(session.sessionId)}
                aria-pressed={selectedSessionId === session.sessionId}
              >
                <div className="control-plane-session-card__topline">
                  <span className="metric-label">{formatSessionKind(session.sessionKind)}</span>
                  {session.status.isActiveMainSession ? (
                    <span className="control-plane-chip control-plane-chip--active">
                      {text.sessionList.activeMain}
                    </span>
                  ) : null}
                </div>
                <strong className="control-plane-session-card__title">{session.sessionId}</strong>
                <div className="control-plane-session-card__meta">
                  <span>{text.sessionDetail.breakpoint}: {session.status.breakpointState ?? text.sessionDetail.none}</span>
                  <span>{text.sessionDetail.lastEvent}: {formatTimestamp(session.lastEventAt)}</span>
                </div>
                <div className="control-plane-chip-row">
                  <span className="control-plane-chip">{text.sessionList.messages(session.status.messageCount)}</span>
                  {session.status.pendingApprovalCount > 0 ? (
                    <span className="control-plane-chip control-plane-chip--warning">
                      {text.sessionList.pendingApprovals(session.status.pendingApprovalCount)}
                    </span>
                  ) : null}
                </div>
              </button>
            ))}
          </div>
        </section>

        <div className="control-plane-pane-stage">
          <section className="status-card status-card--normal control-plane-stage-hero" data-testid="session-detail">
            <p className="section-eyebrow">{text.sections.sessionDetail}</p>
            {isHydratingSelection ? (
              <p className="section-copy">{text.sessionDetail.loading}</p>
            ) : sessionDetail && hasFreshSessionDetail ? (
              <div className="control-plane-stack">
                <div className="control-plane-stage-hero__header">
                  <div>
                    <h3 className="section-title control-plane-card-title">{sessionDetail.sessionId}</h3>
                    <p className="section-copy control-plane-compact-copy">
                      {text.sessionDetail.sessionFocus} · {formatSessionKind(sessionDetail.sessionKind)}
                    </p>
                  </div>
                  <div className="control-plane-chip-row">
                    <span className="control-plane-chip">{text.sessionDetail.breakpoint}: {sessionDetail.status.breakpointState ?? text.sessionDetail.none}</span>
                    {sessionDetail.status.pendingApprovalCount > 0 ? (
                      <span className="control-plane-chip control-plane-chip--warning">
                        {text.sessionList.pendingApprovals(sessionDetail.status.pendingApprovalCount)}
                      </span>
                    ) : null}
                  </div>
                </div>

                <div className="control-plane-summary-grid">
                  <div className="metric-item">
                    <span className="metric-label">{text.sessionDetail.session}</span>
                    <span className="metric-value metric-value--path">{sessionDetail.sessionId}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.sessionDetail.kind}</span>
                    <span className="metric-value">{formatSessionKind(sessionDetail.sessionKind)}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.sessionDetail.createdAt}</span>
                    <span className="metric-value">{formatTimestamp(sessionDetail.createdAt)}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.sessionDetail.lastEvent}</span>
                    <span className="metric-value">{formatTimestamp(sessionDetail.lastEventAt)}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.sessionDetail.messagesAndApprovals}</span>
                    <span className="metric-value">
                      {sessionDetail.status.messageCount} / {sessionDetail.status.pendingApprovalCount}
                    </span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.sessionDetail.messageBreakdown}</span>
                    <span className="metric-value">
                      {sessionDetail.userMessageCount} / {sessionDetail.assistantMessageCount} / {sessionDetail.toolCallCount}
                    </span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.sessionDetail.approvals}</span>
                    <span className="metric-value">{sessionDetail.pendingApprovalCallIds.length}</span>
                  </div>
                  <div className="metric-item">
                    <span className="metric-label">{text.sessionDetail.traceIndex}</span>
                    <span className="metric-value">{sessionDetail.lastSfpIndex}</span>
                  </div>
                </div>

                <div className="metric-item control-plane-stack" data-testid="session-prompt-report">
                  <span className="metric-label">{text.sections.promptReport}</span>
                  {sessionDetail.promptReport ? (
                    <>
                      <span className="metric-label">{text.sessionDetail.promptProfile}</span>
                      <span className="metric-value">{sessionDetail.promptReport.profileId}</span>
                      <span className="metric-label">{text.sessionDetail.promptSize}</span>
                      <span className="metric-value">{formatPromptSize(sessionDetail.promptReport.characterCount)}</span>
                      <span className="metric-label">{text.sessionDetail.promptBudget}</span>
                      <span className="metric-value">
                        {formatPromptBudget(
                          sessionDetail.promptReport.characterCount,
                          sessionDetail.promptReport.characterBudget,
                          sessionDetail.promptReport.remainingCharacterBudget,
                        )}
                      </span>
                      <span className="metric-label">{text.sessionDetail.promptGeneratedAt}</span>
                      <span className="metric-value">{formatTimestamp(sessionDetail.promptReport.generatedAt)}</span>
                      <span className="metric-label">{text.sessionDetail.truncationState}</span>
                      <span className="metric-value">
                        {sessionDetail.promptReport.wasTruncated
                          ? text.sessionDetail.truncationOn
                          : text.sessionDetail.truncationOff}
                      </span>
                      <span className="metric-label">{text.sessionDetail.loadedContextFiles}</span>
                      {sessionDetail.promptReport.loadedContextFiles.length > 0 ? (
                        sessionDetail.promptReport.loadedContextFiles.map((path) => (
                          <span key={path} className="metric-value metric-value--path">{path}</span>
                        ))
                      ) : (
                        <span className="metric-value">{text.sessionDetail.none}</span>
                      )}
                      {sessionDetail.promptReport.wasTruncated ? (
                        <>
                          <span className="metric-label">{text.sessionDetail.truncatedContextFiles}</span>
                          {(sessionDetail.promptReport.truncatedContextFiles?.length ?? 0) > 0 ? (
                            sessionDetail.promptReport.truncatedContextFiles!.map((path) => (
                              <span key={path} className="metric-value metric-value--path">{path}</span>
                            ))
                          ) : (
                            <span className="metric-value">{text.sessionDetail.none}</span>
                          )}
                          <span className="metric-label">{text.sessionDetail.truncationNotes}</span>
                          {(sessionDetail.promptReport.truncationNotes?.length ?? 0) > 0 ? (
                            sessionDetail.promptReport.truncationNotes!.map((note) => (
                              <span key={note} className="metric-value">{note}</span>
                            ))
                          ) : (
                            <span className="metric-value">{text.sessionDetail.none}</span>
                          )}
                        </>
                      ) : null}
                      <span className="metric-label">{text.sessionDetail.promptDelta}</span>
                      {sessionDetail.promptReportDelta ? (
                        <>
                          <span className="metric-label">{text.sessionDetail.previousPromptGeneratedAt}</span>
                          <span className="metric-value">{formatTimestamp(sessionDetail.promptReportDelta.previousGeneratedAt)}</span>
                          <span className="metric-label">{text.sessionDetail.charDelta}</span>
                          <span className="metric-value">{formatPromptDelta(sessionDetail.promptReportDelta.characterCountDelta)}</span>
                          {sessionDetail.promptReportDelta.truncationStateChanged ? (
                            <span className="metric-value">{text.sessionDetail.truncationChanged}</span>
                          ) : null}
                          <span className="metric-label">{text.sessionDetail.addedContextFiles}</span>
                          {sessionDetail.promptReportDelta.addedContextFiles.length > 0 ? (
                            sessionDetail.promptReportDelta.addedContextFiles.map((path) => (
                              <span key={path} className="metric-value metric-value--path">{path}</span>
                            ))
                          ) : (
                            <span className="metric-value">{text.sessionDetail.none}</span>
                          )}
                          <span className="metric-label">{text.sessionDetail.removedContextFiles}</span>
                          {sessionDetail.promptReportDelta.removedContextFiles.length > 0 ? (
                            sessionDetail.promptReportDelta.removedContextFiles.map((path) => (
                              <span key={path} className="metric-value metric-value--path">{path}</span>
                            ))
                          ) : (
                            <span className="metric-value">{text.sessionDetail.none}</span>
                          )}
                        </>
                      ) : (
                        <span className="metric-value">{text.sessionDetail.noPromptDelta}</span>
                      )}
                      <span className="metric-label">{text.sessionDetail.promptHistory}</span>
                      {(sessionDetail.recentPromptReports?.length ?? 0) > 0 ? (
                        sessionDetail.recentPromptReports!.map((report, index) => (
                          <span key={`${report.generatedAt}-${index}`} className="metric-value">
                            {report.profileId} · {formatPromptSize(report.characterCount)} · {formatTimestamp(report.generatedAt)}
                          </span>
                        ))
                      ) : (
                        <span className="metric-value">{text.sessionDetail.none}</span>
                      )}
                      <span className="metric-label">{text.sessionDetail.promptPreview}</span>
                      <pre className="message__text">{sessionDetail.promptReport.systemPrompt}</pre>
                    </>
                  ) : (
                    <span className="metric-value">{text.sessionDetail.promptUnavailable}</span>
                  )}
                </div>
              </div>
            ) : selectedSessionSummary ? (
              <div className="control-plane-summary-grid">
                <div className="metric-item">
                  <span className="metric-label">{text.sessionDetail.session}</span>
                  <span className="metric-value metric-value--path">{selectedSessionSummary.sessionId}</span>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.sessionDetail.kind}</span>
                  <span className="metric-value">{formatSessionKind(selectedSessionSummary.sessionKind)}</span>
                </div>
              </div>
            ) : (
              <p className="section-copy">{text.sessionDetail.empty}</p>
            )}
          </section>

          <div className="control-plane-two-pane control-plane-two-pane--diagnostics">
            <section className="timeline control-plane-stage-panel" data-testid="diagnostics-timeline">
              <div className="timeline__header">
                <h3 className="section-title">{text.sections.diagnosticsTimeline}</h3>
                <span className="composer__status">{visibleTimeline.length}</span>
              </div>
              <div className="timeline__body">
                {isHydratingSelection ? (
                  <p className="section-copy">{text.timeline.loading}</p>
                ) : visibleTimeline.length > 0 ? (
                  visibleTimeline.map((event) => (
                    <article key={event.id} className="message message--system control-plane-stack">
                      <div className="message__meta">
                        <span className="message__role">{event.level} · {event.eventType}</span>
                        <span>{formatTimestamp(event.timestamp)}</span>
                      </div>
                      <p className="control-plane-zero-margin">{event.message}</p>
                      <span className="metric-label">{event.source}</span>
                    </article>
                  ))
                ) : (
                  <p className="section-copy">{text.timeline.empty}</p>
                )}
              </div>
            </section>

            <section className="status-card status-card--warning control-plane-stage-panel" data-testid="diagnostic-bundle-export">
              <p className="section-eyebrow">{text.sections.diagnosticBundle}</p>
              <div className="control-plane-summary-grid">
                <div className="metric-item">
                  <span className="metric-label">{text.bundle.requestedSession}</span>
                  <span className="metric-value">
                    {selectedSessionId ?? text.bundle.crossSession}
                  </span>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.bundle.timelineWindow}</span>
                  <span className="metric-value">{text.bundle.timelineWindowValue}</span>
                </div>
              </div>

              <div className="bootstrap-form__actions">
                <button
                  type="button"
                  className="secondary-button"
                  data-testid="diagnostic-bundle-export-button"
                  onClick={() => void handleExportBundle()}
                  disabled={isLoadingList || isHydratingSelection || isExportingBundle}
                >
                  {isExportingBundle ? text.bundle.exporting : text.bundle.export}
                </button>
              </div>

              {bundleError ? (
                <p
                  className="bootstrap-panel__feedback bootstrap-panel__feedback--error"
                  data-testid="diagnostic-bundle-export-error"
                >
                  {bundleError}
                </p>
              ) : null}

              {bundleNote ? (
                <p
                  className="bootstrap-panel__feedback bootstrap-panel__feedback--success"
                  data-testid="diagnostic-bundle-export-note"
                >
                  {bundleNote}
                </p>
              ) : null}

              {bundleExport ? (
                <div className="metric-item control-plane-stack" data-testid="diagnostic-bundle-export-result">
                  <span className="metric-label">{text.bundle.bundlePath}</span>
                  <span className="metric-value metric-value--path">
                    {bundleExport.bundlePath}
                  </span>
                  <span className="metric-label">{text.bundle.workspaceRoot}</span>
                  <span className="metric-value metric-value--path">
                    {bundleExport.workspaceRootPath}
                  </span>
                  <span className="metric-label">{text.bundle.manifest}</span>
                  <span className="metric-value">
                    {bundleExport.manifest.entries.length} entries · {formatTimestamp(bundleExport.generatedAt)}
                  </span>
                  <span className="metric-label">{text.bundle.redactionPosture}</span>
                  <span className="metric-value">
                    {text.bundle.rawSecrets}: {bundleExport.manifest.redactionSummary.includesRawSecrets ? text.bundle.included : text.bundle.excluded}
                    {" · "}
                    {text.bundle.messageBodies}: {bundleExport.manifest.redactionSummary.includesMessageBodies ? text.bundle.included : text.bundle.excluded}
                  </span>
                  <ul data-testid="diagnostic-bundle-redaction-notes" className="risk-briefing__list">
                    {bundleExport.manifest.redactionSummary.notes.map((note) => (
                      <li key={note} className="section-copy">
                        {note}
                      </li>
                    ))}
                  </ul>
                </div>
              ) : null}
            </section>
          </div>
        </div>
      </div>
    </section>
  );
}
