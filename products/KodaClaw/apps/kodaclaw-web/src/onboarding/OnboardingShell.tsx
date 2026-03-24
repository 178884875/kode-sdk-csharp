import { useCallback } from 'react';
import type { OnboardingState } from '../types/contracts';
import { completeOnboarding } from '../lib/api';
import { ModelStep } from './steps/ModelStep';
import './onboarding.css';

interface Props {
  initialState: OnboardingState;
  onComplete: () => void;
}

export function OnboardingShell({ onComplete }: Props) {
  const handleSkip = useCallback(async () => {
    await completeOnboarding().catch(() => {});
    onComplete();
  }, [onComplete]);

  const handleDone = useCallback(async () => {
    await completeOnboarding().catch(() => {});
    onComplete();
  }, [onComplete]);

  return (
    <div className="ob-shell" data-testid="onboarding-shell">
      {/* ── Left: brand panel ── */}
      <aside className="ob-left">
        <div className="ob-left-inner">
          <div className="ob-brand">
            <div className="ob-brand-mark">K</div>
            <div className="ob-brand-name">KodaClaw</div>
            <div className="ob-brand-sub">运行在你本机的 Agent OS</div>
          </div>

          <div className="ob-divider" />

          <ul className="ob-features">
            <li>
              <span className="ob-feature-icon">◈</span>
              <span>
                <strong>对话即工作流</strong>
                <em>说一句话，Koda 安排好一切</em>
              </span>
            </li>
            <li>
              <span className="ob-feature-icon">◈</span>
              <span>
                <strong>工作区是文件</strong>
                <em>人格、记忆、规则，纯文本可读可改</em>
              </span>
            </li>
            <li>
              <span className="ob-feature-icon">◈</span>
              <span>
                <strong>MCP 无限扩展</strong>
                <em>接入任何工具和外部服务</em>
              </span>
            </li>
          </ul>

          <div className="ob-left-footer">
            <span className="ob-version">v0.1.0</span>
            <span className="ob-local-badge">local-first</span>
          </div>
        </div>
        <div className="ob-left-glow" />
      </aside>

      {/* ── Right: config panel ── */}
      <main className="ob-right">
        <div className="ob-form-wrap">
          <ModelStep onNext={handleDone} onSkip={handleSkip} />
        </div>
      </main>
    </div>
  );
}
