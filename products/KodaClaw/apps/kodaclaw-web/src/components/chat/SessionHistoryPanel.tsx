import { useState, useRef, useEffect } from 'react';
import { History, X, RotateCcw, AlertTriangle } from 'lucide-react';
import { useSessionHistory } from '../../hooks/useSessionHistory';
import { useLocaleText } from '../../i18n/I18nProvider';
import type { SessionSummary } from '../../types/contracts';

type Props = {
  activeSessionId?: string | null;
  isStreaming?: boolean;
  onResumed: (sessionId: string) => void;
};

function formatRelativeTime(isoString?: string | null): string {
  if (!isoString) return '—';
  const diff = Date.now() - new Date(isoString).getTime();
  const mins = Math.floor(diff / 60_000);
  if (mins < 1) return '刚刚';
  if (mins < 60) return `${mins} 分钟前`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours} 小时前`;
  const days = Math.floor(hours / 24);
  return `${days} 天前`;
}

function StatusBadge({ status }: { status: SessionSummary['status'] }) {
  const breakpoint = status.breakpointState?.toLowerCase() ?? '';
  let cls = 'session-history-badge';
  let label = '空闲';
  if (breakpoint === 'running' || breakpoint === 'active') { cls += ' session-history-badge--active'; label = '运行中'; }
  else if (breakpoint === 'error' || breakpoint === 'failed') { cls += ' session-history-badge--error'; label = '错误'; }
  else { cls += ' session-history-badge--idle'; }
  return <span className={cls}>{label}</span>;
}

export function SessionHistoryPanel({ activeSessionId, isStreaming, onResumed }: Props) {
  const [open, setOpen] = useState(false);
  const [pendingResumeSessionId, setPendingResumeSessionId] = useState<string | null>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  const text = useLocaleText({
    zh: {
      title: '历史会话',
      close: '关闭',
      loading: '加载中…',
      empty: '暂无历史会话',
      resume: '恢复',
      current: '当前',
      streamInterruptBody: 'Koda 正在回复中，确认中断并切换到此会话？',
      confirmOk: '确认中断',
      confirmCancel: '取消',
    },
    en: {
      title: 'Session History',
      close: 'Close',
      loading: 'Loading…',
      empty: 'No sessions yet',
      resume: 'Resume',
      current: 'Current',
      streamInterruptBody: 'Koda is responding. Interrupt and switch to this session?',
      confirmOk: 'Interrupt',
      confirmCancel: 'Cancel',
    },
  });

  const { sessions, isLoading, error, isResuming, resumeError, resume, load } = useSessionHistory((resumedSessionId: string) => {
    setOpen(false);
    setPendingResumeSessionId(null);
    onResumed(resumedSessionId);
  });

  // Load on open
  useEffect(() => {
    if (open) load();
  }, [open, load]);

  // Close on outside click
  useEffect(() => {
    if (!open) return;
    function handleClick(e: MouseEvent) {
      if (panelRef.current && !panelRef.current.contains(e.target as Node)) {
        setOpen(false);
        setPendingResumeSessionId(null);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [open]);

  function handleResumeClick(sessionId: string) {
    if (isStreaming) {
      setPendingResumeSessionId(sessionId);
    } else {
      resume(sessionId);
    }
  }

  return (
    <div className="session-history-root" ref={panelRef}>
      <button
        className="session-history-trigger"
        title="历史会话"
        aria-label="历史会话"
        aria-expanded={open}
        onClick={() => { setOpen(prev => !prev); setPendingResumeSessionId(null); }}
      >
        <History size={16} strokeWidth={1.75} />
      </button>

      {open && (
        <div className="session-history-panel" data-testid="session-history-panel">
          <div className="session-history-panel__header">
            <span className="session-history-panel__title">{text.title}</span>
            <button className="session-history-panel__close" onClick={() => { setOpen(false); setPendingResumeSessionId(null); }} aria-label={text.close}>
              <X size={14} strokeWidth={1.75} />
            </button>
          </div>

          {/* KC-BUG-302: stream interrupt confirm banner */}
          {pendingResumeSessionId && (
            <div className="session-history-interrupt-confirm">
              <AlertTriangle size={13} strokeWidth={2} className="session-history-interrupt-confirm__icon" />
              <span className="session-history-interrupt-confirm__body">{text.streamInterruptBody}</span>
              <div className="session-history-interrupt-confirm__actions">
                <button
                  className="session-history-interrupt-confirm__btn session-history-interrupt-confirm__btn--cancel"
                  onClick={() => setPendingResumeSessionId(null)}
                >
                  {text.confirmCancel}
                </button>
                <button
                  className="session-history-interrupt-confirm__btn session-history-interrupt-confirm__btn--ok"
                  disabled={isResuming}
                  onClick={() => resume(pendingResumeSessionId)}
                >
                  {text.confirmOk}
                </button>
              </div>
            </div>
          )}

          {isLoading && sessions.length === 0 && (
            <div className="session-history-panel__empty">{text.loading}</div>
          )}
          {error && (
            <div className="session-history-panel__empty session-history-panel__empty--error">{error}</div>
          )}
          {resumeError && (
            <div className="session-history-panel__empty session-history-panel__empty--error">{resumeError}</div>
          )}

          {sessions.length === 0 && !isLoading && !error && (
            <div className="session-history-panel__empty">{text.empty}</div>
          )}

          <ul className="session-history-list" role="list">
            {sessions.map(session => {
              const isCurrent = session.sessionId === activeSessionId;
              const isPending = session.sessionId === pendingResumeSessionId;
              return (
                <li key={session.sessionId} className={`session-history-item${isCurrent ? ' session-history-item--current' : ''}${isPending ? ' session-history-item--pending' : ''}`}>
                  <div className="session-history-item__meta">
                    <StatusBadge status={session.status} />
                    {isCurrent && <span className="session-history-item__current-label">{text.current}</span>}
                  </div>
                  <div className="session-history-item__title">
                    {session.title ?? `${session.sessionId.slice(0, 8)}…`}
                  </div>
                  <div className="session-history-item__time">
                    {formatRelativeTime(session.lastEventAt ?? session.createdAt)}
                  </div>
                  {!isCurrent && (
                    <button
                      className="session-history-item__resume"
                      disabled={isResuming}
                      onClick={() => handleResumeClick(session.sessionId)}
                      title={text.resume}
                    >
                      <RotateCcw size={13} strokeWidth={1.75} />
                      {text.resume}
                    </button>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}
