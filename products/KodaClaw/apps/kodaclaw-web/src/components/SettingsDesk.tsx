import { useRef, useState, useEffect } from 'react';
import {
  User, Brain, Zap, Radio, Globe, Shield, RefreshCw, AlertTriangle,
} from 'lucide-react';
import { LocaleToggle } from './LocaleToggle';
import { WorkspaceIdentityEditor } from './settings/WorkspaceIdentityEditor';
import { MemorySection } from './settings/MemorySection';
import { HeartbeatSection } from './settings/HeartbeatSection';
import { ConnectionsSection } from './settings/ConnectionsSection';
import { SystemSection } from './settings/SystemSection';
import { UpdatesSection } from './settings/UpdatesSection';
import { RiskSection } from './settings/RiskSection';
import { useLocaleText } from '../i18n/I18nProvider';

type SectionId = 'identity' | 'memory' | 'heartbeat' | 'connections' | 'preferences' | 'system' | 'updates' | 'risk';

const STROKE = 1.75;
const ICON_SIZE = 16;

export function SettingsDesk() {
  const text = useLocaleText({
    zh: {
      prefsTitle: '偏好',
      prefsDesc: '界面语言等个性化选项。',
      language: '界面语言',
      nav: {
        groupWorkspace: '工作区',
        groupConnect: '连接',
        groupAdmin: '系统管理',
        identity: '身份',
        memory: '记忆',
        heartbeat: '心跳',
        connections: '渠道连接',
        preferences: '偏好',
        system: '系统',
        updates: '更新',
        risk: '风险简报',
      },
    },
    en: {
      prefsTitle: 'Preferences',
      prefsDesc: 'Language and other personalization options.',
      language: 'Interface language',
      nav: {
        groupWorkspace: 'Workspace',
        groupConnect: 'Connections',
        groupAdmin: 'Admin',
        identity: 'Identity',
        memory: 'Memory',
        heartbeat: 'Heartbeat',
        connections: 'Channels',
        preferences: 'Preferences',
        system: 'System',
        updates: 'Updates',
        risk: 'Risk',
      },
    },
  });

  const [activeSection, setActiveSection] = useState<SectionId>('identity');

  const refs: Record<SectionId, React.MutableRefObject<HTMLDivElement | null>> = {
    identity:    useRef<HTMLDivElement | null>(null),
    memory:      useRef<HTMLDivElement | null>(null),
    heartbeat:   useRef<HTMLDivElement | null>(null),
    connections: useRef<HTMLDivElement | null>(null),
    preferences: useRef<HTMLDivElement | null>(null),
    system:      useRef<HTMLDivElement | null>(null),
    updates:     useRef<HTMLDivElement | null>(null),
    risk:        useRef<HTMLDivElement | null>(null),
  };

  const contentRef = useRef<HTMLDivElement>(null);

  // Track active section via scroll position
  useEffect(() => {
    const container = contentRef.current;
    if (!container || typeof IntersectionObserver === 'undefined') return;

    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            const id = entry.target.getAttribute('data-section') as SectionId | null;
            if (id) setActiveSection(id);
          }
        }
      },
      { root: container, threshold: 0.3 },
    );

    for (const [, ref] of Object.entries(refs)) {
      if (ref.current) observer.observe(ref.current);
    }

    return () => observer.disconnect();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function scrollTo(id: SectionId) {
    setActiveSection(id);
    refs[id].current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  type NavGroup = {
    groupLabel: string;
    items: Array<{ id: SectionId; icon: React.ReactNode; label: string }>;
  };

  const navGroups: NavGroup[] = [
    {
      groupLabel: text.nav.groupWorkspace,
      items: [
        { id: 'identity',    icon: <User size={ICON_SIZE} strokeWidth={STROKE} />,  label: text.nav.identity },
        { id: 'memory',      icon: <Brain size={ICON_SIZE} strokeWidth={STROKE} />, label: text.nav.memory },
        { id: 'heartbeat',   icon: <Zap size={ICON_SIZE} strokeWidth={STROKE} />,   label: text.nav.heartbeat },
      ],
    },
    {
      groupLabel: text.nav.groupConnect,
      items: [
        { id: 'connections', icon: <Radio size={ICON_SIZE} strokeWidth={STROKE} />, label: text.nav.connections },
        { id: 'preferences', icon: <Globe size={ICON_SIZE} strokeWidth={STROKE} />, label: text.nav.preferences },
      ],
    },
    {
      groupLabel: text.nav.groupAdmin,
      items: [
        { id: 'system',  icon: <Shield size={ICON_SIZE} strokeWidth={STROKE} />,        label: text.nav.system },
        { id: 'updates', icon: <RefreshCw size={ICON_SIZE} strokeWidth={STROKE} />,     label: text.nav.updates },
        { id: 'risk',    icon: <AlertTriangle size={ICON_SIZE} strokeWidth={STROKE} />, label: text.nav.risk },
      ],
    },
  ];

  return (
    <div className="settings-split" data-testid="settings-desk">
      {/* Left nav rail */}
      <nav className="settings-nav" aria-label="Settings sections">
        {navGroups.map((group) => (
          <div key={group.groupLabel} className="settings-nav__group">
            <span className="settings-nav__group-label">{group.groupLabel}</span>
            {group.items.map(({ id, icon, label }) => (
              <button
                key={id}
                type="button"
                className={`settings-nav__item ${activeSection === id ? 'settings-nav__item--active' : ''}`}
                onClick={() => scrollTo(id)}
                aria-current={activeSection === id ? 'true' : undefined}
              >
                <span className="settings-nav__icon">{icon}</span>
                <span className="settings-nav__label">{label}</span>
              </button>
            ))}
          </div>
        ))}
      </nav>

      {/* Right scrollable content */}
      <div className="settings-content" ref={contentRef}>
        <div ref={refs.identity} data-section="identity">
          <WorkspaceIdentityEditor />
        </div>

        <div ref={refs.memory} data-section="memory" className="settings-split__anchor">
          <MemorySection />
        </div>

        <div ref={refs.heartbeat} data-section="heartbeat" className="settings-split__anchor">
          <HeartbeatSection />
        </div>

        <div ref={refs.connections} data-section="connections" className="settings-split__anchor">
          <ConnectionsSection />
        </div>

        <div ref={refs.preferences} data-section="preferences" className="settings-split__anchor">
          <div className="settings-section" data-testid="settings-preferences-section">
            <h2 className="settings-section-title">{text.prefsTitle}</h2>
            <p className="settings-section-desc">{text.prefsDesc}</p>
            <div className="settings-pref-row">
              <span className="settings-pref-label">{text.language}</span>
              <LocaleToggle />
            </div>
          </div>
        </div>

        <div ref={refs.system} data-section="system" className="settings-split__anchor">
          <SystemSection />
        </div>

        <div ref={refs.updates} data-section="updates" className="settings-split__anchor">
          <UpdatesSection />
        </div>

        <div ref={refs.risk} data-section="risk" className="settings-split__anchor">
          <RiskSection />
        </div>
      </div>
    </div>
  );
}
