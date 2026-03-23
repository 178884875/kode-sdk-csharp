import { useState, useRef, useEffect } from 'react';
import { History, X, RotateCcw } from 'lucide-react';
import { useSessionHistory } from '../../hooks/useSessionHistory';
import type { SessionSummary } from '../../types/contracts';

type Props = {
  activeSessionId?: string | null;
  onResumed: () => void;
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

export function SessionHistoryPanel({ activeSessionId, onResumed }: Props) {
  const [open, setOpen] = useState(false);
  const panelRef = useRef<HTMLDivElement>(null);
  const { sessions, isLoading, error, isResuming, resumeError, resume } = useSessionHistory(() => {
    setOpen(false);
    onResumed();
  });

  // Close on outside click
  useEffect(() => {
    if (!open) return;
    function handleClick(e: MouseEvent) {
      if (panelRef.current && !panelRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [open]);

  return (
    <div className="session-history-root" ref={panelRef}>
      <button
        className="session-history-trigger"
        title="历史会话"
        aria-label="历史会话"
        aria-expanded={open}
        onClick={() => setOpen(prev => !prev)}
      >
        <History size={16} strokeWidth={1.75} />
      </button>

      {open && (
        <div className="session-history-panel" data-testid="session-history-panel">
          <div className="session-history-panel__header">
            <span className="session-history-panel__title">历史会话</span>
            <button className="session-history-panel__close" onClick={() => setOpen(false)} aria-label="关闭">
              <X size={14} strokeWidth={1.75} />
            </button>
          </div>

          {isLoading && sessions.length === 0 && (
            <div className="session-history-panel__empty">加载中…</div>
          )}
          {error && (
            <div className="session-history-panel__empty session-history-panel__empty--error">{error}</div>
          )}
          {resumeError && (
            <div className="session-history-panel__empty session-history-panel__empty--error">{resumeError}</div>
          )}

          {sessions.length === 0 && !isLoading && !error && (
            <div className="session-history-panel__empty">暂无历史会话</div>
          )}

          <ul className="session-history-list" role="list">
            {sessions.map(session => {
              const isCurrent = session.sessionId === activeSessionId;
              return (
                <li key={session.sessionId} className={`session-history-item${isCurrent ? ' session-history-item--current' : ''}`}>
                  <div className="session-history-item__meta">
                    <StatusBadge status={session.status} />
                    {isCurrent && <span className="session-history-item__current-label">当前</span>}
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
                      onClick={() => resume(session.sessionId)}
                      title="恢复此会话"
                    >
                      <RotateCcw size={13} strokeWidth={1.75} />
                      恢复
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
