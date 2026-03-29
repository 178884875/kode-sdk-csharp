import { useCallback, useEffect, useRef, useState } from 'react';
import { ChevronDown, ChevronRight, Trash2, WifiOff, Workflow, X } from 'lucide-react';
import {
  clearDiagnostics,
  fetchDiagnosticsRecent,
  fetchDiagnosticsStats,
  openDiagnosticsStream,
} from '../lib/api';
import { useLocaleText } from '../i18n/I18nProvider';
import { markDiagAsSeen } from '../lib/diagLastSeen';
import type { DiagnosticEvent, DiagnosticsStatsResponse } from '../types/contracts';
import './DiagnosticsDesk.css';

// ── Helpers ──────────────────────────────────────────────────────────────────

function formatTs(iso: string): string {
  try {
    return new Date(iso).toLocaleTimeString(undefined, {
      hour: '2-digit', minute: '2-digit', second: '2-digit',
    });
  } catch {
    return iso;
  }
}

function LevelBadge({ level }: { level: string }) {
  const lower = level.toLowerCase();
  const cls = lower === 'error' || lower === 'warning' || lower === 'debug'
    ? `kc-diag-badge kc-diag-badge--${lower}`
    : 'kc-diag-badge kc-diag-badge--info';
  return <span className={cls}>{lower}</span>;
}

type EventRowProps = {
  event: DiagnosticEvent;
  onTrack?: (correlationId: string) => void;
};

