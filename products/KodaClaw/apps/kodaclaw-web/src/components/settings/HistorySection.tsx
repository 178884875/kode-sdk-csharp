import { useCallback, useEffect, useState } from 'react';
import { History, ChevronDown, ChevronRight, RotateCcw, AlertCircle } from 'lucide-react';
import { fetchWorkspaceGitLog, fetchWorkspaceGitDiff, revertWorkspaceFile } from '../../lib/api';
import { useLocaleText } from '../../i18n/I18nProvider';
import type { WorkspaceGitCommit } from '../../types/contracts';

function formatCommittedAt(iso: string): string {
  try {
    return new Date(iso).toLocaleString(undefined, {
      month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
    });
  } catch {
    return iso;
  }
}

function parseSource(message: string): 'agent' | 'user' | 'system' | 'unknown' {
  if (message.includes('[agent]')) return 'agent';
  if (message.includes('[user/')) return 'user';
  if (message.includes('[system/')) return 'system';
  return 'unknown';
}

function SourceBadge({ message }: { message: string }) {
  const source = parseSource(message);
  const styles: Record<string, string> = {
    agent: 'background:var(--color-amber-100);color:var(--color-amber-800)',
    user: 'background:var(--color-neutral-100);color:var(--color-neutral-700)',
    system: 'background:var(--color-neutral-50);color:var(--color-neutral-500)',
    unknown: 'background:var(--color-neutral-50);color:var(--color-neutral-400)',
  };
  const labels: Record<string, string> = {
    agent: 'Agent', user: 'User', system: 'System', unknown: '?',
  };
  return (
    <span style={{
      display: 'inline-block',
      padding: '1px 6px',
      borderRadius: 4,
      fontSize: 11,
      fontWeight: 600,
      ...Object.fromEntries(styles[source].split(';').map(s => {
        const [k, v] = s.split(':');
        return [k.trim().replace(/-([a-z])/g, (_: string, c: string) => c.toUpperCase()), v?.trim()];
      })),
    }}>
      {labels[source]}
    </span>
  );
}

function CommitRow({
  commit, onRevert,
}: {
  commit: WorkspaceGitCommit;
  onRevert: (hash: string, filePath: string) => Promise<void>;
}) {
  const [expanded, setExpanded] = useState(false);
  const [diff, setDiff] = useState<string | null>(null);
  const [diffLoading, setDiffLoading] = useState(false);
  const [revertingFile, setRevertingFile] = useState<string | null>(null);

  const handleExpand = useCallback(async () => {
    if (!expanded && diff === null) {
      setDiffLoading(true);
      try {
        const d = await fetchWorkspaceGitDiff(commit.hash);
        setDiff(d);
      } catch {
        setDiff('');
      } finally {
        setDiffLoading(false);
      }
    }
    setExpanded(e => !e);
  }, [expanded, diff, commit.hash]);

  const handleRevert = useCallback(async (filePath: string) => {
    setRevertingFile(filePath);
    try {
      await onRevert(commit.hash, filePath);
    } finally {
      setRevertingFile(null);
    }
  }, [commit.hash, onRevert]);

  return (
    <div style={{ borderBottom: '1px solid var(--color-border)', paddingBottom: 8, marginBottom: 8 }}>
      <div
        onClick={handleExpand}
        style={{
          display: 'flex', alignItems: 'flex-start', gap: 8,
          cursor: 'pointer', padding: '4px 0',
        }}
      >
        <span style={{ color: 'var(--color-text-secondary)', marginTop: 2 }}>
          {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        </span>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
            <SourceBadge message={commit.message} />
            <span style={{ fontSize: 13, color: 'var(--color-text-primary)', wordBreak: 'break-word' }}>
              {commit.message}
            </span>
          </div>
          <div style={{ fontSize: 11, color: 'var(--color-text-secondary)', marginTop: 2 }}>
            {commit.shortHash} · {formatCommittedAt(commit.committedAt)}
          </div>
        </div>
      </div>

      {expanded && (
        <div style={{ paddingLeft: 22, marginTop: 4 }}>
          {commit.changedFiles.map(f => (
            <div key={f} style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 4 }}>
              <span style={{ fontSize: 12, color: 'var(--color-text-secondary)', flex: 1 }}>{f}</span>
              {f.startsWith('workspace/') && (
                <button
                  onClick={() => handleRevert(f)}
                  disabled={revertingFile === f}
                  style={{
                    display: 'flex', alignItems: 'center', gap: 4,
                    fontSize: 11, padding: '2px 8px',
                    borderRadius: 4, border: '1px solid var(--color-border)',
                    background: 'transparent', cursor: 'pointer',
                    color: 'var(--color-text-secondary)',
                    opacity: revertingFile === f ? 0.5 : 1,
                  }}
                >
                  <RotateCcw size={10} />
                  回滚此文件
                </button>
              )}
            </div>
          ))}

          {diffLoading && (
            <div style={{ fontSize: 12, color: 'var(--color-text-secondary)', padding: '4px 0' }}>
              加载 diff…
            </div>
          )}
          {!diffLoading && diff !== null && diff.length > 0 && (
            <pre style={{
              fontSize: 11, lineHeight: 1.5,
              background: 'var(--color-surface-raised)',
              border: '1px solid var(--color-border)',
              borderRadius: 4, padding: 8, overflowX: 'auto',
              whiteSpace: 'pre-wrap', wordBreak: 'break-all',
              maxHeight: 320, overflowY: 'auto',
              color: 'var(--color-text-secondary)',
              marginTop: 8,
            }}>
              {diff}
            </pre>
          )}
        </div>
      )}
    </div>
  );
}

