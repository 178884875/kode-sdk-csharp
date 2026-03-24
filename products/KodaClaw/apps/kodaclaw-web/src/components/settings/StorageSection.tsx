import './StorageSection.css';
import { useCallback, useEffect, useRef, useState } from 'react';
import { HardDrive, MessageSquare, Trash2 } from 'lucide-react';
import { fetchStorageUsage, fetchSessions, deleteMainSession } from '../../lib/api';
import { useLocaleText } from '../../i18n/I18nProvider';
import type { StorageUsageResponse, SessionSummary } from '../../types/contracts';
import { ConfirmModal } from '../ui/ConfirmModal';

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

function formatDate(iso?: string | null): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleString(undefined, {
      month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
    });
  } catch {
    return iso;
  }
}

export function StorageSection() {
  const text = useLocaleText({
    zh: {
      sectionTitle: '存储',
      sectionDesc: 'Session 文件夹占用磁盘空间统计与管理。',
      main: '主会话',
      auto: '自动化',
      channel: '渠道会话',
      total: '合计',
      sessions: '个',
      autoPolicy: '自动清理：保留 30 天 / 每任务最近 20 次',
      channelNote: '与渠道绑定，不自动清理',
      historyTitle: '历史主会话',
      historyDesc: '可删除不再需要的历史会话。当前活跃会话无法删除。',
      active: '活跃',
      msgCount: '条消息',
      lastUsed: '最后使用',
      deleteBtn: '删除',
      deleteModalTitle: '删除会话',
      deleteModalDesc: '此操作将永久删除该会话的所有数据，不可恢复。',
      batchDeleteTitle: '批量删除会话',
      batchDeleteDesc: (n: number) => `将永久删除 ${n} 个会话的所有数据，不可恢复。`,
      batchDeleteBtn: (n: number) => `删除 ${n} 条`,
      selectAll: '全选',
      deselectAll: '取消全选',
      confirm: '确认删除',
      close: '取消',
      loading: '加载中…',
      error: '加载失败',
      refresh: '刷新',
      showMore: (n: number) => `加载更多 ${n} 条`,
    },
    en: {
      sectionTitle: 'Storage',
      sectionDesc: 'Disk usage and management for session folders.',
      main: 'Main',
      auto: 'Automation',
      channel: 'Channels',
      total: 'Total',
      sessions: '',
      autoPolicy: 'Auto-cleanup: 30 days / 20 runs per task',
      channelNote: 'Tied to channel bindings, not auto-cleaned',
      historyTitle: 'Session History',
      historyDesc: 'Delete old sessions you no longer need. The active session cannot be deleted.',
      active: 'Active',
      msgCount: 'msgs',
      lastUsed: 'Last used',
      deleteBtn: 'Delete',
      deleteModalTitle: 'Delete Session',
      deleteModalDesc: 'This will permanently delete all data for this session. This cannot be undone.',
      batchDeleteTitle: 'Delete Sessions',
      batchDeleteDesc: (n: number) => `This will permanently delete ${n} sessions. This cannot be undone.`,
      batchDeleteBtn: (n: number) => `Delete ${n}`,
      selectAll: 'Select all',
      deselectAll: 'Deselect',
      confirm: 'Delete',
      close: 'Cancel',
      loading: 'Loading…',
      error: 'Failed to load',
      refresh: 'Refresh',
      showMore: (n: number) => `Load ${n} more`,
    },
  });

  const [usage, setUsage] = useState<StorageUsageResponse | null>(null);
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const [pendingDelete, setPendingDelete] = useState<SessionSummary | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [batchConfirmOpen, setBatchConfirmOpen] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [visibleCount, setVisibleCount] = useState(10);
  const SESSION_PAGE = 10;

  const sessionsRef = useRef<SessionSummary[]>([]);

  const load = useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);
    try {
      const [usageData, sessionsData] = await Promise.all([
        fetchStorageUsage(),
        fetchSessions(100),
      ]);
      setUsage(usageData);
      const mainSessions = sessionsData.sessions.filter(s => s.sessionKind === 'Main');
      setSessions(mainSessions);
      sessionsRef.current = mainSessions;
      setActiveSessionId(mainSessions.length > 0 ? mainSessions[0].sessionId : null);
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'error');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function removeAndRefreshUsage(ids: string[]) {
    const idSet = new Set(ids);
    setSessions(prev => {
      const next = prev.filter(s => !idSet.has(s.sessionId));
      sessionsRef.current = next;
      return next;
    });
    setSelectedIds(prev => {
      const next = new Set(prev);
      ids.forEach(id => next.delete(id));
      return next;
    });
    try {
      const usageData = await fetchStorageUsage();
      setUsage(usageData);
    } catch { /* silent */ }
  }

  async function handleDelete() {
    if (!pendingDelete) return;
    setIsDeleting(true);
    try {
      await deleteMainSession(pendingDelete.sessionId);
      setPendingDelete(null);
      await removeAndRefreshUsage([pendingDelete.sessionId]);
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Delete failed');
    } finally {
      setIsDeleting(false);
    }
  }

  async function handleBatchDelete() {
    if (selectedIds.size === 0) return;
    setIsDeleting(true);
    const ids = [...selectedIds];
    try {
      const results = await Promise.allSettled(ids.map(id => deleteMainSession(id)));
      const succeeded = ids.filter((_, i) => results[i].status === 'fulfilled');
      setBatchConfirmOpen(false);
      await removeAndRefreshUsage(succeeded);
      const failed = ids.length - succeeded.length;
      if (failed > 0) alert(`${failed} 条删除失败，其余已删除。`);
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Delete failed');
    } finally {
      setIsDeleting(false);
    }
  }

  const deletableSessions = sessions.slice(0, visibleCount).filter(s => s.sessionId !== activeSessionId);
  const allVisibleSelected = deletableSessions.length > 0 && deletableSessions.every(s => selectedIds.has(s.sessionId));

  function toggleSelectAll() {
    if (allVisibleSelected) {
      setSelectedIds(prev => {
        const next = new Set(prev);
        deletableSessions.forEach(s => next.delete(s.sessionId));
        return next;
      });
    } else {
      setSelectedIds(prev => {
        const next = new Set(prev);
        deletableSessions.forEach(s => next.add(s.sessionId));
        return next;
      });
    }
  }

  function toggleSelect(id: string) {
    setSelectedIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const totalBytes = usage?.totalSizeBytes ?? 0;

  return (
    <div className="settings-section" data-testid="settings-storage-section">
      <h2 className="settings-section-title">
        <HardDrive size={16} strokeWidth={1.75} style={{ marginRight: 6, verticalAlign: 'middle' }} />
        {text.sectionTitle}
      </h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

      {isLoading && !usage && <p className="settings-section-desc">{text.loading}</p>}
      {loadError && (
        <p className="settings-section-desc" style={{ color: 'var(--error)' }}>
          {text.error}: {loadError}&nbsp;
          <button className="btn btn--ghost btn--sm" onClick={() => void load()}>{text.refresh}</button>
        </p>
      )}

      {/* ── Usage cards ── */}
      {usage && (
        <>
          <div className="storage-usage-cards">
            <UsageCard
              label={text.main}
              count={usage.main.count}
              countSuffix={text.sessions}
              sizeBytes={usage.main.sizeBytes}
              totalBytes={totalBytes}
              accent
            />
            <UsageCard
              label={text.auto}
              count={usage.auto.count}
              countSuffix={text.sessions}
              sizeBytes={usage.auto.sizeBytes}
              totalBytes={totalBytes}
              note={text.autoPolicy}
            />
            <UsageCard
              label={text.channel}
              count={usage.channel.count}
              countSuffix={text.sessions}
              sizeBytes={usage.channel.sizeBytes}
              totalBytes={totalBytes}
              note={text.channelNote}
            />
          </div>
          <div className="storage-total-row">
            <span className="storage-total-row__label">{text.total}</span>
            <span className="storage-total-row__size">{formatBytes(usage.totalSizeBytes)}</span>
          </div>
        </>
      )}

      {/* ── Session list ── */}
      {sessions.length > 0 && (
        <>
          <div className="storage-list-header">
            <h3 className="storage-list-header__title">{text.historyTitle}</h3>

            {deletableSessions.length > 0 && (
              <label className="storage-list-header__select-all">
                <input
                  type="checkbox"
                  checked={allVisibleSelected}
                  onChange={toggleSelectAll}
                />
                {allVisibleSelected ? text.deselectAll : text.selectAll}
              </label>
            )}

            {selectedIds.size > 0 && (
              <button
                className="storage-batch-btn"
                onClick={() => setBatchConfirmOpen(true)}
              >
                <Trash2 size={12} strokeWidth={2} />
                {text.batchDeleteBtn(selectedIds.size)}
              </button>
            )}
          </div>
          <p className="settings-section-desc" style={{ marginTop: 0, marginBottom: 8 }}>{text.historyDesc}</p>

          <div className="storage-session-list">
            {sessions.slice(0, visibleCount).map(s => {
              const isActive = s.sessionId === activeSessionId;
              const isSelected = selectedIds.has(s.sessionId);
              const label = s.title?.trim() || s.sessionId;
              const isTruncatedId = !s.title?.trim();

              return (
                <div
                  key={s.sessionId}
                  className={[
                    'storage-session-row',
                    isSelected ? 'storage-session-row--selected' : '',
                    isActive   ? 'storage-session-row--active'   : '',
                  ].filter(Boolean).join(' ')}
                >
                  {isActive
                    ? <span className="storage-session-row__checkbox-placeholder" />
                    : (
                      <input
                        type="checkbox"
                        className="storage-session-row__checkbox"
                        checked={isSelected}
                        onChange={() => toggleSelect(s.sessionId)}
                      />
                    )
                  }

                  <div className="storage-session-row__body">
                    <div className={`storage-session-row__title${isTruncatedId ? ' storage-session-row__title--mono' : ''}`}>
                      {isTruncatedId ? `${label.slice(0, 32)}…` : label}
                    </div>
                    <div className="storage-session-row__meta">
                      <span className="storage-session-row__meta-item">
                        <MessageSquare size={10} strokeWidth={2} />
                        {s.status.messageCount} {text.msgCount}
                      </span>
                      <span className="storage-session-row__meta-dot" />
                      <span className="storage-session-row__meta-item">
                        {text.lastUsed} {formatDate(s.lastEventAt ?? s.createdAt)}
                      </span>
                    </div>
                  </div>

                  {isActive
                    ? <span className="storage-active-badge">{text.active}</span>
                    : (
                      <div className="storage-session-row__actions">
                        <button
                          className="storage-session-row__delete-btn"
                          onClick={() => setPendingDelete(s)}
                          aria-label={`${text.deleteBtn} ${s.sessionId}`}
                        >
                          <Trash2 size={11} strokeWidth={2} />
                          {text.deleteBtn}
                        </button>
                      </div>
                    )
                  }
                </div>
              );
            })}
          </div>

          {visibleCount < sessions.length && (
            <button
              className="storage-load-more"
              onClick={() => setVisibleCount(c => c + SESSION_PAGE)}
            >
              {text.showMore(Math.min(SESSION_PAGE, sessions.length - visibleCount))}
            </button>
          )}
        </>
      )}

      <ConfirmModal
        open={pendingDelete !== null}
        title={text.deleteModalTitle}
        description={pendingDelete
          ? `${text.deleteModalDesc}\n\n${pendingDelete.title?.trim() || pendingDelete.sessionId}`
          : ''}
        confirmLabel={isDeleting ? '…' : text.confirm}
        cancelLabel={text.close}
        variant="danger"
        busy={isDeleting}
        onConfirm={() => void handleDelete()}
        onCancel={() => setPendingDelete(null)}
      />

      <ConfirmModal
        open={batchConfirmOpen}
        title={text.batchDeleteTitle}
        description={text.batchDeleteDesc(selectedIds.size)}
        confirmLabel={isDeleting ? '…' : text.confirm}
        cancelLabel={text.close}
        variant="danger"
        busy={isDeleting}
        onConfirm={() => void handleBatchDelete()}
        onCancel={() => setBatchConfirmOpen(false)}
      />
    </div>
  );
}

/* ── Usage Card ── */
function UsageCard({
  label, count, countSuffix, sizeBytes, totalBytes, note, accent,
}: {
  label: string;
  count: number;
  countSuffix: string;
  sizeBytes: number;
  totalBytes: number;
  note?: string;
  accent?: boolean;
}) {
  const pct = totalBytes > 0 ? Math.max((sizeBytes / totalBytes) * 100, sizeBytes > 0 ? 4 : 0) : 0;
  return (
    <div className="storage-card">
      <div className="storage-card__header">
        <span className="storage-card__label">{label}</span>
        <span className="storage-card__count">
          {count}{countSuffix ? ` ${countSuffix}` : ''}
        </span>
      </div>
      <div className="storage-card__bar-track">
        <div
          className={`storage-card__bar-fill${accent ? '' : ' storage-card__bar-fill--muted'}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="storage-card__size">{formatBytes(sizeBytes)}</span>
      {note && <span className="storage-card__note">{note}</span>}
    </div>
  );
}
