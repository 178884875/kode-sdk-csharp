import type { OnboardingState } from '../../types/contracts';

interface Props {
  state: OnboardingState;
  onComplete: () => void;
}

export function DoneStep({ state, onComplete }: Props) {
  const suggestions = [
    '💬 和 Koda 打个招呼，告诉它你在做什么项目',
    state.channelStepSkipped
      ? '📱 在 Channels 页绑定 Telegram，随时随地和 Koda 对话'
      : '📱 打开 Telegram，给你的 Bot 发一条消息',
    '⚡ 在 Automations 页设置你的第一个自动化任务',
  ];

  return (
    <div className="onboarding-step onboarding-step--done" data-testid="onboarding-step-done">
      <div className="done-checkmark">✓</div>
      <h1 className="onboarding-step-title">Koda 已就绪</h1>
      <p className="onboarding-step-desc">一切准备就绪，开始使用吧</p>

      {(state.selectedPresetId || state.selectedPersonaPresetId) && (
        <div className="done-summary">
          {state.selectedPresetId && (
            <div className="done-summary-item">模型已配置</div>
          )}
          {state.selectedPersonaPresetId && (
            <div className="done-summary-item">人格风格已选择</div>
          )}
          {!state.channelStepSkipped && (
            <div className="done-summary-item">Telegram 已绑定</div>
          )}
        </div>
      )}

      <div className="done-suggestions">
        <h3>接下来可以做什么</h3>
        {suggestions.map((s, i) => (
          <div key={i} className="done-suggestion">{s}</div>
        ))}
      </div>

      <button
        className="onboarding-next-btn onboarding-start-btn"
        onClick={onComplete}
      >
        开始使用 KodaClaw →
      </button>
    </div>
  );
}
