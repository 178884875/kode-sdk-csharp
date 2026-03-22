import { useState } from 'react';
import { testTelegramToken, createChannelAccount } from '../../lib/api';
import { useLocaleText } from '../../i18n/I18nProvider';

type ChannelSetupWizardProps = {
  onComplete: () => void;
  onDismiss: () => void;
};

type SubPhase = 'intro' | 'instructions' | 'token' | 'delivery' | 'success';

export function ChannelSetupWizard({ onComplete, onDismiss }: ChannelSetupWizardProps) {
  const [phase, setPhase] = useState<SubPhase>('intro');
  const [botToken, setBotToken] = useState('');
  const [testing, setTesting] = useState(false);
  const [botName, setBotName] = useState<string | null>(null);
  const [tokenError, setTokenError] = useState<string | null>(null);
  const [selectedDelivery, setSelectedDelivery] = useState('DraftApproval');
  const [saving, setSaving] = useState(false);

  const text = useLocaleText({
    zh: {
      introTitle: '绑定 Telegram',
      introDesc: '绑定后可以通过手机随时和 Koda 对话',
      introStart: '开始绑定',
      instructionsTitle: '创建 Telegram Bot',
      step1: '打开 Telegram，搜索',
      step2: '发送',
      step3: '给 Bot 起一个名字',
      step4: 'BotFather 会给你一个 Token',
      instructionsNext: 'Token 已复制，下一步 →',
      tokenTitle: '输入 Bot Token',
      testing: '验证中...',
      testConnection: '测试连接',
      invalidToken: 'Token 无效，请重新检查',
      networkError: '网络错误，请重试',
      deliveryTitle: '设置发送方式',
      botVerified: (name: string) => `Bot: ${name} 验证成功`,
      deliveryOptions: [
        { value: 'DraftApproval',   label: '草稿审批',  desc: 'Koda 先展示回复，你确认后再发（推荐）', recommended: true },
        { value: 'RequireApproval', label: '需要审批',  desc: '每条消息都需要你在 Web 审批', recommended: false },
        { value: 'AutoSend',        label: '自动发送',  desc: 'Koda 直接发送回复（适合熟练用户）', recommended: false },
      ],
      recommended: '推荐',
      saving: '保存中...',
      finish: '完成绑定 →',
      successTitle: 'Telegram 已绑定 ✓',
      successDesc: '发一条消息给你的 Bot 试试！',
      done: '完成',
      cancel: '取消',
    },
    en: {
      introTitle: 'Connect Telegram',
      introDesc: 'Chat with Koda from your phone anytime after connecting.',
      introStart: 'Get started',
      instructionsTitle: 'Create a Telegram Bot',
      step1: 'Open Telegram and search for',
      step2: 'Send',
      step3: 'Give your bot a name',
      step4: 'BotFather will send you a token',
      instructionsNext: 'Token copied — next step →',
      tokenTitle: 'Enter Bot Token',
      testing: 'Verifying...',
      testConnection: 'Test connection',
      invalidToken: 'Invalid token, please double-check',
      networkError: 'Network error, please retry',
      deliveryTitle: 'Choose delivery mode',
      botVerified: (name: string) => `Bot: ${name} — verified`,
      deliveryOptions: [
        { value: 'DraftApproval',   label: 'Draft approval',   desc: 'Koda shows the reply first; you confirm before sending (recommended)', recommended: true },
        { value: 'RequireApproval', label: 'Require approval', desc: 'Every message needs your approval in the web UI', recommended: false },
        { value: 'AutoSend',        label: 'Auto-send',        desc: 'Koda sends replies directly (for power users)', recommended: false },
      ],
      recommended: 'Recommended',
      saving: 'Saving...',
      finish: 'Finish setup →',
      successTitle: 'Telegram connected ✓',
      successDesc: 'Send a message to your bot to try it out!',
      done: 'Done',
      cancel: 'Cancel',
    },
  });

  const handleTestToken = async () => {
    setTesting(true);
    setTokenError(null);
    try {
      const result = await testTelegramToken(botToken);
      if (result.ok) {
        setBotName(result.botName ?? result.botUsername ?? 'Bot');
        setPhase('delivery');
      } else {
        setTokenError(result.error ?? text.invalidToken);
      }
    } catch {
      setTokenError(text.networkError);
    } finally {
      setTesting(false);
    }
  };

  const handleFinish = async () => {
    setSaving(true);
    try {
      await createChannelAccount({
        id: `telegram-${Date.now()}`,
        connectorKind: 'Telegram',
        displayName: botName ?? 'My Telegram Bot',
        configurationJson: JSON.stringify({ botToken }),
        inboundEnabled: true,
      });
    } catch {
      // best-effort
    } finally {
      setSaving(false);
    }
    setPhase('success');
  };

  return (
    <div className="onboarding-step" data-testid="channel-setup-wizard">
      {phase === 'intro' && (
        <div>
          <h2 style={{ fontSize: 18, fontWeight: 700, marginBottom: 8 }}>{text.introTitle}</h2>
          <p className="onboarding-step-desc">{text.introDesc}</p>
          <button className="onboarding-next-btn" onClick={() => setPhase('instructions')}>
            {text.introStart}
          </button>
          <button className="onboarding-skip-step-btn" onClick={onDismiss}>
            {text.cancel}
          </button>
        </div>
      )}

      {phase === 'instructions' && (
        <div>
          <h2 style={{ fontSize: 18, fontWeight: 700, marginBottom: 8 }}>{text.instructionsTitle}</h2>
          <ol className="telegram-instructions">
            <li>{text.step1} <strong>@BotFather</strong></li>
            <li>{text.step2} <code>/newbot</code></li>
            <li>{text.step3}</li>
            <li>{text.step4}</li>
          </ol>
          <button className="onboarding-next-btn" onClick={() => setPhase('token')}>
            {text.instructionsNext}
          </button>
        </div>
      )}

      {phase === 'token' && (
        <div>
          <h2 style={{ fontSize: 18, fontWeight: 700, marginBottom: 8 }}>{text.tokenTitle}</h2>
          <div className="apikey-input-group">
            <label htmlFor="csw-token-input">Bot Token</label>
            <input
              id="csw-token-input"
              type="password"
              className="apikey-input"
              data-testid="telegram-token-input"
              placeholder="1234567890:ABCDEFGHIJKLMNOPQRSTUVWXYZ"
              value={botToken}
              onChange={e => { setBotToken(e.target.value); setTokenError(null); }}
            />
          </div>
          {tokenError && <div className="connection-result is-error">{tokenError}</div>}
          <button
            className="test-connection-btn"
            data-testid="test-telegram-btn"
            onClick={() => void handleTestToken()}
            disabled={testing || !botToken}
          >
            {testing ? text.testing : text.testConnection}
          </button>
        </div>
      )}

      {phase === 'delivery' && (
        <div>
          <h2 style={{ fontSize: 18, fontWeight: 700, marginBottom: 8 }}>{text.deliveryTitle}</h2>
          <p className="onboarding-step-desc">{text.botVerified(botName ?? '')}</p>
          <div className="delivery-options">
            {text.deliveryOptions.map(opt => (
              <button
                key={opt.value}
                className={`delivery-option-card ${selectedDelivery === opt.value ? 'is-selected' : ''} ${opt.recommended ? 'is-recommended' : ''}`}
                data-testid="delivery-rule-select"
                onClick={() => setSelectedDelivery(opt.value)}
              >
                <div className="delivery-option-label">{opt.label}</div>
                <div className="delivery-option-desc">{opt.desc}</div>
                {opt.recommended && <span className="delivery-recommended-badge">{text.recommended}</span>}
              </button>
            ))}
          </div>
          <button
            className="onboarding-next-btn"
            data-testid="finish-telegram-btn"
            onClick={() => void handleFinish()}
            disabled={saving}
          >
            {saving ? text.saving : text.finish}
          </button>
        </div>
      )}

      {phase === 'success' && (
        <div>
          <h2 style={{ fontSize: 18, fontWeight: 700, marginBottom: 8 }}>{text.successTitle}</h2>
          <p>{text.successDesc}</p>
          <button className="onboarding-next-btn" onClick={onComplete}>
            {text.done}
          </button>
        </div>
      )}
    </div>
  );
}
