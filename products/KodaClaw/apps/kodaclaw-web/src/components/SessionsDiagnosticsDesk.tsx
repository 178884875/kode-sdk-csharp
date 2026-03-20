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
        diagnosticsTimeline: "诊断时间线",
        diagnosticBundle: "诊断证据包",
      },
      sessionDetail: {
        loading: "正在加载所选会话详情...",
        empty: "选择一个会话以检查详情与时间线。",
        session: "会话",
        messagesAndApprovals: "消息 / 待审批",
        breakpoint: "断点",
        lastEvent: "最近事件",
        none: "无",
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
        diagnosticsTimeline: "Diagnostics timeline",
        diagnosticBundle: "Diagnostic bundle",
      },
      sessionDetail: {
        loading: "Loading selected session detail...",
        empty: "Select a session to inspect detail and timeline.",
        session: "Session",
        messagesAndApprovals: "Messages / Pending approvals",
        breakpoint: "Breakpoint",
        lastEvent: "Last event",
        none: "none",
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
        const hasCurrent = payload.sessions.some(
          (session) => session.sessionId === selectedSessionId,
        );
        if (!hasCurrent) {
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
  }, [defaultLimit, refreshToken, selectedSessionId, text.errors.sessions]);

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

      <div className="control-plane-split-pane">
        <section className="timeline" data-testid="sessions-list">
          <div className="timeline__header">
            <h3 className="section-title">{text.sections.sessionIndex}</h3>
            <span className="composer__status">
              {isLoadingList ? text.common.loading : text.common.items(sessions.length)}
            </span>
          </div>
          <div className="timeline__body">
            {sessions.map((session) => (
              <button
                key={session.sessionId}
                type="button"
                className={`desk-tab control-plane-list-button ${selectedSessionId === session.sessionId ? "control-plane-list-button--selected" : ""}`}
                data-testid={`session-select-${session.sessionId}`}
                onClick={() => setSelectedSessionId(session.sessionId)}
                aria-pressed={selectedSessionId === session.sessionId}
              >
                <span className="metric-label">{formatSessionKind(session.sessionKind)}</span>
                <span>{session.sessionId}</span>
              </button>
            ))}
          </div>
        </section>

        <div className="control-plane-stack">
          <section className="status-card status-card--normal" data-testid="session-detail">
            <p className="section-eyebrow">{text.sections.sessionDetail}</p>
            {isLoadingDetail ? (
              <p className="section-copy">{text.sessionDetail.loading}</p>
            ) : sessionDetail ? (
              <div className="control-plane-summary-grid">
                <div className="metric-item">
                  <span className="metric-label">{text.sessionDetail.session}</span>
                  <span className="metric-value metric-value--path">{sessionDetail.sessionId}</span>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.sessionDetail.messagesAndApprovals}</span>
                  <span className="metric-value">
                    {sessionDetail.status.messageCount} / {sessionDetail.status.pendingApprovalCount}
                  </span>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.sessionDetail.breakpoint}</span>
                  <span className="metric-value">{sessionDetail.status.breakpointState ?? text.sessionDetail.none}</span>
                </div>
                <div className="metric-item">
                  <span className="metric-label">{text.sessionDetail.lastEvent}</span>
                  <span className="metric-value">{formatTimestamp(sessionDetail.lastEventAt)}</span>
                </div>
              </div>
            ) : (
              <p className="section-copy">{text.sessionDetail.empty}</p>
            )}
          </section>

          <section className="timeline" data-testid="diagnostics-timeline">
            <div className="timeline__header">
              <h3 className="section-title">{text.sections.diagnosticsTimeline}</h3>
              <span className="composer__status">{timeline.length}</span>
            </div>
            <div className="timeline__body">
              {isLoadingDetail ? (
                <p className="section-copy">{text.timeline.loading}</p>
              ) : timeline.length > 0 ? (
                timeline.map((event) => (
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

          <section className="status-card status-card--warning" data-testid="diagnostic-bundle-export">
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
                disabled={isLoadingList || isLoadingDetail || isExportingBundle}
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
    </section>
  );
}
