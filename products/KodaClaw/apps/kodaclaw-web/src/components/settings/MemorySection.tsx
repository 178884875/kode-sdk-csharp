import { useState, useEffect, useRef } from 'react';
import { fetchWorkspaceFile, updateWorkspaceFile } from '../../lib/api';
import { Skeleton } from '../ui/Skeleton';
import { useLocaleText } from '../../i18n/I18nProvider';

export function MemorySection() {
  const [content, setContent] = useState('');
  const [saved, setSaved] = useState('');
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  const text = useLocaleText({
    zh: {
      sectionTitle: '长期记忆',
      sectionDesc: 'MEMORY.md 是 Koda 跨会话记忆的索引，Agent 会自动维护。你也可以在此手动整理记忆条目。',
      fileHint: '跨会话长期记忆索引',
      saved: '已保存',
      saving: '保存中…',
      save: '保存',
    },
    en: {
      sectionTitle: 'Long-term Memory',
      sectionDesc: 'MEMORY.md is the index for Koda\'s cross-session memory. The agent maintains it automatically, but you can also edit entries here.',
      fileHint: 'Cross-session memory index',
      saved: 'Saved',
      saving: 'Saving…',
      save: 'Save',
    },
  });

  useEffect(() => {
    const ac = new AbortController();
    abortRef.current = ac;
    setLoading(true);
    fetchWorkspaceFile('memory', ac.signal)
      .then(r => {
        const t = r.content ?? '';
        setContent(t);
        setSaved(t);
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

  const save = async () => {
    setSaving(true);
    setError(null);
    setSuccess(false);
    try {
      await updateWorkspaceFile('memory', content);
      setSaved(content);
      setSaving(false);
      setSuccess(true);
      setTimeout(() => setSuccess(false), 3000);
    } catch (e) {
      setSaving(false);
      setError(String(e));
    }
  };

  const isDirty = content !== saved;

  return (
    <div className="settings-section" data-testid="settings-memory-section">
      <h2 className="settings-section-title">{text.sectionTitle}</h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

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
            className="settings-btn settings-btn--primary"
            disabled={!isDirty || saving || loading}
            onClick={save}
          >
            {saving ? text.saving : text.save}
          </button>
        </div>
      </div>
    </div>
  );
}
