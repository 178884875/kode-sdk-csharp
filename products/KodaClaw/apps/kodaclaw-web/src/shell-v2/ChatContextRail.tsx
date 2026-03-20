import { useEffect, useMemo, useState } from "react";
import { useI18n, useLocaleText } from "../i18n/I18nProvider";
import { fetchSessions } from "../lib/api";
import type { SessionSummary } from "../types/contracts";

type ChatContextRailProps = {
  activeSessionId?: string | null;
  onOpenSessionsDesk: () => void;
  onOpenSessionDetail: (sessionId: string) => void;
  defaultLimit?: number;
};

export function ChatContextRail({
  activeSessionId,
  onOpenSessionsDesk,
  onOpenSessionDetail,
  defaultLimit = 8,
}: ChatContextRailProps) {
  const { formatDateTime } = useI18n();
  const text = useLocaleText({
    zh: {
      eyebrow: "主线程索引",
      title: "会话脉络",
      summary: "当前主会话与最近轨迹保持在同一条视线上，必要时再切到诊断台深挖。",
      loading: "正在同步最近会话…",
      empty: "还没有历史轨迹。先发起一轮对话或自动化运行，让时间线开始积累。",
      error: "无法同步最近会话。",
      current: "当前主会话",
      approvals: (count: number) => `${count} 个待审批`,
      breakpoint: "断点",
      messages: (count: number) => `${count} 条消息`,
      openedAt: "创建",
      lastEventAt: "最近事件",
      openDiagnostics: "打开诊断台",
      refresh: "刷新",
      kinds: {
        Main: "主会话",
        Automation: "自动化",
        ChannelDirectMessage: "渠道私信",
        ChannelGroup: "渠道群聊",
        Plugin: "插件",
      },
      none: "暂无",
    },
    en: {
      eyebrow: "Main thread index",
      title: "Session pulse",
      summary: "Keep the active main session and recent traces in view, then jump into diagnostics only when deeper inspection is needed.",
      loading: "Synchronizing recent sessions...",
      empty: "No recent traces yet. Trigger a chat or automation run to start building the timeline.",
      error: "Failed to synchronize recent sessions.",
      current: "Current main session",
      approvals: (count: number) => `${count} pending approvals`,
      breakpoint: "Breakpoint",
      messages: (count: number) => `${count} messages`,
      openedAt: "Created",
      lastEventAt: "Last event",
      openDiagnostics: "Open diagnostics",
      refresh: "Refresh",
      kinds: {
        Main: "Main",
        Automation: "Automation",
        ChannelDirectMessage: "Channel direct message",
        ChannelGroup: "Channel group",
        Plugin: "Plugin",
      },
      none: "n/a",
    },
  });

  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  useEffect(() => {
    let isDisposed = false;

    async function loadSessions() {
      setIsLoading(true);
      setError(null);

      try {
        const payload = await fetchSessions(defaultLimit);
        if (!isDisposed) {
          setSessions(Array.isArray(payload.sessions) ? payload.sessions : []);
        }
      } catch (nextError) {
        if (!isDisposed) {
          setError(nextError instanceof Error ? nextError.message : text.error);
          setSessions([]);
        }
      } finally {
        if (!isDisposed) {
          setIsLoading(false);
        }
      }
    }

    void loadSessions();

    return () => {
      isDisposed = true;
    };
  }, [defaultLimit, refreshToken, text.error]);

  const orderedSessions = useMemo(() => {
    const safeSessions = Array.isArray(sessions) ? sessions : [];

    if (!activeSessionId) {
      return safeSessions;
    }

    return [...safeSessions].sort((left, right) => {
      if (left.sessionId === activeSessionId) {
        return -1;
      }

      if (right.sessionId === activeSessionId) {
        return 1;
      }

      return 0;
    });
  }, [activeSessionId, sessions]);

  const formatKind = (kind: string): string => {
    return text.kinds[kind as keyof typeof text.kinds] ?? kind;
  };

  return (
    <section className="v2-chat-context" data-testid="v2-chat-context">
      <div className="v2-chat-context__head">
        <div>
          <p className="section-eyebrow">{text.eyebrow}</p>
          <h3 className="v2-chat-context__title">{text.title}</h3>
          <p className="section-copy">{text.summary}</p>
        </div>
        <div className="v2-chat-context__actions">
          <button
            type="button"
            className="secondary-button"
            onClick={() => setRefreshToken((current) => current + 1)}
          >
            {text.refresh}
          </button>
          <button
            type="button"
            className="secondary-button"
            onClick={onOpenSessionsDesk}
          >
            {text.openDiagnostics}
          </button>
        </div>
      </div>

      {isLoading ? <p className="v2-chat-context__state">{text.loading}</p> : null}
      {!isLoading && error ? <p className="v2-chat-context__state v2-chat-context__state--error">{error}</p> : null}
      {!isLoading && !error && orderedSessions.length === 0 ? (
        <p className="v2-chat-context__state">{text.empty}</p>
      ) : null}

      {!isLoading && !error && orderedSessions.length > 0 ? (
        <div className="v2-chat-context__list">
          {orderedSessions.map((session) => {
            const isCurrent = session.sessionId === activeSessionId || session.status.isActiveMainSession;
            return (
              <button
                key={session.sessionId}
                type="button"
                className={`v2-chat-session-card ${isCurrent ? "is-current" : ""}`}
                data-current={isCurrent ? "true" : "false"}
                data-testid={`v2-chat-session-${session.sessionId}`}
                onClick={() => onOpenSessionDetail(session.sessionId)}
              >
                <div className="v2-chat-session-card__topline">
                  <strong className="v2-chat-session-card__id">{session.sessionId}</strong>
                  {isCurrent ? <span className="v2-chat-session-card__pill">{text.current}</span> : null}
                </div>

                <div className="v2-chat-session-card__meta">
                  <span>{formatKind(session.sessionKind)}</span>
                  <span>
                    {text.lastEventAt}: {formatDateTime(session.lastEventAt, text.none)}
                  </span>
                  <span>
                    {text.openedAt}: {formatDateTime(session.createdAt, text.none)}
                  </span>
                </div>

                <div className="v2-chat-session-card__stats">
                  <span>{text.messages(session.status.messageCount)}</span>
                  <span>{text.breakpoint}: {session.status.breakpointState ?? text.none}</span>
                  {session.status.pendingApprovalCount > 0 ? (
                    <span>{text.approvals(session.status.pendingApprovalCount)}</span>
                  ) : null}
                </div>
              </button>
            );
          })}
        </div>
      ) : null}
    </section>
  );
}
