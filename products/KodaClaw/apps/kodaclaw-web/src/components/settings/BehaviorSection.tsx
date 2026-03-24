import { useState, useEffect, type FormEvent } from 'react';
import { fetchSettings, saveSettings } from '../../lib/api';
import { Skeleton } from '../ui/Skeleton';
import { Toggle } from '../ui/Toggle';
import { useLocaleText } from '../../i18n/I18nProvider';
import type { KodaClawSettings } from '../../types/contracts';

export function BehaviorSection() {
  const text = useLocaleText({
    zh: {
      sectionTitle: '行为控制',
      sectionDesc: '审批策略与自动化引擎开关。',
      requireApproval: '外部动作需要审批',
      requireApprovalDesc: '开启后，Koda 执行外部动作（如发送消息）前将暂停并等待你的确认。',
      automationsEnabled: '启用自动化引擎',
      automationsDesc: '关闭后，所有定时任务和心跳规则将暂停执行。',
      autoApproveToolCalls: '工具调用自动授权',
      autoApproveToolCallsDesc: '开启后，Agent 执行所有工具时跳过审批弹卡，自动授权（对新建/恢复的会话生效）。',
      save: '保存',
      saving: '保存中…',
      saved: '已保存。',
      loadError: '加载设置失败。',
      saveError: '保存设置失败。',
    },
    en: {
      sectionTitle: 'Behavior',
      sectionDesc: 'Approval policy and automation engine controls.',
      requireApproval: 'Require approval for external actions',
      requireApprovalDesc: 'When enabled, Koda pauses before sending messages or performing external actions and waits for your confirmation.',
      automationsEnabled: 'Automations engine enabled',
      automationsDesc: 'When disabled, all scheduled tasks and heartbeat rules are paused.',
      autoApproveToolCalls: 'Auto-approve tool calls',
      autoApproveToolCallsDesc: 'When enabled, tool calls are approved automatically without inline approval cards (takes effect on new or resumed sessions).',
      save: 'Save',
      saving: 'Saving…',
      saved: 'Saved.',
      loadError: 'Failed to load settings.',
      saveError: 'Failed to save settings.',
    },
  });

  const [draft, setDraft] = useState<KodaClawSettings | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setIsLoading(true);
    fetchSettings()
      .then(s => { if (!cancelled) { setDraft(s); setIsLoading(false); } })
      .catch(() => { if (!cancelled) { setError(text.loadError); setIsLoading(false); } });
    return () => { cancelled = true; };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleSave(e: FormEvent) {
    e.preventDefault();
    if (!draft) return;
    setIsSaving(true);
    setNote(null);
    setError(null);
    try {
      const saved = await saveSettings(draft);
      setDraft(saved);
      setNote(text.saved);
    } catch {
      setError(text.saveError);
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <div className="settings-section" data-testid="settings-behavior-section">
      <h2 className="settings-section-title">{text.sectionTitle}</h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

      {isLoading ? (
        <Skeleton count={2} height={40} />
      ) : (
        <form onSubmit={e => { void handleSave(e); }}>
          <div className="settings-toggle-row">
            <div className="settings-action-info">
              <div className="settings-action-label">{text.requireApproval}</div>
              <div className="settings-action-desc">{text.requireApprovalDesc}</div>
            </div>
            <Toggle
              data-testid="settings-require-approval"
              checked={draft?.requireApprovalForExternalActions ?? false}
              onChange={e => setDraft(d => d ? { ...d, requireApprovalForExternalActions: e.target.checked } : d)}
            />
          </div>

          <div className="settings-toggle-row">
            <div className="settings-action-info">
              <div className="settings-action-label">{text.automationsEnabled}</div>
              <div className="settings-action-desc">{text.automationsDesc}</div>
            </div>
            <Toggle
              data-testid="settings-automations-enabled-toggle"
              checked={draft?.automationsEnabled ?? false}
              onChange={e => setDraft(d => d ? { ...d, automationsEnabled: e.target.checked } : d)}
            />
          </div>

          <div className="settings-toggle-row">
            <div className="settings-action-info">
              <div className="settings-action-label">{text.autoApproveToolCalls}</div>
              <div className="settings-action-desc">{text.autoApproveToolCallsDesc}</div>
            </div>
            <Toggle
              data-testid="settings-auto-approve-tool-calls-toggle"
              checked={draft?.autoApproveToolCalls ?? false}
              onChange={e => setDraft(d => d ? { ...d, autoApproveToolCalls: e.target.checked } : d)}
            />
          </div>

          {error && <p className="settings-file-error" style={{ marginTop: 'var(--space-2)' }}>{error}</p>}
          {note && <p className="settings-file-success" style={{ marginTop: 'var(--space-2)' }}>{note}</p>}

          <div style={{ marginTop: 'var(--space-4)' }}>
            <button
              type="submit"
              className="btn btn--primary"
              disabled={isSaving || !draft}
            >
              {isSaving ? text.saving : text.save}
            </button>
          </div>
        </form>
      )}
    </div>
  );
}
