import { useState } from 'react';
import { testTelegramToken, createChannelAccount } from '../../lib/api';

interface Props {
  onNext: () => void;
  onSkip: () => void;
}

type ChannelSubPhase = 'intro' | 'instructions' | 'token' | 'delivery' | 'success';

const DELIVERY_OPTIONS = [
  { value: 'DraftApproval', label: '草稿审批', desc: 'Koda 先展示回复，你确认后再发（推荐新手）', recommended: true },
  { value: 'RequireApproval', label: '需要审批', desc: '每条消息都需要你在 Web 审批' },
  { value: 'AutoSend', label: '自动发送', desc: 'Koda 直接发送回复（适合熟练用户）' },
];

export function ChannelStep({ onNext, onSkip }: Props) {
  const [phase, setPhase] = useState<ChannelSubPhase>('intro');
  const [botToken, setBotToken] = useState('');
  const [testing, setTesting] = useState(false);
  const [botName, setBotName] = useState<string | null>(null);
  const [tokenError, setTokenError] = useState<string | null>(null);
  const [selectedDelivery, setSelectedDelivery] = useState('DraftApproval');
  const [saving, setSaving] = useState(false);

  const handleTestToken = async () => {
    setTesting(true);
    setTokenError(null);
    try {
      const result = await testTelegramToken(botToken);
      if (result.ok) {
        setBotName(result.botName ?? result.botUsername ?? 'Bot');
        setPhase('delivery');
      } else {
        setTokenError(result.error ?? 'Token 无效，请重新检查');
      }
    } catch {
      setTokenError('网络错误，请重试');
    } finally {
      setTesting(false);
    }
  };

  const handleFinish = async () => {
    setSaving(true);
    try {
      const accountId = `telegram-${Date.now()}`;
      await createChannelAccount({
        id: accountId,
        connectorKind: 'Telegram',
        displayName: botName ?? 'My Telegram Bot',
        configurationJson: JSON.stringify({ botToken }),
        inboundEnabled: true,
      });
      setPhase('success');
    } catch {
      setPhase('success');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="onboarding-step" data-testid="onboarding-step-channel">
      {phase === 'intro' && (
        <div>
          <h1 className="onboarding-step-title">绑定 Telegram（可选）</h1>
          <p className="onboarding-step-desc">绑定后你可以通过手机随时和 Koda 对话，无需打开 Web</p>
          <p className="onboarding-step-optional">这一步是可选的，你可以之后随时在 Channels 页面绑定</p>
          <button className="onboarding-next-btn" onClick={() => setPhase('instructions')}>
            开始绑定
          </button>
          <button className="onboarding-skip-step-btn" data-testid="skip-telegram-btn" onClick={onSkip}>
            跳过这一步
          </button>
        </div>
      )}

      {phase === 'instructions' && (
        <div>
          <h1 className="onboarding-step-title">创建 Telegram Bot</h1>
          <ol className="telegram-instructions">
            <li>打开 Telegram，搜索 <strong>@BotFather</strong></li>
            <li>发送 <code>/newbot</code></li>
            <li>给 Bot 起一个名字（如 "My Koda Bot"）</li>
            <li>BotFather 会给你一个 Token，如：<code>1234567890:ABC...</code></li>
          </ol>
          <button className="onboarding-next-btn" onClick={() => setPhase('token')}>
            Token 已复制，下一步 →
          </button>
        </div>
      )}

      {phase === 'token' && (
        <div>
          <h1 className="onboarding-step-title">输入 Bot Token</h1>
          <div className="apikey-input-group">
            <label htmlFor="telegram-token-input">Bot Token</label>
            <input
              id="telegram-token-input"
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
            {testing ? '验证中...' : '测试连接'}
          </button>
        </div>
      )}

      {phase === 'delivery' && (
        <div>
          <h1 className="onboarding-step-title">设置发送方式</h1>
          <p className="onboarding-step-desc">Bot: <strong>{botName}</strong> 验证成功</p>
          <div className="delivery-options">
            {DELIVERY_OPTIONS.map(opt => (
              <button
                key={opt.value}
                className={`delivery-option-card ${selectedDelivery === opt.value ? 'is-selected' : ''} ${opt.recommended ? 'is-recommended' : ''}`}
                data-testid="delivery-rule-select"
                onClick={() => setSelectedDelivery(opt.value)}
              >
                <div className="delivery-option-label">{opt.label}</div>
                <div className="delivery-option-desc">{opt.desc}</div>
                {opt.recommended && <span className="delivery-recommended-badge">推荐</span>}
              </button>
            ))}
          </div>
          <button
            className="onboarding-next-btn"
            data-testid="finish-telegram-btn"
            onClick={() => void handleFinish()}
            disabled={saving}
          >
            {saving ? '保存中...' : '完成绑定 →'}
          </button>
        </div>
      )}

      {phase === 'success' && (
        <div>
          <h1 className="onboarding-step-title">Telegram 已绑定</h1>
          <p>发一条消息给你的 Bot 试试！</p>
          <button className="onboarding-next-btn" onClick={onNext}>
            继续 →
          </button>
        </div>
      )}
    </div>
  );
}
