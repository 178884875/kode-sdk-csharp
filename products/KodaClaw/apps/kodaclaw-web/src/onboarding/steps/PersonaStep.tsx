import { useState, useEffect } from 'react';
import type { PersonaPreset } from '../../types/contracts';
import { fetchPersonaPresets, applyPersonaPreset } from '../../lib/api';

interface Props {
  onNext: (presetId: string) => void;
  onSkip: () => void;
}

export function PersonaStep({ onNext, onSkip }: Props) {
  const [presets, setPresets] = useState<PersonaPreset[]>([]);
  const [selected, setSelected] = useState<PersonaPreset | null>(null);
  const [applying, setApplying] = useState(false);

  useEffect(() => {
    fetchPersonaPresets().then(setPresets).catch(() => {});
  }, []);

  const handleApply = async () => {
    if (!selected) return;
    setApplying(true);
    try {
      await applyPersonaPreset(selected.presetId);
      onNext(selected.presetId);
    } catch {
      onNext(selected.presetId);
    } finally {
      setApplying(false);
    }
  };

  return (
    <div className="onboarding-step" data-testid="onboarding-step-persona">
      <h1 className="onboarding-step-title">选择 Koda 的风格</h1>
      <p className="onboarding-step-desc">选择一种和你最搭的沟通风格，之后可以随时调整</p>

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
                <div className="persona-preview-title">核心准则预览</div>
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

      {selected && (
        <button
          className="onboarding-next-btn apply-persona-btn"
          data-testid="apply-persona-btn"
          onClick={() => void handleApply()}
          disabled={applying}
        >
          {applying ? '应用中...' : `使用「${selected.displayName}」风格 →`}
        </button>
      )}

      <button
        className="onboarding-skip-step-btn"
        data-testid="skip-to-bootstrap-btn"
        onClick={onSkip}
      >
        让 Koda 自己来了解我（通过 Bootstrap 对话决定）
      </button>
    </div>
  );
}
