import { useState, useEffect, type FormEvent } from 'react';
import { fetchSettings, saveSettings } from '../../lib/api';
import { Skeleton } from '../ui/Skeleton';
import { Toggle } from '../ui/Toggle';
import { useLocaleText } from '../../i18n/I18nProvider';
import type { KodaClawSettings } from '../../types/contracts';

export function NotificationsSection() {
  const text = useLocaleText({
    zh: {
      sectionTitle: '通知',
      sectionDesc: '控制推送通知与静默时段。',
      notificationsEnabled: '启用通知',
      notificationsDesc: '允许 Koda 在任务完成或需要关注时发送通知。',
      quietHoursEnabled: '启用静默时段',
      quietHoursDesc: '在指定时间段内屏蔽推送通知。',
      quietStart: '静默开始（HH:mm）',
      quietEnd: '静默结束（HH:mm）',
      save: '保存',
      saving: '保存中…',
      saved: '已保存。',
      loadError: '加载设置失败。',
      saveError: '保存设置失败。',
    },
    en: {
      sectionTitle: 'Notifications',
      sectionDesc: 'Control push notifications and quiet hours.',
      notificationsEnabled: 'Enable notifications',
      notificationsDesc: 'Allow Koda to send notifications when tasks complete or need attention.',
      quietHoursEnabled: 'Enable quiet hours',
      quietHoursDesc: 'Suppress push notifications during the specified time window.',
      quietStart: 'Quiet start (HH:mm)',
      quietEnd: 'Quiet end (HH:mm)',
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
    <div className="settings-section" data-testid="settings-notifications-section">
      <h2 className="settings-section-title">{text.sectionTitle}</h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

      {isLoading ? (
        <Skeleton count={3} height={40} />
      ) : (
        <form onSubmit={e => { void handleSave(e); }}>
          <div className="settings-toggle-row">
            <div className="settings-action-info">
              <div className="settings-action-label">{text.notificationsEnabled}</div>
              <div className="settings-action-desc">{text.notificationsDesc}</div>
            </div>
            <Toggle
              data-testid="settings-notifications"
              checked={draft?.notificationsEnabled ?? false}
              onChange={e => setDraft(d => d ? { ...d, notificationsEnabled: e.target.checked } : d)}
            />
          </div>

          <div className="settings-toggle-row">
            <div className="settings-action-info">
              <div className="settings-action-label">{text.quietHoursEnabled}</div>
              <div className="settings-action-desc">{text.quietHoursDesc}</div>
            </div>
            <Toggle
              data-testid="settings-quiet-hours"
              checked={draft?.quietHoursEnabled ?? false}
              onChange={e => setDraft(d => d ? { ...d, quietHoursEnabled: e.target.checked } : d)}
            />
          </div>

          {draft?.quietHoursEnabled && (
            <div style={{ paddingTop: 'var(--space-3)' }}>
              <div className="settings-pref-row">
                <span className="settings-pref-label">{text.quietStart}</span>
                <input
                  data-testid="settings-quiet-start"
                  className="kc-input"
                  value={draft?.quietHoursStartLocalTime ?? ''}
                  onChange={e => {
                    const v = e.target.value.trim() || null;
                    setDraft(d => d ? { ...d, quietHoursStartLocalTime: v } : d);
                  }}
                />
              </div>
              <div className="settings-pref-row" style={{ marginTop: 'var(--space-2)' }}>
                <span className="settings-pref-label">{text.quietEnd}</span>
                <input
                  data-testid="settings-quiet-end"
                  className="kc-input"
                  value={draft?.quietHoursEndLocalTime ?? ''}
                  onChange={e => {
                    const v = e.target.value.trim() || null;
                    setDraft(d => d ? { ...d, quietHoursEndLocalTime: v } : d);
                  }}
                />
              </div>
            </div>
          )}

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
