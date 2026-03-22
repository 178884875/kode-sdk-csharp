import { useState } from 'react';
import { resetOnboarding, updateWorkspaceFile } from '../../lib/api';
import { useLocaleText } from '../../i18n/I18nProvider';

type ActionState = 'idle' | 'confirming' | 'working' | 'done' | 'error';

function useConfirmAction(action: () => Promise<void>) {
  const [state, setState] = useState<ActionState>('idle');
  const [error, setError] = useState<string | null>(null);

  const request = () => setState('confirming');
  const cancel = () => setState('idle');
  const confirm = async () => {
    setState('working');
    setError(null);
    try {
      await action();
      setState('done');
    } catch (e) {
      setError(String(e));
      setState('error');
    }
  };
  const reset = () => { setState('idle'); setError(null); };

  return { state, error, request, cancel, confirm, reset };
}

export function SystemSection() {
  const text = useLocaleText({
    zh: {
      sectionTitle: '系统',
      sectionDesc: '系统级操作，操作不可撤销，请谨慎使用。',
      resetLabel: '重新引导',
      resetDesc: '重置 Onboarding 状态，下次刷新将重新进入引导流程。',
      resetBtn: '重新引导…',
      resetConfirmMsg: '确认重置 Onboarding？',
      clearLabel: '清除工作区身份',
      clearDesc: '清空 IDENTITY.md、SOUL.md、USER.md。Koda 下次会话会重新引导你配置身份。',
      clearBtn: '清除身份…',
      clearConfirmMsg: '确认清除所有身份文件？',
      clearDoneMsg: '已清除。Koda 下次会重新引导你配置身份。',
      confirm: '确认',
      cancel: '取消',
      close: '关闭',
      working: '处理中…',
    },
    en: {
      sectionTitle: 'System',
      sectionDesc: 'System-level operations. These actions are irreversible — use with care.',
      resetLabel: 'Re-run Onboarding',
      resetDesc: 'Reset onboarding state. The setup wizard will appear again on next reload.',
      resetBtn: 'Re-run onboarding…',
      resetConfirmMsg: 'Confirm reset onboarding?',
      clearLabel: 'Clear Workspace Identity',
      clearDesc: 'Wipe IDENTITY.md, SOUL.md, and USER.md. Koda will guide you through identity setup at the next session.',
      clearBtn: 'Clear identity…',
      clearConfirmMsg: 'Confirm clearing all identity files?',
      clearDoneMsg: 'Cleared. Koda will guide you through identity setup next session.',
      confirm: 'Confirm',
      cancel: 'Cancel',
      close: 'Close',
      working: 'Working…',
    },
  });

  const resetOnboardingAction = useConfirmAction(async () => {
    await resetOnboarding();
    window.location.reload();
  });

  const clearIdentityAction = useConfirmAction(async () => {
    await Promise.all([
      updateWorkspaceFile('identity', ''),
      updateWorkspaceFile('soul', ''),
      updateWorkspaceFile('user', ''),
    ]);
  });

  return (
    <div className="settings-section" data-testid="settings-system-section">
      <h2 className="settings-section-title">{text.sectionTitle}</h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

      <div className="settings-action-row">
        <div className="settings-action-info">
          <div className="settings-action-label">{text.resetLabel}</div>
          <div className="settings-action-desc">{text.resetDesc}</div>
        </div>
        {resetOnboardingAction.state === 'idle' && (
          <button
            type="button"
            className="settings-btn settings-btn--danger"
            data-testid="settings-reset-onboarding"
            onClick={resetOnboardingAction.request}
          >
            {text.resetBtn}
          </button>
        )}
        {resetOnboardingAction.state === 'confirming' && (
          <div className="settings-confirm-row">
            <span className="settings-confirm-text">{text.resetConfirmMsg}</span>
            <button type="button" className="settings-btn settings-btn--danger" onClick={resetOnboardingAction.confirm}>{text.confirm}</button>
            <button type="button" className="settings-btn settings-btn--ghost" onClick={resetOnboardingAction.cancel}>{text.cancel}</button>
          </div>
        )}
        {resetOnboardingAction.state === 'working' && <span className="settings-action-status">{text.working}</span>}
        {resetOnboardingAction.state === 'error' && (
          <div className="settings-confirm-row">
            <span className="settings-file-error">{resetOnboardingAction.error}</span>
            <button type="button" className="settings-btn settings-btn--ghost" onClick={resetOnboardingAction.reset}>{text.close}</button>
          </div>
        )}
      </div>

      <div className="settings-action-row">
        <div className="settings-action-info">
          <div className="settings-action-label">{text.clearLabel}</div>
          <div className="settings-action-desc">{text.clearDesc}</div>
        </div>
        {clearIdentityAction.state === 'idle' && (
          <button
            type="button"
            className="settings-btn settings-btn--danger"
            data-testid="settings-clear-identity"
            onClick={clearIdentityAction.request}
          >
            {text.clearBtn}
          </button>
        )}
        {clearIdentityAction.state === 'confirming' && (
          <div className="settings-confirm-row">
            <span className="settings-confirm-text">{text.clearConfirmMsg}</span>
            <button type="button" className="settings-btn settings-btn--danger" onClick={clearIdentityAction.confirm}>{text.confirm}</button>
            <button type="button" className="settings-btn settings-btn--ghost" onClick={clearIdentityAction.cancel}>{text.cancel}</button>
          </div>
        )}
        {clearIdentityAction.state === 'working' && <span className="settings-action-status">{text.working}</span>}
        {clearIdentityAction.state === 'done' && (
          <div className="settings-confirm-row">
            <span className="settings-file-success">{text.clearDoneMsg}</span>
            <button type="button" className="settings-btn settings-btn--ghost" onClick={clearIdentityAction.reset}>{text.close}</button>
          </div>
        )}
        {clearIdentityAction.state === 'error' && (
          <div className="settings-confirm-row">
            <span className="settings-file-error">{clearIdentityAction.error}</span>
            <button type="button" className="settings-btn settings-btn--ghost" onClick={clearIdentityAction.reset}>{text.close}</button>
          </div>
        )}
      </div>
    </div>
  );
}
