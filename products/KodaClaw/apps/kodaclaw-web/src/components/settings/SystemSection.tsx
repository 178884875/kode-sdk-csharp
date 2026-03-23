import { useState } from 'react';
import { resetOnboarding, updateWorkspaceFile } from '../../lib/api';
import { useLocaleText } from '../../i18n/I18nProvider';
import { ConfirmModal } from '../ui/ConfirmModal';

export function SystemSection() {
  const text = useLocaleText({
    zh: {
      sectionTitle: '系统',
      sectionDesc: '系统级操作，操作不可撤销，请谨慎使用。',
      resetLabel: '重新引导',
      resetDesc: '重置 Onboarding 状态，下次刷新将重新进入引导流程。',
      resetBtn: '重新引导…',
      resetModalTitle: '重新引导',
      resetModalDesc: '此操作将重置 Onboarding 状态，下次刷新将重新进入引导流程。此操作不可撤销。',
      clearLabel: '清除工作区身份',
      clearDesc: '清空 IDENTITY.md、SOUL.md、USER.md。Koda 下次会话会重新引导你配置身份。',
      clearBtn: '清除身份…',
      clearModalTitle: '清除工作区身份',
      clearModalDesc: '此操作将清空 IDENTITY.md、SOUL.md、USER.md。Koda 下次会话会重新引导你配置身份。此操作不可撤销。',
      clearDoneMsg: '已清除。Koda 下次会重新引导你配置身份。',
      confirm: '确认',
      close: '关闭',
      working: '处理中…',
    },
    en: {
      sectionTitle: 'System',
      sectionDesc: 'System-level operations. These actions are irreversible — use with care.',
      resetLabel: 'Re-run Onboarding',
      resetDesc: 'Reset onboarding state. The setup wizard will appear again on next reload.',
      resetBtn: 'Re-run onboarding…',
      resetModalTitle: 'Re-run Onboarding',
      resetModalDesc: 'This will reset your onboarding state. The setup wizard will appear again on next reload. This action cannot be undone.',
      clearLabel: 'Clear Workspace Identity',
      clearDesc: 'Wipe IDENTITY.md, SOUL.md, and USER.md. Koda will guide you through identity setup at the next session.',
      clearBtn: 'Clear identity…',
      clearModalTitle: 'Clear Workspace Identity',
      clearModalDesc: 'This will wipe IDENTITY.md, SOUL.md, and USER.md. Koda will guide you through identity setup at the next session. This action cannot be undone.',
      clearDoneMsg: 'Cleared. Koda will guide you through identity setup next session.',
      confirm: 'Confirm',
      close: 'Close',
      working: 'Working…',
    },
  });

  // Reset onboarding state
  const [resetOpen, setResetOpen] = useState(false);
  const [resetBusy, setResetBusy] = useState(false);
  const [resetError, setResetError] = useState<string | null>(null);

  async function handleResetOnboarding() {
    setResetBusy(true);
    setResetError(null);
    try {
      await resetOnboarding();
      setResetOpen(false);
      window.location.reload();
    } catch (e) {
      setResetError(String(e));
      setResetBusy(false);
    }
  }

  // Clear identity state
  const [clearOpen, setClearOpen] = useState(false);
  const [clearBusy, setClearBusy] = useState(false);
  const [clearDone, setClearDone] = useState(false);
  const [clearError, setClearError] = useState<string | null>(null);

  async function handleClearIdentity() {
    setClearBusy(true);
    setClearError(null);
    try {
      await Promise.all([
        updateWorkspaceFile('identity', ''),
        updateWorkspaceFile('soul', ''),
        updateWorkspaceFile('user', ''),
      ]);
      setClearOpen(false);
      setClearDone(true);
    } catch (e) {
      setClearError(String(e));
    } finally {
      setClearBusy(false);
    }
  }

  return (
    <div className="settings-section" data-testid="settings-system-section">
      <h2 className="settings-section-title">{text.sectionTitle}</h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

      {/* ── Re-run Onboarding ── */}
      <div className="settings-action-row">
        <div className="settings-action-info">
          <div className="settings-action-label">{text.resetLabel}</div>
          <div className="settings-action-desc">{text.resetDesc}</div>
        </div>
        <div>
          <button
            type="button"
            className="btn btn--danger"
            data-testid="settings-reset-onboarding"
            onClick={() => { setResetError(null); setResetOpen(true); }}
          >
            {text.resetBtn}
          </button>
          {resetError && <p className="settings-file-error" style={{ marginTop: 4 }}>{resetError}</p>}
        </div>
      </div>

      {/* ── Clear Identity ── */}
      <div className="settings-action-row">
        <div className="settings-action-info">
          <div className="settings-action-label">{text.clearLabel}</div>
          <div className="settings-action-desc">{text.clearDesc}</div>
        </div>
        <div>
          <button
            type="button"
            className="btn btn--danger"
            data-testid="settings-clear-identity"
            onClick={() => { setClearError(null); setClearDone(false); setClearOpen(true); }}
          >
            {text.clearBtn}
          </button>
          {clearDone && <p className="settings-file-success" style={{ marginTop: 4 }}>{text.clearDoneMsg}</p>}
          {clearError && <p className="settings-file-error" style={{ marginTop: 4 }}>{clearError}</p>}
        </div>
      </div>

      {/* ── Confirm Modals ── */}
      <ConfirmModal
        open={resetOpen}
        title={text.resetModalTitle}
        description={text.resetModalDesc}
        confirmLabel={text.confirm}
        variant="danger"
        busy={resetBusy}
        onConfirm={() => void handleResetOnboarding()}
        onCancel={() => setResetOpen(false)}
      />

      <ConfirmModal
        open={clearOpen}
        title={text.clearModalTitle}
        description={text.clearModalDesc}
        confirmLabel={text.confirm}
        variant="danger"
        busy={clearBusy}
        onConfirm={() => void handleClearIdentity()}
        onCancel={() => setClearOpen(false)}
      />
    </div>
  );
}
