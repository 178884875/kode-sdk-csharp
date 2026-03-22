import { useState, useEffect, useRef } from 'react';
import { fetchWorkspaceFile } from '../../lib/api';
import { Skeleton } from '../ui/Skeleton';
import { useLocaleText } from '../../i18n/I18nProvider';

export function HeartbeatSection() {
  const [content, setContent] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const text = useLocaleText({
    zh: {
      sectionTitle: '自动化规则预览',
      sectionDesc: 'HEARTBEAT.md 由 Koda 在对话中自动更新，定义了定时任务和触发规则。此处仅供查看，修改请在对话中指示 Koda。',
      fileHint: '自动化规则（只读）',
      ariaLabel: 'HEARTBEAT.md 只读预览',
    },
    en: {
      sectionTitle: 'Automation Rules Preview',
      sectionDesc: 'HEARTBEAT.md is updated automatically by Koda during conversations. It defines scheduled tasks and trigger rules. This view is read-only — ask Koda in chat to make changes.',
      fileHint: 'Automation rules (read-only)',
      ariaLabel: 'HEARTBEAT.md read-only preview',
    },
  });

  useEffect(() => {
    const ac = new AbortController();
    abortRef.current = ac;
    setLoading(true);
    fetchWorkspaceFile('heartbeat', ac.signal)
      .then(r => {
        setContent(r.content ?? '');
        setLoading(false);
      })
      .catch(e => {
        if (!ac.signal.aborted) {
          setError(String(e));
          setLoading(false);
        }
      });
    return () => ac.abort();
  }, []);

  return (
    <div className="settings-section" data-testid="settings-heartbeat-section">
      <h2 className="settings-section-title">{text.sectionTitle}</h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

      <div className="settings-file-editor" data-testid="settings-heartbeat-editor">
        <div className="settings-file-header">
          <label className="settings-file-label">HEARTBEAT.md</label>
          <span className="settings-file-hint">{text.fileHint}</span>
        </div>

        {loading ? (
          <Skeleton count={3} height={20} />
        ) : error ? (
          <div className="settings-file-error" style={{ padding: '8px 0' }}>{error}</div>
        ) : (
          <textarea
            className="settings-file-textarea"
            value={content}
            readOnly
            rows={8}
            spellCheck={false}
            aria-label={text.ariaLabel}
          />
        )}
      </div>
    </div>
  );
}
