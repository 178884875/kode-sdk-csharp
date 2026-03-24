import { useState, useEffect } from 'react';
import { ArrowRight, ShieldAlert } from 'lucide-react';
import { fetchSandboxRiskOverview } from '../../lib/api';
import { Skeleton } from '../ui/Skeleton';
import { useLocaleText } from '../../i18n/I18nProvider';
import type { SandboxRiskOverviewResponse } from '../../types/contracts';

function navigateToSessions() {
  window.dispatchEvent(new CustomEvent('kc:desk-navigate', { detail: { desk: 'sessions' } }));
}

export function RiskSummaryCard() {
  const text = useLocaleText({
    zh: {
      title: '沙箱与风险简报',
      desc: '当前运行时安全姿态摘要。',
      sandbox: '活跃沙箱',
      highRisk: '高风险插件',
      pending: '待处理审批',
      unknown: '未知',
      viewFull: '查看完整简报',
      loadError: '加载失败',
    },
    en: {
      title: 'Sandbox & Risk Briefing',
      desc: 'Current runtime security posture summary.',
      sandbox: 'Active sandbox',
      highRisk: 'High-risk plugins',
      pending: 'Pending approvals',
      unknown: 'Unknown',
      viewFull: 'View full briefing',
      loadError: 'Failed to load',
    },
  });

  const [data, setData] = useState<SandboxRiskOverviewResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [hasError, setHasError] = useState(false);

  useEffect(() => {
    let cancelled = false;
    fetchSandboxRiskOverview()
      .then(d => { if (!cancelled) { setData(d); setIsLoading(false); } })
      .catch(() => { if (!cancelled) { setHasError(true); setIsLoading(false); } });
    return () => { cancelled = true; };
  }, []);

  const activeProfile = data?.executionProfiles?.find(p => p.active);

  return (
    <div className="settings-section" data-testid="settings-risk-summary">
      <h2 className="settings-section-title">{text.title}</h2>
      <p className="settings-section-desc">{text.desc}</p>

      {isLoading ? (
        <Skeleton count={3} height={32} />
      ) : hasError ? (
        <p className="settings-file-error">{text.loadError}</p>
      ) : (
        <div className="risk-summary__metrics">
          <div className="risk-summary__row">
            <span className="risk-summary__label">{text.sandbox}</span>
            <span className="risk-summary__value">
              {activeProfile?.displayName ?? text.unknown}
            </span>
          </div>
          <div className="risk-summary__row">
            <span className="risk-summary__label">{text.highRisk}</span>
            <span className={`risk-summary__value${(data?.pluginRisk?.highRiskCount ?? 0) > 0 ? ' risk-summary__value--warn' : ''}`}>
              {data?.pluginRisk?.highRiskCount ?? 0}
            </span>
          </div>
          <div className="risk-summary__row">
            <span className="risk-summary__label">{text.pending}</span>
            <span className={`risk-summary__value${(data?.channelRisk?.pendingApprovalCount ?? 0) > 0 ? ' risk-summary__value--warn' : ''}`}>
              {data?.channelRisk?.pendingApprovalCount ?? 0}
            </span>
          </div>
        </div>
      )}

      <div style={{ marginTop: 'var(--space-4)' }}>
        <button
          type="button"
          className="btn btn--secondary"
          onClick={navigateToSessions}
          style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--space-2)' }}
        >
          <ShieldAlert size={14} strokeWidth={1.75} />
          {text.viewFull}
          <ArrowRight size={14} strokeWidth={1.75} />
        </button>
      </div>
    </div>
  );
}