function EventRow({ event, onTrack }: EventRowProps) {
  const [expanded, setExpanded] = useState(false);
  const hasDetail = (event.attributes && Object.keys(event.attributes).length > 0)
    || !!event.sessionId
    || !!event.eventType
    || !!event.correlationId;

  return (
    <div className="kc-diag-row">
      <div
        className={`kc-diag-row__main${hasDetail ? ' kc-diag-row--clickable kc-diag-row__main' : ''}`}
        onClick={() => hasDetail && setExpanded(e => !e)}
      >
        <span className="kc-diag-row__time">{formatTs(event.timestamp)}</span>
        <span className="kc-diag-row__source">{event.source}</span>
        <span className="kc-diag-row__message">{event.message}</span>
        <div className="kc-diag-row__end">
          <LevelBadge level={event.level} />
          {hasDetail && (
            <span className="kc-diag-row__chevron">
              {expanded ? <ChevronDown size={11} /> : <ChevronRight size={11} />}
            </span>
          )}
        </div>
      </div>

      {expanded && (
        <div className="kc-diag-row__detail">
          {event.correlationId && (
            <div className="kc-diag-row__detail-line">
              <span className="kc-diag-row__detail-key">correlationId</span>
              <span className="kc-diag-row__detail-corr">{event.correlationId}</span>
              {onTrack && (
                <button
                  type="button"
                  className="kc-diag-row__track-btn"
                  title="追踪此 CorrelationId"
                  onClick={(ev) => { ev.stopPropagation(); onTrack(event.correlationId!); }}
                >
                  <Workflow size={12} />
                </button>
              )}
            </div>
          )}
          {event.sessionId && (
            <div className="kc-diag-row__detail-line">
              <span className="kc-diag-row__detail-key">session</span>{event.sessionId}
            </div>
          )}
          {event.eventType && (
            <div className="kc-diag-row__detail-line">
              <span className="kc-diag-row__detail-key">eventType</span>{event.eventType}
            </div>
          )}
          {event.attributes && Object.entries(event.attributes).map(([k, v]) => (
            <div key={k} className="kc-diag-row__detail-line">
              <span className="kc-diag-row__detail-key">{k}</span>{v ?? 'null'}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Main Component ────────────────────────────────────────────────────────────

export function DiagnosticsDesk() {
  const text = useLocaleText({
    zh: {
      allLevels: '全部级别',
      allSources: '全部来源',
      searchPlaceholder: '搜索 message 或 source…',
      clear7d: '清理 7 天前',
      clear1d: '清理 1 天前',
      clearAll: '全部清理',
      live: '实时',
      offline: '已断开',
      noEvents: '暂无事件。向 Koda 发送一条消息后此处将出现记录。',
      loading: '加载中…',
      errorsLabel: '错误',
      warningsLabel: '警告',
      totalLabel: '事件',
    },
    en: {
      allLevels: 'All Levels',
      allSources: 'All Sources',
      searchPlaceholder: 'Search message or source…',
      clear7d: 'Clear older than 7 days',
      clear1d: 'Clear older than 1 day',
      clearAll: 'Clear all',
      live: 'Live',
      offline: 'Disconnected',
      noEvents: 'No events yet. Send a message to Koda to start recording.',
      loading: 'Loading…',
      errorsLabel: 'Errors',
      warningsLabel: 'Warnings',
      totalLabel: 'Events',
    },
  });

  const [events, setEvents] = useState<DiagnosticEvent[]>([]);
  const [stats, setStats] = useState<DiagnosticsStatsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [live, setLive] = useState(false);
  const [filterLevel, setFilterLevel] = useState<string[]>(['warning', 'error']);
  const [filterSource, setFilterSource] = useState('');
  const [search, setSearch] = useState('');
  const [activeCorrelationId, setActiveCorrelationId] = useState('');
  const [clearMenuOpen, setClearMenuOpen] = useState(false);
  const streamAbortRef = useRef<AbortController | null>(null);

  const loadInitial = useCallback(async () => {
    setLoading(true);
    try {
      const [res, statsRes] = await Promise.all([
        fetchDiagnosticsRecent({ limit: 200 }),
        fetchDiagnosticsStats(),
      ]);
      setEvents(res.events);
      setStats(statsRes);
    } catch {
      // ignore
    } finally {
      setLoading(false);
    }
  }, []);

  // SSE 实时流
  useEffect(() => {
    const abort = new AbortController();
    streamAbortRef.current = abort;
    setLive(false);

    openDiagnosticsStream((evt) => {
      setLive(true);
      setEvents(prev => [evt, ...prev].slice(0, 500));
      setStats(prev => prev ? {
        ...prev,
        totalEvents: prev.totalEvents + 1,
        errorCount: evt.level === 'error' ? prev.errorCount + 1 : prev.errorCount,
        warningCount: evt.level === 'warning' ? prev.warningCount + 1 : prev.warningCount,
      } : prev);
    }, abort.signal);

    const liveTimer = setTimeout(() => setLive(true), 1000);
    return () => {
      abort.abort();
      clearTimeout(liveTimer);
      setLive(false);
    };
  }, []);

  useEffect(() => { void loadInitial(); }, [loadInitial]);

  // 进入面板即标记已读，清除 Sidebar 徽标
  useEffect(() => { markDiagAsSeen(); }, []);

  const handleClear = useCallback(async (mode: 'all' | '1d' | '7d') => {
    setClearMenuOpen(false);
    let before: string | undefined;
    if (mode === '7d') before = new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString();
    else if (mode === '1d') before = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
    try {
      await clearDiagnostics(before);
      await loadInitial();
    } catch {
      // ignore
    }
  }, [loadInitial]);

  const allSources = Array.from(new Set(events.map(e => e.source))).sort();

  const hasFilter = filterLevel.length > 0 || filterSource || search || activeCorrelationId;

  const filtered = events.filter(e => {
    if (filterLevel.length > 0 && !filterLevel.includes(e.level.toLowerCase())) return false;
    if (filterSource && e.source !== filterSource) return false;
    if (activeCorrelationId && e.correlationId !== activeCorrelationId) return false;
    if (search) {
      const q = search.toLowerCase();
      if (!e.message.toLowerCase().includes(q) && !e.source.toLowerCase().includes(q)) return false;
    }
    return true;
  });

  // When any filter is active, compute stats from filtered array; otherwise use API stats.
  const displayStats: DiagnosticsStatsResponse | null = hasFilter
    ? stats
      ? {
          ...stats,
          totalEvents: filtered.length,
          errorCount: filtered.filter(e => e.level === 'error').length,
          warningCount: filtered.filter(e => e.level === 'warning').length,
        }
      : null
    : stats;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>

      {/* Toolbar: stats + live + filters + clear — one unified row */}
      <div className="kc-diag-toolbar">

        {/* Stats */}
        {displayStats && (
          <div className="kc-diag-stats">
            <div className="kc-diag-stat">
              <span className="kc-diag-stat__value">{displayStats.totalEvents}</span>
              <span className="kc-diag-stat__label">{text.totalLabel}</span>
            </div>
            {displayStats.errorCount > 0 && (
              <div className="kc-diag-stat">
                <span className="kc-diag-stat__value kc-diag-stat__value--error">{displayStats.errorCount}</span>
                <span className="kc-diag-stat__label">{text.errorsLabel}</span>
              </div>
            )}
            {displayStats.warningCount > 0 && (
              <div className="kc-diag-stat">
                <span className="kc-diag-stat__value kc-diag-stat__value--warning">{displayStats.warningCount}</span>
                <span className="kc-diag-stat__label">{text.warningsLabel}</span>
              </div>
            )}
          </div>
        )}

        {/* Live indicator */}
        <span className={`kc-diag-live${live ? ' kc-diag-live--on' : ' kc-diag-live--off'}`}>
          {live ? (
            <span className="kc-diag-live__dot" />
          ) : (
            <WifiOff size={9} />
          )}
          {live ? text.live : text.offline}
        </span>

        <div className="kc-diag-toolbar__sep" />

        {/* CorrelationId chip */}
        {activeCorrelationId && (
          <div className="kc-diag-chip">
            <Workflow size={11} />
            <span className="kc-diag-chip__id">{activeCorrelationId.slice(0, 12)}…</span>
            <button
              type="button"
              className="kc-diag-chip__close"
              title="清除追踪"
              onClick={() => setActiveCorrelationId('')}
            >
              <X size={10} />
            </button>
          </div>
        )}

        {/* Level filter */}
        <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
          {(['debug', 'info', 'warning', 'error'] as const).map(l => {
            const active = filterLevel.includes(l);
            const c: Record<string, [string,string,string]> = { debug: ['#f3f4f6','#374151','#d1d5db'], info: ['#dbeafe','#1e40af','#93c5fd'], warning: ['#fef3c7','#92400e','#fcd34d'], error: ['#fee2e2','#991b1b','#fca5a5'] };
            const [bg, text, border] = active ? c[l] : ['transparent', '#9ca3af', '#e5e7eb'];
            return (
              <span key={l} onClick={() => setFilterLevel(prev => prev.includes(l) ? prev.filter(x => x !== l) : [...prev, l])}
                style={{ padding: '4px 12px', borderRadius: '14px', fontSize: '12px', fontWeight: 500, cursor: 'pointer', userSelect: 'none', border: '1px solid ' + border, backgroundColor: bg, color: text, transition: 'all 0.15s ease' }}>
                {l}
              </span>
            );
          })}
        </div>

        {/* Source filter */}
        <select
          className="kc-diag-select"
          value={filterSource}
          onChange={e => setFilterSource(e.target.value)}
        >
          <option value="">{text.allSources}</option>
          {allSources.map(s => <option key={s} value={s}>{s}</option>)}
        </select>

        {/* Search */}
        <input
          className="kc-diag-search"
          type="text"
          placeholder={text.searchPlaceholder}
          value={search}
          onChange={e => setSearch(e.target.value)}
        />

        {/* Count */}
        <span className="kc-diag-count">{filtered.length} / {events.length}</span>

        {/* Clear dropdown */}
        <div style={{ position: 'relative' }}>
          <button
            className="kc-diag-clear-btn"
            onClick={() => setClearMenuOpen(o => !o)}
            title="Clear events"
          >
            <Trash2 size={12} />
            <ChevronDown size={10} />
          </button>
          {clearMenuOpen && (
            <>
              <div style={{ position: 'fixed', inset: 0, zIndex: 10 }} onClick={() => setClearMenuOpen(false)} />
              <div className="kc-diag-clear-menu">
                {[
                  { label: text.clear7d, mode: '7d' as const },
                  { label: text.clear1d, mode: '1d' as const },
                  { label: text.clearAll, mode: 'all' as const },
                ].map(item => (
                  <button
                    key={item.mode}
                    className={`kc-diag-clear-item${item.mode === 'all' ? ' kc-diag-clear-item--danger' : ''}`}
                    onClick={() => void handleClear(item.mode)}
                  >
                    {item.label}
                  </button>
                ))}
              </div>
            </>
          )}
        </div>
      </div>

      {/* Event list */}
      <div className="kc-diag-list">
        {loading && (
          <div className="kc-diag-empty">{text.loading}</div>
        )}
        {!loading && filtered.length === 0 && (
          <div className="kc-diag-empty">{text.noEvents}</div>
        )}
        {!loading && filtered.map(e => (
          <EventRow key={e.id} event={e} onTrack={setActiveCorrelationId} />
        ))}
      </div>
    </div>
  );
}