export function HistorySection() {
  const text = useLocaleText({
    zh: {
      sectionTitle: '变更历史',
      sectionDesc: 'Workspace 文件的所有修改记录，包括 Agent 写入和手动编辑。',
      privacyNote: 'Workspace 历史记录了 Agent 和你对工作区文件的所有修改。编辑文件后旧内容仍存在于历史中——如需完全清除，请使用"系统"分区的重置功能。',
      noHistory: '暂无变更历史。向 Koda 发送一条消息后此处将出现记录。',
      revertSuccess: '已回滚至该版本。',
      revertFailed: '回滚失败，请稍后重试。',
    },
    en: {
      sectionTitle: 'Change History',
      sectionDesc: 'All modifications to workspace files, by Agent or manual edits.',
      privacyNote: 'Workspace history records every change to your workspace files. Edited content remains in history even after deletion — use the System section to fully reset history.',
      noHistory: 'No history yet. Send a message to Koda to start recording changes.',
      revertSuccess: 'File reverted successfully.',
      revertFailed: 'Revert failed. Please try again.',
    },
  });

  const [commits, setCommits] = useState<WorkspaceGitCommit[]>([]);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await fetchWorkspaceGitLog(50);
      setCommits(res.commits);
    } catch {
      setCommits([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const handleRevert = useCallback(async (hash: string, filePath: string) => {
    try {
      await revertWorkspaceFile({ hash, filePath });
      setNotice(text.revertSuccess);
      await load();
    } catch {
      setNotice(text.revertFailed);
    }
    setTimeout(() => setNotice(null), 3000);
  }, [load, text.revertFailed, text.revertSuccess]);

  return (
    <section>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
        <History size={16} />
        <h3 style={{ margin: 0, fontSize: 15, fontWeight: 600 }}>{text.sectionTitle}</h3>
      </div>
      <p style={{ fontSize: 13, color: 'var(--color-text-secondary)', marginBottom: 12 }}>
        {text.sectionDesc}
      </p>

      <div style={{
        display: 'flex', alignItems: 'flex-start', gap: 6,
        background: 'var(--color-surface-raised)', borderRadius: 6,
        padding: '8px 10px', marginBottom: 16, fontSize: 12,
        color: 'var(--color-text-secondary)',
        border: '1px solid var(--color-border)',
      }}>
        <AlertCircle size={13} style={{ flexShrink: 0, marginTop: 1 }} />
        <span>{text.privacyNote}</span>
      </div>

      {notice && (
        <div style={{
          padding: '6px 10px', borderRadius: 6, marginBottom: 12,
          background: 'var(--color-amber-50)', border: '1px solid var(--color-amber-200)',
          fontSize: 13, color: 'var(--color-amber-800)',
        }}>
          {notice}
        </div>
      )}

      {loading && (
        <div style={{ fontSize: 13, color: 'var(--color-text-secondary)' }}>加载中…</div>
      )}

      {!loading && commits.length === 0 && (
        <div style={{ fontSize: 13, color: 'var(--color-text-secondary)' }}>{text.noHistory}</div>
      )}

      {!loading && commits.map(c => (
        <CommitRow key={c.hash} commit={c} onRevert={handleRevert} />
      ))}
    </section>
  );
}
