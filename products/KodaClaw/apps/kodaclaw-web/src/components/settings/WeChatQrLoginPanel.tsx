import { useState, useEffect, useRef } from 'react';
import QRCode from 'qrcode';
import { getWeChatQrCode, pollWeChatQrStatus } from '../../lib/api';
import { useLocaleText } from '../../i18n/I18nProvider';

type WeChatQrLoginPanelProps = {
  onLoginSuccess: (botToken: string) => void;
};

type PanelState = 'idle' | 'loading' | 'waiting' | 'scaned' | 'confirmed' | 'expired' | 'error';

const MAX_POLL_DURATION_MS = 3 * 60 * 1000; // 3 分钟超时

export function WeChatQrLoginPanel({ onLoginSuccess }: WeChatQrLoginPanelProps) {
  const [state, setState] = useState<PanelState>('idle');
  const [qrcode, setQrcode] = useState<string | null>(null);
  const [qrcodeImgUrl, setQrcodeImgUrl] = useState<string | null>(null);
  const [qrcodeDataUrl, setQrcodeDataUrl] = useState<string | null>(null);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const pollTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const deadlineRef = useRef<number>(0);

  const text = useLocaleText({
    zh: {
      getQrcode: '获取二维码',
      loading: '正在获取二维码...',
      scanPrompt: '请用微信扫码登录',
      scanExpiry: '二维码有效期约 3 分钟',
      scaned: '已扫码，请在手机上确认',
      confirmed: '登录成功！',
      expired: '二维码已过期',
      refresh: '重新获取',
      error: '获取失败，请重试',
    },
    en: {
      getQrcode: 'Get QR Code',
      loading: 'Fetching QR code...',
      scanPrompt: 'Scan with WeChat to log in',
      scanExpiry: 'QR code valid for ~3 minutes',
      scaned: 'Scanned — please confirm on your phone',
      confirmed: 'Login successful!',
      expired: 'QR code expired',
      refresh: 'Refresh',
      error: 'Failed to fetch QR code, please retry',
    },
  });

  // 清理轮询定时器
  const stopPolling = () => {
    if (pollTimerRef.current) {
      clearInterval(pollTimerRef.current);
      pollTimerRef.current = null;
    }
  };

  useEffect(() => {
    return () => stopPolling();
  }, []);

  const startPolling = (qrcodeToken: string) => {
    stopPolling();
    deadlineRef.current = Date.now() + MAX_POLL_DURATION_MS;

    pollTimerRef.current = setInterval(() => {
      void (async () => {
        // 超时
        if (Date.now() > deadlineRef.current) {
          stopPolling();
          setState('expired');
          return;
        }

        try {
          const status = await pollWeChatQrStatus(qrcodeToken);
          if (status.status === 'scaned') {
            setState('scaned');
          } else if (status.status === 'confirmed' && status.botToken) {
            stopPolling();
            setState('confirmed');
            onLoginSuccess(status.botToken);
          } else if (status.status === 'expired') {
            stopPolling();
            setState('expired');
          }
          // 'wait' → 继续轮询
        } catch {
          // 网络抖动：继续轮询，不中止
        }
      })();
    }, 2000);
  };

  const handleGetQrCode = async () => {
    setState('loading');
    setErrorMsg(null);
    try {
      const result = await getWeChatQrCode();
      setQrcode(result.qrcode);
      setQrcodeImgUrl(result.qrcodeImgUrl);
      // qrcode_img_content 是 H5 页面 URL（Content-Type: text/html），
      // 不能直接作为 <img src>。改用 qrcode 库将该 URL 编码为二维码图片。
      const dataUrl = await QRCode.toDataURL(result.qrcodeImgUrl, {
        width: 180,
        margin: 1,
        color: { dark: '#000000', light: '#ffffff' },
      });
      setQrcodeDataUrl(dataUrl);
      setState('waiting');
      startPolling(result.qrcode);
    } catch (e) {
      setState('error');
      setErrorMsg(e instanceof Error ? e.message : text.error);
    }
  };

  return (
    <div style={{ textAlign: 'center' }}>
      {(state === 'idle' || state === 'error') && (
        <>
          {state === 'error' && (
            <div className="connection-result is-error" style={{ marginBottom: 8 }}>
              {errorMsg ?? text.error}
            </div>
          )}
          <button
            className="onboarding-next-btn"
            onClick={() => void handleGetQrCode()}
          >
            {text.getQrcode}
          </button>
        </>
      )}

      {state === 'loading' && (
        <p className="onboarding-step-desc">{text.loading}</p>
      )}

      {(state === 'waiting' || state === 'scaned') && qrcodeDataUrl && (
        <div>
          <img
            src={qrcodeDataUrl}
            alt="WeChat QR Code"
            style={{ width: 180, height: 180, border: '1px solid var(--color-border, #e5e7eb)', borderRadius: 8 }}
          />
          <p className="onboarding-step-desc" style={{ marginTop: 8 }}>
            {state === 'scaned' ? text.scaned : text.scanPrompt}
          </p>
          {state === 'waiting' && (
            <p style={{ fontSize: 11, color: 'var(--color-text-muted, #9ca3af)' }}>{text.scanExpiry}</p>
          )}
        </div>
      )}

      {state === 'confirmed' && (
        <div className="connection-result is-success">
          {text.confirmed}
        </div>
      )}

      {state === 'expired' && (
        <div>
          <p className="onboarding-step-desc">{text.expired}</p>
          <button
            className="onboarding-next-btn"
            onClick={() => { setState('idle'); setQrcode(null); setQrcodeImgUrl(null); setQrcodeDataUrl(null); }}
          >
            {text.refresh}
          </button>
        </div>
      )}
    </div>
  );
}
