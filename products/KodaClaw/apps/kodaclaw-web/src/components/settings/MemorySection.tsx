import './MemorySection.css';
import { useState, useEffect, useCallback } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../lib/queryKeys';
import { Brain, ArrowUpCircle } from 'lucide-react';
import { fetchWorkspaceFile, updateWorkspaceFile, fetchMemoryStats, fetchMemoryEntries, promoteMemoryEntry } from '../../lib/api';
import type { MemoryStats, MemoryEntryItem } from '../../lib/api';
import { Skeleton } from '../ui/Skeleton';
import { EmptyState } from '../ui/EmptyState';
import { useLocaleText } from '../../i18n/I18nProvider';

const FILTER_KEYS = ['all', 'active', 'dormant', 'archived'] as const;
type FilterKey = (typeof FILTER_KEYS)[number];

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleString(undefined, {
      month: 'short', day: 'numeric',
    });
  } catch {
    return iso;
  }
}

export function MemorySection() {
  const queryClient = useQueryClient();
  const { data: memoryFileData, isLoading: loading, error: memoryFileError } = useQuery({
    queryKey: queryKeys.workspaceFile('memory'),
    queryFn: () => fetchWorkspaceFile('memory'),
  });
  const { data: statsData } = useQuery({
    queryKey: queryKeys.memoryStats,
    queryFn: () => fetchMemoryStats(),
  });
  const stats: MemoryStats | null = statsData ?? null;
  const [content, setContent] = useState('');
  const [saved, setSaved] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(memoryFileError instanceof Error ? memoryFileError.message : null);
  const [success, setSuccess] = useState(false);
  const [entries, setEntries] = useState<MemoryEntryItem[]>([]);
  const [entriesFilter, setEntriesFilter] = useState<FilterKey>('active');
  const [entriesLoading, setEntriesLoading] = useState(false);
  const [promoting, setPromoting] = useState<string | null>(null);

  const text = useLocaleText({
    zh: {
      sectionTitle: '长期记忆',
      sectionDesc: 'MEMORY.md 是 Koda 跨会话记忆的索引，Agent 会自动维护。你也可以在此手动整理记忆条目。',
      fileHint: '跨会话长期记忆索引',
      saved: '已保存',
      saving: '保存中…',
      save: '保存',
      active: '活跃',
      dormant: '温存',
      archived: '归档',
      topics: '主题',
      sessions: '摘要',
      entriesTitle: '记忆条目',
      filterAll: '全部',
      filterActive: '活跃',
      filterDormant: '温存',
      filterArchived: '归档',
      promote: '激活',
      promoting: '激活中…',
      noEntries: '暂无记忆条目',
      noEntriesDesc: 'Agent 会在会话中自动记录重要信息。',
    },
    en: {
      sectionTitle: 'Long-term Memory',
      sectionDesc: 'MEMORY.md is the index for Koda\'s cross-session memory. The agent maintains it automatically, but you can also edit entries here.',
      fileHint: 'Cross-session memory index',
      saved: 'Saved',
      saving: 'Saving…',
      save: 'Save',
      active: 'Active',
      dormant: 'Dormant',
      archived: 'Archived',
      topics: 'Topics',
      sessions: 'Summaries',
      entriesTitle: 'Memory Entries',
      filterAll: 'All',
      filterActive: 'Active',
      filterDormant: 'Dormant',
      filterArchived: 'Archived',
      promote: 'Promote',
      promoting: 'Promoting…',
      noEntries: 'No memory entries yet',
      noEntriesDesc: 'The agent records important information automatically during sessions.',
    },
  });

  const filterLabels: Record<FilterKey, string> = {
    all: text.filterAll,
    active: text.filterActive,
    dormant: text.filterDormant,
    archived: text.filterArchived,
  };

  useEffect(() => {
    if (memoryFileData && !content) {
      const t = memoryFileData.content ?? '';
      setContent(t);
      setSaved(t);
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [memoryFileData]);

  const loadEntries = useCallback(async (status: FilterKey) => {
    setEntriesLoading(true);
    try {
      const resp = await fetchMemoryEntries(status === 'all' ? undefined : status, 50);
      setEntries(resp.entries);
    } catch {
      setEntries([]);
    }
    setEntriesLoading(false);
  }, []);

  useEffect(() => {
    loadEntries(entriesFilter);
  }, [entriesFilter, loadEntries]);

  const save = async () => {
    setSaving(true);
    setError(null);
    setSuccess(false);
    try {
      await updateWorkspaceFile('memory', content);
      void queryClient.invalidateQueries({ queryKey: queryKeys.workspaceFile('memory') });
      setSaved(content);
      setSaving(false);
      setSuccess(true);
      setTimeout(() => setSuccess(false), 3000);
    } catch (e) {
      setSaving(false);
      setError(String(e));
    }
  };

  const handlePromote = async (key: string) => {
    setPromoting(key);
    try {
      await promoteMemoryEntry(key);
      await loadEntries(entriesFilter);
      void queryClient.invalidateQueries({ queryKey: queryKeys.memoryStats });
    } catch {
      // silently fail
    }
    setPromoting(null);
  };

  const isDirty = content !== saved;

  return (
    <div className="settings-section" data-testid="settings-memory-section">
      <h2 className="settings-section-title">{text.sectionTitle}</h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

      {/* Stats cards */}
      {stats && (
        <div className="memory-stats-grid" data-testid="memory-stats">
          <StatCard value={stats.activeCount} label={text.active} />
          <StatCard value={stats.dormantCount} label={text.dormant} />
          <StatCard value={stats.archivedCount} label={text.archived} />
          <StatCard value={stats.topicsCount} label={text.topics} />
          <StatCard value={stats.sessionsCount} label={text.sessions} />
        </div>
      )}

      {/* MEMORY.md editor */}
      <div className="settings-file-editor" data-testid="settings-memory-editor">
        <div className="settings-file-header">
          <label className="settings-file-label">MEMORY.md</label>
          <span className="settings-file-hint">{text.fileHint}</span>
        </div>

        {loading ? (
          <Skeleton count={4} height={20} />
        ) : (
          <textarea
            className="settings-file-textarea"
            value={content}
            onChange={e => setContent(e.target.value)}
            rows={10}
            spellCheck={false}
          />
        )}

        <div className="settings-file-actions">
          {error && <span className="settings-file-error">{error}</span>}
          {success && <span className="settings-file-success">{text.saved}</span>}
          <button
            type="button"
            className="btn btn--primary"
            disabled={!isDirty || saving || loading}
            onClick={save}
          >
            {saving ? text.saving : text.save}
          </button>
        </div>
      </div>

      {/* Entries list */}
      <div className="memory-entries-section" data-testid="memory-entries">
        <div className="memory-entries-header">
          <h3 className="memory-entries-header__title">{text.entriesTitle}</h3>
        </div>

        <div className="memory-filter-tabs">
          {FILTER_KEYS.map(f => (
            <button
              key={f}
              type="button"
              className={`memory-filter-tab ${entriesFilter === f ? 'memory-filter-tab--active' : ''}`}
              onClick={() => setEntriesFilter(f)}
            >
              {filterLabels[f]}
            </button>
          ))}
        </div>

        {entriesLoading ? (
          <Skeleton count={3} height={40} />
        ) : entries.length === 0 ? (
          <EmptyState
            icon={<Brain size={28} />}
            title={text.noEntries}
            description={text.noEntriesDesc}
          />
        ) : (
          <div className="memory-entries-list">
            {entries.map(entry => (
              <div key={entry.key} className="memory-entry-row">
                <span className={badgeClass(entry.status)}>{entry.status}</span>
                <div className="memory-entry-body">
                  <div className="memory-entry-title" title={entry.key}>{entry.title}</div>
                  <div className="memory-entry-meta">
                    <span className="memory-entry-meta__item">{entry.priority}</span>
                    <span className="memory-entry-meta__dot" />
                    <span className="memory-entry-meta__item">{entry.created ? formatDate(entry.created) : '—'}</span>
                  </div>
                </div>
                {entry.status !== 'active' && (
                  <div className="memory-entry-actions">
                    <button
                      type="button"
                      className="memory-entry-promote-btn"
                      disabled={promoting === entry.key}
                      onClick={() => handlePromote(entry.key)}
                    >
                      <ArrowUpCircle size={13} />
                      {promoting === entry.key ? text.promoting : text.promote}
                    </button>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function StatCard({ value, label }: { value: number; label: string }) {
  return (
    <div className="memory-stat-card">
      <span className="memory-stat-value">{value}</span>
      <span className="memory-stat-label">{label}</span>
    </div>
  );
}

function badgeClass(status: string): string {
  return status === 'active' ? 'memory-badge memory-badge--active'
    : status === 'dormant' ? 'memory-badge memory-badge--dormant'
    : 'memory-badge memory-badge--archived';
}
