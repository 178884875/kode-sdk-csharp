import type { OnboardingState } from '../../types/contracts';

interface Props {
  state: OnboardingState;
  onComplete: () => void;
}

export function DoneStep({ state, onComplete }: Props) {
  return (
    <div className="onboarding-step onboarding-step--done" data-testid="onboarding-step-done">
      <div className="done-checkmark">✓</div>
      <h2 className="desk-section-title">Koda 已就绪</h2>
      <p className="desk-section-desc">模型已配置，开始和 Koda 对话吧</p>

      {state.selectedPresetId && (
        <div className="done-summary">
          <div className="done-summary-item">✅ 模型已配置</div>
        </div>
      )}

      <div className="done-suggestions">
        <h3>接下来可以做什么</h3>
        <div className="done-suggestion">💬 和 Koda 打个招呼，它会主动了解你的偏好</div>
        <div className="done-suggestion">⚙️ 在设置页编辑 Koda 的身份与行为风格</div>
        <div className="done-suggestion">📡 在渠道页绑定 Telegram，随时随地和 Koda 对话</div>
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
