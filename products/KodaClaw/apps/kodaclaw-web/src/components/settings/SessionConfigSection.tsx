import { useState, useEffect, type FormEvent } from 'react';
import { fetchSettings, saveSettings } from '../../lib/api';
import { Skeleton } from '../ui/Skeleton';
import { useLocaleText } from '../../i18n/I18nProvider';
import type { KodaClawSettings } from '../../types/contracts';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../lib/queryKeys';

// Hardcoded defaults shown as placeholders when the user hasn't overridden.
const DEFAULT_MAIN = 30;
const DEFAULT_CHANNEL = 15;
const DEFAULT_AUTOMATION = 50;

export function SessionConfigSection() {
  const text = useLocaleText({
    zh: {
      sectionTitle: '会话参数',
      sectionDesc: '每次对话允许 Agent 执行的最大 LLM 调用轮数（每轮包含一次模型请求及其触发的工具调用）。超出后本轮对话自动结束。',
      mainLabel: '主会话',
      mainDesc: `Chat 交互式会话的最大轮数。默认 ${DEFAULT_MAIN}。`,
      channelLabel: '渠道会话',
      channelDesc: `Telegram / 飞书等渠道消息会话的最大轮数。默认 ${DEFAULT_CHANNEL}。`,
      automationLabel: '自动化会话',
      automationDesc: `HEARTBEAT 规则触发的自动化任务会话的最大轮数。默认 ${DEFAULT_AUTOMATION}。`,
      save: '保存',
      saving: '保存中…',
      saved: '已保存。',
      loadError: '加载设置失败。',
      saveError: '保存设置失败。',
      invalidInput: '请输入 1–500 之间的整数，或留空使用默认值。',
    },
    en: {
      sectionTitle: 'Session Parameters',
      sectionDesc: 'Maximum LLM rounds per conversation turn (each round = one model request + any tool calls it triggers). The turn ends automatically when the limit is reached.',
      mainLabel: 'Main session',
      mainDesc: `Max rounds for Chat interactive sessions. Default ${DEFAULT_MAIN}.`,
      channelLabel: 'Channel session',
      channelDesc: `Max rounds for Telegram / Feishu channel message sessions. Default ${DEFAULT_CHANNEL}.`,
      automationLabel: 'Automation session',
      automationDesc: `Max rounds for HEARTBEAT-triggered automation task sessions. Default ${DEFAULT_AUTOMATION}.`,
      save: 'Save',
      saving: 'Saving…',
      saved: 'Saved.',
      loadError: 'Failed to load settings.',
      saveError: 'Failed to save settings.',
      invalidInput: 'Enter an integer between 1 and 500, or leave blank to use the default.',
    },
  });

  const queryClient = useQueryClient();
  const { data: settingsData, isLoading } = useQuery({ queryKey: queryKeys.settings, queryFn: () => fetchSettings() });
  const [draft, setDraft] = useState<KodaClawSettings | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (settingsData && draft === null) setDraft(settingsData);
  }, [settingsData, draft]);

  function parseIterations(raw: string): number | null | undefined {
    const trimmed = raw.trim();
    if (trimmed === '') return null; // use default
    const n = parseInt(trimmed, 10);
    if (isNaN(n) || n < 1 || n > 500) return undefined; // invalid
    return n;
  }

  function handleChange(field: 'mainMaxIterations' | 'channelMaxIterations' | 'automationMaxIterations', raw: string) {
    setDraft(d => {
      if (!d) return d;
      const parsed = parseIterations(raw);
      if (parsed === undefined) return d; // invalid, don't update
      return { ...d, [field]: parsed };
    });
  }

  async function handleSave(e: FormEvent) {
    e.preventDefault();
    if (!draft) return;
    setIsSaving(true);
    setNote(null);
    setError(null);
    try {
      const saved = await saveSettings(draft);
      setDraft(saved);
      void queryClient.invalidateQueries({ queryKey: queryKeys.settings });
      setNote(text.saved);
    } catch {
      setError(text.saveError);
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <div className="settings-section" data-testid="settings-session-config-section">
      <h2 className="settings-section-title">{text.sectionTitle}</h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

      {isLoading ? (
        <Skeleton count={3} height={40} />
      ) : (
        <form onSubmit={e => { void handleSave(e); }}>
          <IterationsRow
            label={text.mainLabel}
            desc={text.mainDesc}
            value={draft?.mainMaxIterations ?? null}
            placeholder={String(DEFAULT_MAIN)}
            testId="settings-main-max-iterations"
            onChange={v => handleChange('mainMaxIterations', v)}
          />
          <IterationsRow
            label={text.channelLabel}
            desc={text.channelDesc}
            value={draft?.channelMaxIterations ?? null}
            placeholder={String(DEFAULT_CHANNEL)}
            testId="settings-channel-max-iterations"
            onChange={v => handleChange('channelMaxIterations', v)}
          />
          <IterationsRow
            label={text.automationLabel}
            desc={text.automationDesc}
            value={draft?.automationMaxIterations ?? null}
            placeholder={String(DEFAULT_AUTOMATION)}
            testId="settings-automation-max-iterations"
            onChange={v => handleChange('automationMaxIterations', v)}
          />

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

interface IterationsRowProps {
  label: string;
  desc: string;
  value: number | null | undefined;
  placeholder: string;
  testId: string;
  onChange: (raw: string) => void;
}

function IterationsRow({ label, desc, value, placeholder, testId, onChange }: IterationsRowProps) {
  return (
    <div className="settings-toggle-row">
      <div className="settings-action-info">
        <div className="settings-action-label">{label}</div>
        <div className="settings-action-desc">{desc}</div>
      </div>
      <input
        type="number"
        data-testid={testId}
        className="settings-number-input"
        min={1}
        max={500}
        value={value ?? ''}
        placeholder={placeholder}
        onChange={e => onChange(e.target.value)}
        style={{
          width: '5rem',
          padding: 'var(--space-1) var(--space-2)',
          borderRadius: 'var(--radius-sm)',
          border: '1px solid var(--border)',
          background: 'var(--surface)',
          color: 'var(--fg)',
          fontSize: 'var(--text-sm)',
          textAlign: 'right',
          flexShrink: 0,
        }}
      />
    </div>
  );
}
