import { useState, useEffect, useCallback } from 'react';
import { Plug } from 'lucide-react';
import { fetchChannelAccounts } from '../../lib/api';
import type { ChannelAccount } from '../../types/contracts';
import { ChannelSetupWizard } from './ChannelSetupWizard';
import { Skeleton } from '../ui/Skeleton';
import { EmptyState } from '../ui/EmptyState';
import { useLocaleText } from '../../i18n/I18nProvider';

export function ConnectionsSection() {
  const [accounts, setAccounts] = useState<ChannelAccount[]>([]);
  const [loading, setLoading] = useState(true);
  const [showWizard, setShowWizard] = useState(false);

  const text = useLocaleText({
    zh: {
      sectionTitle: '连接',
      sectionDesc: '管理外部渠道账号（如 Telegram Bot）。',
      emptyTitle: '尚未绑定任何渠道账号',
      emptyDesc: '绑定后可通过外部渠道与 Koda 交互。',
      unknown: '未知',
      addChannel: '+ 绑定新渠道',
    },
    en: {
      sectionTitle: 'Connections',
      sectionDesc: 'Manage external channel accounts (e.g. Telegram Bot).',
      emptyTitle: 'No channels connected yet',
      emptyDesc: 'Connect a channel to interact with Koda from external apps.',
      unknown: 'Unknown',
      addChannel: '+ Add channel',
    },
  });

  const loadAccounts = useCallback(() => {
    setLoading(true);
    fetchChannelAccounts()
      .then(r => setAccounts(Array.isArray(r) ? r : []))
      .catch(() => setAccounts([]))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { loadAccounts(); }, [loadAccounts]);

  return (
    <div className="settings-section" data-testid="settings-connections-section">
      <h2 className="settings-section-title">{text.sectionTitle}</h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

      {loading ? (
        <Skeleton height={40} count={2} />
      ) : accounts.length === 0 ? (
        <EmptyState
          icon={<Plug size={24} strokeWidth={1.5} />}
          title={text.emptyTitle}
          description={text.emptyDesc}
        />
      ) : (
        <div className="settings-accounts-list">
          {accounts.map(acc => (
            <div key={acc.id} className="settings-account-row">
              <span className="settings-account-name">{acc.displayName}</span>
              <span className="settings-account-kind">{acc.connectorKind}</span>
              <span className={`settings-account-state settings-account-state--${acc.state?.toLowerCase() ?? 'unknown'}`}>
                {acc.state ?? text.unknown}
              </span>
            </div>
          ))}
        </div>
      )}

      {!showWizard && (
        <button
          type="button"
          className="settings-btn settings-btn--secondary"
          onClick={() => setShowWizard(true)}
        >
          {text.addChannel}
        </button>
      )}

      {showWizard && (
        <ChannelSetupWizard
          onComplete={() => { setShowWizard(false); loadAccounts(); }}
          onDismiss={() => setShowWizard(false)}
        />
      )}
    </div>
  );
}
