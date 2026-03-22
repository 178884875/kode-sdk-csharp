import { useState, useEffect, type FormEvent } from 'react';
import { fetchSettings, saveSettings } from '../../lib/api';
import { Skeleton } from '../ui/Skeleton';
import { useLocaleText } from '../../i18n/I18nProvider';
import type { KodaClawSettings, ThemeMode } from '../../types/contracts';

export function AppearanceSection() {
  const text = useLocaleText({
    zh: {
      sectionTitle: '外观',
      sectionDesc: '界面主题与默认落地页面。',
      theme: '主题',
      themeSystem: '跟随系统',
      themeLight: '浅色',
      themeDark: '深色',
      landingRoute: '默认落地路由',
      save: '保存',
      saving: '保存中…',
      saved: '已保存。',
      loadError: '加载设置失败。',
      saveError: '保存设置失败。',
    },
    en: {
      sectionTitle: 'Appearance',
      sectionDesc: 'Interface theme and default landing destination.',
      theme: 'Theme',
      themeSystem: 'System',
      themeLight: 'Light',
      themeDark: 'Dark',
      landingRoute: 'Default landing route',
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
    <div className="settings-section" data-testid="settings-appearance-section">
      <h2 className="settings-section-title">{text.sectionTitle}</h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

      {isLoading ? (
        <Skeleton count={2} height={40} />
      ) : (
        <form onSubmit={e => { void handleSave(e); }}>
          <div className="settings-pref-row">
            <span className="settings-pref-label">{text.theme}</span>
            <select
              data-testid="settings-theme"
              className="settings-select"
              value={draft?.theme ?? 'System'}
              onChange={e => setDraft(d => d ? { ...d, theme: e.target.value as ThemeMode } : d)}
            >
              <option value="System">{text.themeSystem}</option>
              <option value="Light">{text.themeLight}</option>
              <option value="Dark">{text.themeDark}</option>
            </select>
          </div>

          <div className="settings-pref-row" style={{ marginTop: 'var(--space-3)' }}>
            <span className="settings-pref-label">{text.landingRoute}</span>
            <input
              data-testid="settings-route"
              className="settings-input"
              value={draft?.defaultLandingRoute ?? ''}
              onChange={e => setDraft(d => d ? { ...d, defaultLandingRoute: e.target.value } : d)}
            />
          </div>

          {error && <p className="settings-file-error" style={{ marginTop: 'var(--space-2)' }}>{error}</p>}
          {note && <p className="settings-file-success" style={{ marginTop: 'var(--space-2)' }}>{note}</p>}

          <div style={{ marginTop: 'var(--space-4)' }}>
            <button
              type="submit"
              className="settings-btn settings-btn--primary"
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
