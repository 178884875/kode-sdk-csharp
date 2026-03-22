import { useState, useEffect } from 'react';
import type { PersonaPreset } from '../../types/contracts';
import { fetchPersonaPresets, applyPersonaPreset } from '../../lib/api';
import { useLocaleText } from '../../i18n/I18nProvider';

type PersonaSelectorProps = {
  onSelect: (soulMarkdown: string) => void;
  onDismiss: () => void;
};

export function PersonaSelector({ onSelect, onDismiss }: PersonaSelectorProps) {
  const [presets, setPresets] = useState<PersonaPreset[]>([]);
  const [selected, setSelected] = useState<PersonaPreset | null>(null);
  const [applying, setApplying] = useState(false);

  const text = useLocaleText({
    zh: {
      previewTitle: '核心准则预览',
      applying: '应用中...',
      applyLabel: (name: string) => `使用「${name}」风格 →`,
      cancel: '取消',
    },
    en: {
      previewTitle: 'Core principles preview',
      applying: 'Applying...',
      applyLabel: (name: string) => `Use "${name}" style →`,
      cancel: 'Cancel',
    },
  });

  useEffect(() => {
    fetchPersonaPresets().then(setPresets).catch(() => {});
  }, []);

  const handleApply = async () => {
    if (!selected) return;
    setApplying(true);
    try {
      await applyPersonaPreset(selected.presetId);
    } catch {
      // best-effort; caller still receives the markdown
    } finally {
      setApplying(false);
    }
    onSelect(selected.soulMarkdown);
  };

  return (
    <div data-testid="persona-selector">
      <div className="persona-grid">
        {presets.map(preset => (
          <div
            key={preset.presetId}
            className={`persona-card ${selected?.presetId === preset.presetId ? 'is-selected' : ''}`}
            data-testid={`persona-card-${preset.presetId}`}
            onClick={() => setSelected(selected?.presetId === preset.presetId ? null : preset)}
          >
            <div className="persona-card-name">{preset.displayName}</div>
            <div className="persona-card-tagline">{preset.tagLine}</div>
            <div className="persona-card-desc">{preset.description}</div>
            <div className="persona-card-tags">
              {preset.tags.map(tag => (
                <span key={tag} className="persona-tag">{tag}</span>
              ))}
            </div>
            {selected?.presetId === preset.presetId && (
              <div className="persona-preview" data-testid="persona-preview">
                <div className="persona-preview-title">{text.previewTitle}</div>
                <div className="persona-preview-content">
                  {preset.soulMarkdown.split('\n').filter(l => l.startsWith('-')).slice(0, 5).map((l, i) => (
                    <div key={i} className="persona-preview-bullet">{l}</div>
                  ))}
                </div>
              </div>
            )}
          </div>
        ))}
      </div>

      <div style={{ display: 'flex', gap: 8, marginTop: 16 }}>
        {selected && (
          <button
            className="onboarding-next-btn"
            data-testid="apply-persona-btn"
            onClick={() => void handleApply()}
            disabled={applying}
          >
            {applying ? text.applying : text.applyLabel(selected.displayName)}
          </button>
        )}
        <button className="onboarding-skip-step-btn" onClick={onDismiss}>
          {text.cancel}
        </button>
      </div>
    </div>
  );
}
