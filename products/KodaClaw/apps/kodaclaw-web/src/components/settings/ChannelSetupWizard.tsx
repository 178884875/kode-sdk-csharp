import './ChannelSetupWizard.css';
import { useState } from 'react';
import { testTelegramToken, testFeishuCredentials, createChannelAccount } from '../../lib/api';
import { useLocaleText } from '../../i18n/I18nProvider';
import type { ChannelConnectorKind } from '../../types/contracts';
import { WeChatQrLoginPanel } from './WeChatQrLoginPanel';

type ChannelSetupWizardProps = {
  onComplete: () => void;
  onDismiss: () => void;
};

type SubPhase = 'pick' | 'intro' | 'instructions' | 'credentials' | 'qrlogin' | 'delivery' | 'success';

const CHANNEL_META: Record<string, { emoji: string; label: string }> = {
  Telegram: { emoji: '✈️', label: 'Telegram' },
  Feishu:   { emoji: '🪶', label: '飞书 / Lark' },
  WeChat:   { emoji: '💬', label: '微信' },
  DingTalk: { emoji: '🤖', label: '钉钉 / DingTalk' },
};

export function ChannelSetupWizard({ onComplete, onDismiss }: ChannelSetupWizardProps) {
  const [phase, setPhase] = useState<SubPhase>('pick');
  const [connectorKind, setConnectorKind] = useState<ChannelConnectorKind>('Telegram');

  // Telegram
  const [botToken, setBotToken] = useState('');

  // Feishu
  const [feishuAppId, setFeishuAppId] = useState('');
  const [feishuAppSecret, setFeishuAppSecret] = useState('');

  // DingTalk
  const [dingTalkAppKey, setDingTalkAppKey] = useState('');
  const [dingTalkAppSecret, setDingTalkAppSecret] = useState('');
  const [dingTalkRobotCode, setDingTalkRobotCode] = useState('');

  const [testing, setTesting] = useState(false);
  const [verifiedName, setVerifiedName] = useState<string | null>(null);
  const [credError, setCredError] = useState<string | null>(null);
  const [selectedDelivery, setSelectedDelivery] = useState('DraftApproval');
  const [saving, setSaving] = useState(false);

  const text = useLocaleText({
    zh: {
      pickTitle: '选择渠道类型',
      pickDesc: '绑定外部渠道，通过手机或企业通讯工具与 Koda 对话',
      pickNext: '下一步 →',
      // Telegram intro
      tgIntroTitle: '绑定 Telegram',
      tgIntroDesc: '绑定后可以通过手机随时和 Koda 对话',
      tgIntroStart: '开始绑定',
      tgInstructionsTitle: '创建 Telegram Bot',
      tgStep1: '打开 Telegram，搜索',
      tgStep2: '发送',
      tgStep3: '给 Bot 起一个名字',
      tgStep4: 'BotFather 会给你一个 Token',
      tgInstructionsNext: 'Token 已复制，下一步 →',
      tgCredTitle: '输入 Bot Token',
      tgCredLabel: 'Bot Token',
      tgCredPlaceholder: '1234567890:ABCDEFGHIJKLMNOPQRSTUVWXYZ',
      tgTestBtn: '测试连接',
      // Feishu intro
      fsIntroTitle: '绑定飞书 Bot',
      fsIntroDesc: '使用飞书自建应用机器人，通过 WebSocket 长连接与 Koda 对话',
      fsIntroStart: '开始绑定',
      fsInstructionsTitle: '准备飞书凭证（共 3 步）',
      fsStep1: '前往飞书开放平台，创建自建应用',
      fsStep2: '在"凭证与基础信息"中记录 App ID 和 App Secret',
      fsStep3: '在"权限管理"中开通 im:message 等消息权限并发布版本',
      fsInstructionsNext: '凭证已准备，下一步 →',
      fsCredTitle: '输入飞书凭证',
      fsAppIdLabel: 'App ID',
      fsAppIdPlaceholder: 'cli_xxxxxxxxxxxxxxxx',
      fsAppSecretLabel: 'App Secret',
      fsAppSecretPlaceholder: 'App Secret',
      fsTestBtn: '验证凭证',
      // WeChat
      wxIntroTitle: '绑定微信',
      wxIntroDesc: '通过扫码登录，将个人微信绑定为 Koda 的消息渠道，支持私信对话',
      wxIntroStart: '开始扫码绑定',
      wxSuccessTitle: '微信已绑定 ✓',
      wxSuccessDesc: '用另一个微信账号给你发一条私信试试！Koda 会自动回复。',
      // Shared
      testing: '验证中...',
      credError: '验证失败，请重新检查',
      networkError: '网络错误，请重试',
      deliveryTitle: '设置发送方式',
      botVerified: (name: string) => `已验证：${name}`,
      deliveryOptions: [
        { value: 'DraftApproval',   label: '草稿审批',  desc: 'Koda 先展示回复，你确认后再发（推荐）', recommended: true },
        { value: 'RequireApproval', label: '需要审批',  desc: '每条消息都需要你在 Web 审批', recommended: false },
        { value: 'AutoSend',        label: '自动发送',  desc: 'Koda 直接发送回复（适合熟练用户）', recommended: false },
      ],
      recommended: '推荐',
      saving: '保存中...',
      finish: '完成绑定 →',
      successTitle: (kind: string) => `${kind} 已绑定 ✓`,
      successDesc: '发一条消息给你的 Bot 试试！',
      fsSuccessTitle: '连接已建立，最后一步',
      fsSuccessStep1: '回到飞书开放平台，进入"事件与回调 → 事件配置"',
      fsSuccessStep2: '订阅方式选择"使用长连接接收事件"',
      fsSuccessStep3: '添加事件：im.message.receive_v1（接收消息）',
      fsSuccessStep4: '点击"保存"——飞书此时会检测到 KodaClaw 已建立连接，保存成功',
      fsSuccessStep5: '在飞书搜索栏中搜索你的应用名称，打开对话，发一条消息试试！',
      fsSuccessDesc: '如果 Koda 没有回复，请检查权限管理中 im:message 权限是否已申请并发布版本。',
      // DingTalk intro
      dtIntroTitle: '绑定钉钉机器人',
      dtIntroDesc: '使用钉钉企业内部应用机器人，通过 Stream 模式与 Koda 对话，无需配置回调地址',
      dtIntroStart: '开始绑定',
      dtInstructionsTitle: '准备钉钉凭证（共 4 步）',
      dtStep1: '前往钉钉开放平台注册开发者账号并创建企业',
      dtStep2: '在"应用开发 → 企业内部开发"中创建一个新应用',
      dtStep3: '在"应用信息"中记录 App Key 和 App Secret；在"机器人"功能中记录 Robot Code',
      dtStep4: '发布机器人版本，确保应用处于上线状态',
      dtInstructionsNext: '凭证已准备，下一步 →',
      dtCredTitle: '输入钉钉凭证',
      dtAppKeyLabel: 'App Key',
      dtAppKeyPlaceholder: 'dingxxxxxxxxx',
      dtAppSecretLabel: 'App Secret',
      dtAppSecretPlaceholder: 'App Secret',
      dtRobotCodeLabel: 'Robot Code',
      dtRobotCodePlaceholder: 'dingxxxxxxxxx',
      dtTestBtn: '下一步 →',
      dtSuccessTitle: '钉钉已绑定 ✓',
      dtSuccessStep1: '在钉钉中搜索你的机器人名称',
      dtSuccessStep2: '打开机器人对话',
      dtSuccessStep3: '发一条消息给机器人，Koda 会自动回复',
      dtSuccessDesc: '如果 Koda 没有回复，请检查应用是否已发布，以及机器人功能是否已开启。',
      done: '完成',
      cancel: '取消',
      back: '← 返回',
    },
    en: {
      pickTitle: 'Choose connector type',
      pickDesc: 'Connect a channel to chat with Koda from your phone or work apps.',
      pickNext: 'Next →',
      // Telegram intro
      tgIntroTitle: 'Connect Telegram',
      tgIntroDesc: 'Chat with Koda from your phone anytime after connecting.',
      tgIntroStart: 'Get started',
      tgInstructionsTitle: 'Create a Telegram Bot',
      tgStep1: 'Open Telegram and search for',
      tgStep2: 'Send',
      tgStep3: 'Give your bot a name',
      tgStep4: 'BotFather will send you a token',
      tgInstructionsNext: 'Token copied — next step →',
      tgCredTitle: 'Enter Bot Token',
      tgCredLabel: 'Bot Token',
      tgCredPlaceholder: '1234567890:ABCDEFGHIJKLMNOPQRSTUVWXYZ',
      tgTestBtn: 'Test connection',
      // Feishu intro
      fsIntroTitle: 'Connect Feishu Bot',
      fsIntroDesc: 'Use a Feishu enterprise app bot to chat with Koda via WebSocket.',
      fsIntroStart: 'Get started',
      fsInstructionsTitle: 'Prepare Feishu credentials (3 steps)',
      fsStep1: 'Go to the Feishu Open Platform and create a self-built app',
      fsStep2: 'Copy App ID and App Secret from "Credentials & Basic Info"',
      fsStep3: 'Enable im:message and related permissions under "Permission Management" and publish a version',
      fsInstructionsNext: 'Credentials ready — next step →',
      fsCredTitle: 'Enter Feishu credentials',
      fsAppIdLabel: 'App ID',
      fsAppIdPlaceholder: 'cli_xxxxxxxxxxxxxxxx',
      fsAppSecretLabel: 'App Secret',
      fsAppSecretPlaceholder: 'App Secret',
      fsTestBtn: 'Verify credentials',
      // WeChat
      wxIntroTitle: 'Connect WeChat',
      wxIntroDesc: 'Scan a QR code to link your personal WeChat account as a messaging channel for Koda.',
      wxIntroStart: 'Start QR scan',
      wxSuccessTitle: 'WeChat connected ✓',
      wxSuccessDesc: 'Have another WeChat account send you a DM to test it — Koda will reply automatically.',
      // Shared
      testing: 'Verifying...',
      credError: 'Verification failed, please double-check',
      networkError: 'Network error, please retry',
      deliveryTitle: 'Choose delivery mode',
      botVerified: (name: string) => `Verified: ${name}`,
      deliveryOptions: [
        { value: 'DraftApproval',   label: 'Draft approval',   desc: 'Koda shows the reply first; you confirm before sending (recommended)', recommended: true },
        { value: 'RequireApproval', label: 'Require approval', desc: 'Every message needs your approval in the web UI', recommended: false },
        { value: 'AutoSend',        label: 'Auto-send',        desc: 'Koda sends replies directly (for power users)', recommended: false },
      ],
      recommended: 'Recommended',
      saving: 'Saving...',
      finish: 'Finish setup →',
      successTitle: (kind: string) => `${kind} connected ✓`,
      successDesc: 'Send a message to your bot to try it out!',
      fsSuccessTitle: 'Connection established — one last step',
      fsSuccessStep1: 'Go back to the Feishu console → "Events & Callbacks → Event Configuration"',
      fsSuccessStep2: 'Select "Use long connection to receive events" as the subscription method',
      fsSuccessStep3: 'Add the event: im.message.receive_v1 (Receive messages)',
      fsSuccessStep4: 'Click "Save" — Feishu will detect the active KodaClaw connection and allow saving',
      fsSuccessStep5: 'In Feishu, search for your app name in the search bar, open the conversation, and send a message!',
      fsSuccessDesc: 'If Koda doesn\'t reply, check that the im:message permission is approved and a version is published.',
      // DingTalk intro
      dtIntroTitle: 'Connect DingTalk Bot',
      dtIntroDesc: 'Use a DingTalk enterprise app bot in Stream mode to chat with Koda — no callback URL required.',
      dtIntroStart: 'Get started',
      dtInstructionsTitle: 'Prepare DingTalk credentials (4 steps)',
      dtStep1: 'Go to open.dingtalk.com and register a developer account, then create an enterprise',
      dtStep2: 'Create a new app under "App Development → Enterprise Internal App"',
      dtStep3: 'Copy App Key and App Secret from "App Info"; copy Robot Code from the "Bot" feature section',
      dtStep4: 'Publish the bot version and ensure the app is live',
      dtInstructionsNext: 'Credentials ready — next step →',
      dtCredTitle: 'Enter DingTalk credentials',
      dtAppKeyLabel: 'App Key',
      dtAppKeyPlaceholder: 'dingxxxxxxxxx',
      dtAppSecretLabel: 'App Secret',
      dtAppSecretPlaceholder: 'App Secret',
      dtRobotCodeLabel: 'Robot Code',
      dtRobotCodePlaceholder: 'dingxxxxxxxxx',
      dtTestBtn: 'Next →',
      dtSuccessTitle: 'DingTalk connected ✓',
      dtSuccessStep1: 'Search for your bot name in DingTalk',
      dtSuccessStep2: 'Open the bot conversation',
      dtSuccessStep3: 'Send a message to the bot — Koda will reply automatically',
      dtSuccessDesc: 'If Koda doesn\'t reply, check that the app is published and the bot feature is enabled.',
      done: 'Done',
      cancel: 'Cancel',
      back: '← Back',
    },
  });

  const isTelegram = connectorKind === 'Telegram';
  const isWeChat = connectorKind === 'WeChat';
  const isDingTalk = connectorKind === 'DingTalk';

  const handleTestTelegram = async () => {
    setTesting(true);
    setCredError(null);
    try {
      const result = await testTelegramToken(botToken);
      if (result.ok) {
        setVerifiedName(result.botName ?? result.botUsername ?? 'Bot');
        setPhase('delivery');
      } else {
        setCredError(result.error ?? text.credError);
      }
    } catch {
      setCredError(text.networkError);
    } finally {
      setTesting(false);
    }
  };

  const handleTestFeishu = async () => {
    setTesting(true);
    setCredError(null);
    try {
      const result = await testFeishuCredentials(feishuAppId.trim(), feishuAppSecret.trim());
      if (result.ok) {
        setVerifiedName(result.appName ?? feishuAppId.trim());
        setPhase('delivery');
      } else {
        setCredError(result.error ?? text.credError);
      }
    } catch {
      setCredError(text.networkError);
    } finally {
      setTesting(false);
    }
  };

  const handleFinish = async () => {
    setSaving(true);
    try {
      if (isTelegram) {
        await createChannelAccount({
          id: `telegram-${Date.now()}`,
          connectorKind: 'Telegram',
          displayName: verifiedName ?? 'My Telegram Bot',
          configurationJson: JSON.stringify({ botToken }),
          inboundEnabled: true,
        });
      } else if (isDingTalk) {
        await createChannelAccount({
          id: `dingtalk-${Date.now()}`,
          connectorKind: 'DingTalk',
          displayName: dingTalkAppKey.trim() || '钉钉 Bot',
          configurationJson: JSON.stringify({ appKey: dingTalkAppKey.trim(), appSecret: dingTalkAppSecret.trim(), robotCode: dingTalkRobotCode.trim() }),
          inboundEnabled: true,
        });
      } else if (!isWeChat) {
        await createChannelAccount({
          id: `feishu-${Date.now()}`,
          connectorKind: 'Feishu',
          displayName: verifiedName ?? '飞书 Bot',
          configurationJson: JSON.stringify({ appId: feishuAppId.trim(), appSecret: feishuAppSecret.trim() }),
          inboundEnabled: true,
        });
      }
      // WeChat: account already created server-side on QR login confirmation
    } catch {
      // best-effort
    } finally {
      setSaving(false);
    }
    setPhase('success');
  };

  return (
    <div className="onboarding-step" data-testid="channel-setup-wizard">

      {/* ── Pick connector ── */}
      {phase === 'pick' && (
        <div>
          <h2 className="onboarding-step-title">{text.pickTitle}</h2>
          <p className="onboarding-step-desc">{text.pickDesc}</p>
          <div className="channel-picker-grid">
            {(['Telegram', 'Feishu', 'WeChat', 'DingTalk'] as ChannelConnectorKind[]).map(kind => (
              <button
                key={kind}
                type="button"
                className={`delivery-option-card${connectorKind === kind ? ' is-selected' : ''}`}
                onClick={() => setConnectorKind(kind)}
              >
                <span className="channel-icon">{CHANNEL_META[kind].emoji}</span>
                <div className="delivery-option-label">{CHANNEL_META[kind].label}</div>
              </button>
            ))}
          </div>
          <div className="wizard-btn-row">
            <button className="onboarding-next-btn" onClick={() => setPhase('intro')}>
              {text.pickNext}
            </button>
            <button className="onboarding-skip-step-btn" onClick={onDismiss}>
              {text.cancel}
            </button>
          </div>
        </div>
      )}

      {/* ── Intro ── */}
      {phase === 'intro' && (
        <div>
          <h2 className="onboarding-step-title">
            {isTelegram ? text.tgIntroTitle : isWeChat ? text.wxIntroTitle : isDingTalk ? text.dtIntroTitle : text.fsIntroTitle}
          </h2>
          <p className="onboarding-step-desc">
            {isTelegram ? text.tgIntroDesc : isWeChat ? text.wxIntroDesc : isDingTalk ? text.dtIntroDesc : text.fsIntroDesc}
          </p>
          <div className="wizard-btn-row">
            <button
              className="onboarding-next-btn"
              onClick={() => setPhase(isWeChat ? 'qrlogin' : 'instructions')}
            >
              {isTelegram ? text.tgIntroStart : isWeChat ? text.wxIntroStart : isDingTalk ? text.dtIntroStart : text.fsIntroStart}
            </button>
            <button className="onboarding-skip-step-btn" onClick={() => setPhase('pick')}>
              {text.back}
            </button>
          </div>
        </div>
      )}

      {/* ── Instructions ── */}
      {phase === 'instructions' && (
        <div>
          <h2 className="onboarding-step-title">
            {isTelegram ? text.tgInstructionsTitle : isDingTalk ? text.dtInstructionsTitle : text.fsInstructionsTitle}
          </h2>
          {isTelegram ? (
            <ol className="telegram-instructions">
              <li>{text.tgStep1} <strong>@BotFather</strong></li>
              <li>{text.tgStep2} <code>/newbot</code></li>
              <li>{text.tgStep3}</li>
              <li>{text.tgStep4}</li>
            </ol>
          ) : isDingTalk ? (
            <ol className="telegram-instructions">
              <li>
                {text.dtStep1}{' '}
                <a href="https://open.dingtalk.com" target="_blank" rel="noopener noreferrer">
                  open.dingtalk.com ↗
                </a>
              </li>
              <li>{text.dtStep2}</li>
              <li>{text.dtStep3}</li>
              <li>{text.dtStep4}</li>
            </ol>
          ) : (
            <ol className="telegram-instructions">
              <li>
                {text.fsStep1}{' '}
                <a href="https://open.feishu.cn/app" target="_blank" rel="noopener noreferrer">
                  open.feishu.cn/app ↗
                </a>
              </li>
              <li>{text.fsStep2}</li>
              <li>{text.fsStep3}</li>
            </ol>
          )}
          <div className="wizard-btn-row">
            <button className="onboarding-next-btn" onClick={() => setPhase('credentials')}>
              {isTelegram ? text.tgInstructionsNext : isDingTalk ? text.dtInstructionsNext : text.fsInstructionsNext}
            </button>
            <button className="onboarding-skip-step-btn" onClick={() => setPhase('intro')}>
              {text.back}
            </button>
          </div>
        </div>
      )}

      {/* ── Credentials ── */}
      {phase === 'credentials' && (
        <div>
          <h2 className="onboarding-step-title">
            {isTelegram ? text.tgCredTitle : isDingTalk ? text.dtCredTitle : text.fsCredTitle}
          </h2>
          {isTelegram ? (
            <div className="apikey-input-group">
              <label htmlFor="csw-token-input">{text.tgCredLabel}</label>
              <input
                id="csw-token-input"
                type="password"
                className="apikey-input"
                data-testid="telegram-token-input"
                placeholder={text.tgCredPlaceholder}
                value={botToken}
                onChange={e => { setBotToken(e.target.value); setCredError(null); }}
              />
            </div>
          ) : isDingTalk ? (
            <>
              <div className="apikey-input-group">
                <label htmlFor="csw-dt-app-key">{text.dtAppKeyLabel}</label>
                <input
                  id="csw-dt-app-key"
                  type="text"
                  className="apikey-input"
                  data-testid="dingtalk-app-key-input"
                  placeholder={text.dtAppKeyPlaceholder}
                  value={dingTalkAppKey}
                  onChange={e => { setDingTalkAppKey(e.target.value); setCredError(null); }}
                />
              </div>
              <div className="apikey-input-group">
                <label htmlFor="csw-dt-app-secret">{text.dtAppSecretLabel}</label>
                <input
                  id="csw-dt-app-secret"
                  type="password"
                  className="apikey-input"
                  data-testid="dingtalk-app-secret-input"
                  placeholder={text.dtAppSecretPlaceholder}
                  value={dingTalkAppSecret}
                  onChange={e => { setDingTalkAppSecret(e.target.value); setCredError(null); }}
                />
              </div>
              <div className="apikey-input-group">
                <label htmlFor="csw-dt-robot-code">{text.dtRobotCodeLabel}</label>
                <input
                  id="csw-dt-robot-code"
                  type="text"
                  className="apikey-input"
                  data-testid="dingtalk-robot-code-input"
                  placeholder={text.dtRobotCodePlaceholder}
                  value={dingTalkRobotCode}
                  onChange={e => { setDingTalkRobotCode(e.target.value); setCredError(null); }}
                />
              </div>
            </>
          ) : (
            <>
              <div className="apikey-input-group">
                <label htmlFor="csw-feishu-app-id">{text.fsAppIdLabel}</label>
                <input
                  id="csw-feishu-app-id"
                  type="text"
                  className="apikey-input"
                  data-testid="feishu-app-id-input"
                  placeholder={text.fsAppIdPlaceholder}
                  value={feishuAppId}
                  onChange={e => { setFeishuAppId(e.target.value); setCredError(null); }}
                />
              </div>
              <div className="apikey-input-group">
                <label htmlFor="csw-feishu-app-secret">{text.fsAppSecretLabel}</label>
                <input
                  id="csw-feishu-app-secret"
                  type="password"
                  className="apikey-input"
                  data-testid="feishu-app-secret-input"
                  placeholder={text.fsAppSecretPlaceholder}
                  value={feishuAppSecret}
                  onChange={e => { setFeishuAppSecret(e.target.value); setCredError(null); }}
                />
              </div>
            </>
          )}
          {credError && <div className="connection-result is-error">{credError}</div>}
          <div className="wizard-btn-row">
            {isDingTalk ? (
              <button
                className="test-connection-btn"
                data-testid="test-dingtalk-btn"
                onClick={() => setPhase('delivery')}
                disabled={!dingTalkAppKey || !dingTalkAppSecret || !dingTalkRobotCode}
              >
                {text.dtTestBtn}
              </button>
            ) : (
              <button
                className="test-connection-btn"
                data-testid={isTelegram ? 'test-telegram-btn' : 'test-feishu-btn'}
                onClick={() => void (isTelegram ? handleTestTelegram() : handleTestFeishu())}
                disabled={testing || (isTelegram ? !botToken : !feishuAppId || !feishuAppSecret)}
              >
                {testing ? text.testing : (isTelegram ? text.tgTestBtn : text.fsTestBtn)}
              </button>
            )}
            <button className="onboarding-skip-step-btn" onClick={() => setPhase('instructions')}>
              {text.back}
            </button>
          </div>
        </div>
      )}

      {/* ── WeChat QR login ── */}
      {phase === 'qrlogin' && (
        <div>
          <h2 className="onboarding-step-title">{text.wxIntroTitle}</h2>
          <WeChatQrLoginPanel
            onLoginSuccess={(_token) => {
              setVerifiedName('微信');
              setPhase('delivery');
            }}
          />
          <button className="onboarding-skip-step-btn" style={{ marginTop: 12 }} onClick={() => setPhase('intro')}>
            {text.back}
          </button>
        </div>
      )}

      {/* ── Delivery mode ── */}
      {phase === 'delivery' && (
        <div>
          <h2 className="onboarding-step-title">{text.deliveryTitle}</h2>
          <p className="onboarding-step-desc">{text.botVerified(verifiedName ?? '')}</p>
          <div className="delivery-options">
            {text.deliveryOptions.map(opt => (
              <button
                key={opt.value}
                className={`delivery-option-card${selectedDelivery === opt.value ? ' is-selected' : ''}${opt.recommended ? ' is-recommended' : ''}`}
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
            data-testid="finish-channel-btn"
            onClick={() => void handleFinish()}
            disabled={saving}
          >
            {saving ? text.saving : text.finish}
          </button>
        </div>
      )}

      {/* ── Success ── */}
      {phase === 'success' && (
        <div>
          <div className="wizard-success-icon">
            {CHANNEL_META[connectorKind]?.emoji ?? '✓'}
          </div>
          {isTelegram ? (
            <>
              <h2 className="onboarding-step-title">{text.successTitle('Telegram')}</h2>
              <p className="onboarding-step-desc">{text.successDesc}</p>
            </>
          ) : isWeChat ? (
            <>
              <h2 className="onboarding-step-title">{text.wxSuccessTitle}</h2>
              <p className="onboarding-step-desc">{text.wxSuccessDesc}</p>
            </>
          ) : isDingTalk ? (
            <>
              <h2 className="onboarding-step-title">{text.dtSuccessTitle}</h2>
              <ol className="telegram-instructions" style={{ marginBottom: 12 }}>
                <li>{text.dtSuccessStep1}</li>
                <li>{text.dtSuccessStep2}</li>
                <li><strong>{text.dtSuccessStep3}</strong></li>
              </ol>
              <p className="onboarding-step-desc">{text.dtSuccessDesc}</p>
            </>
          ) : (
            <>
              <h2 className="onboarding-step-title">{text.fsSuccessTitle}</h2>
              <ol className="telegram-instructions" style={{ marginBottom: 12 }}>
                <li>
                  {text.fsSuccessStep1}{' '}
                  <a href="https://open.feishu.cn/app" target="_blank" rel="noopener noreferrer">
                    open.feishu.cn/app ↗
                  </a>
                </li>
                <li>{text.fsSuccessStep2}</li>
                <li>{text.fsSuccessStep3}</li>
                <li>{text.fsSuccessStep4}</li>
                <li><strong>{text.fsSuccessStep5}</strong></li>
              </ol>
              <p className="onboarding-step-desc">{text.fsSuccessDesc}</p>
            </>
          )}
          <button className="onboarding-next-btn" onClick={onComplete} style={{ marginTop: 4 }}>
            {text.done}
          </button>
        </div>
      )}
    </div>
  );
}
