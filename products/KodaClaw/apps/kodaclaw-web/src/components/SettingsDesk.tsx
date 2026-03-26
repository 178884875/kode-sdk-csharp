import { useRef, useState, useEffect } from 'react';
import {
  User, Brain, Zap, Radio, Globe, Shield,
  Palette, Bell, SlidersHorizontal, HardDrive, History,
} from 'lucide-react';
import { LocaleToggle } from './LocaleToggle';
import { WorkspaceIdentityEditor } from './settings/WorkspaceIdentityEditor';
import { MemorySection } from './settings/MemorySection';
import { HeartbeatSection } from './settings/HeartbeatSection';
import { BehaviorSection } from './settings/BehaviorSection';
import { SessionConfigSection } from './settings/SessionConfigSection';
import { ConnectionsSection } from './settings/ConnectionsSection';
import { AppearanceSection } from './settings/AppearanceSection';
import { NotificationsSection } from './settings/NotificationsSection';
import { SystemSection } from './settings/SystemSection';
import { StorageSection } from './settings/StorageSection';
import { HistorySection } from './settings/HistorySection';
import { useLocaleText } from '../i18n/I18nProvider';

type SectionId =
  | 'identity' | 'memory'
  | 'heartbeat' | 'behavior' | 'session-config'
  | 'connections'
  | 'appearance' | 'notifications' | 'preferences'
  | 'system' | 'storage' | 'history';

const STROKE = 1.75;
const ICON_SIZE = 16;

export function SettingsDesk() {
  const text = useLocaleText({
    zh: {
      prefsTitle: '语言',
      prefsDesc: '界面显示语言。',
      language: '界面语言',
      nav: {
        groupWorkspace: '工作区',
        groupAutomation: '自动化',
        groupConnect: '连接',
        groupPreferences: '偏好',
        groupAdmin: '系统管理',
        identity: '身份',
        memory: '记忆',
        heartbeat: '自动化规则',
        behavior: '行为控制',
        sessionConfig: '会话参数',
        connections: '渠道连接',
        appearance: '外观',
        notifications: '通知',
        preferences: '语言',
        system: '系统',
        storage: '存储',
        history: '变更历史',
      },
    },
    en: {
      prefsTitle: 'Language',
      prefsDesc: 'Interface display language.',
      language: 'Interface language',
      nav: {
        groupWorkspace: 'Workspace',
        groupAutomation: 'Automation',
        groupConnect: 'Connections',
        groupPreferences: 'Preferences',
        groupAdmin: 'Admin',
        identity: 'Identity',
        memory: 'Memory',
        heartbeat: 'Automation Rules',
        behavior: 'Behavior',
        sessionConfig: 'Session Parameters',
        connections: 'Channels',
        appearance: 'Appearance',
        notifications: 'Notifications',
        preferences: 'Language',
        system: 'System',
        storage: 'Storage',
        history: 'Change History',
      },
    },
  });

  const [activeSection, setActiveSection] = useState<SectionId>('identity');

  const refs: Record<SectionId, React.MutableRefObject<HTMLDivElement | null>> = {
    identity:       useRef<HTMLDivElement | null>(null),
    memory:         useRef<HTMLDivElement | null>(null),
    heartbeat:      useRef<HTMLDivElement | null>(null),
    behavior:       useRef<HTMLDivElement | null>(null),
    'session-config': useRef<HTMLDivElement | null>(null),
    connections:    useRef<HTMLDivElement | null>(null),
    appearance:    useRef<HTMLDivElement | null>(null),
    notifications: useRef<HTMLDivElement | null>(null),
    preferences:   useRef<HTMLDivElement | null>(null),
    system:        useRef<HTMLDivElement | null>(null),
    storage:       useRef<HTMLDivElement | null>(null),
    history:       useRef<HTMLDivElement | null>(null),
  };

  const contentRef = useRef<HTMLDivElement>(null);
  // Suppresses observer updates during programmatic smooth-scroll triggered by nav clicks.
  const suppressObserverRef = useRef(false);
  const suppressTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Track active section via scroll position.
  // Strategy: narrow detection band at the top 30% of the container (rootMargin cuts off the bottom 70%).
  // When multiple sections are simultaneously in the band, pick the topmost one by boundingClientRect.top.
  useEffect(() => {
    const container = contentRef.current;
    if (!container || typeof IntersectionObserver === 'undefined') return;

    const observer = new IntersectionObserver(
      (entries) => {
        // Ignore observer callbacks fired during a programmatic scroll-to-section.
        if (suppressObserverRef.current) return;

        const visible = entries.filter(e => e.isIntersecting);
        if (visible.length === 0) return;

        // Pick the section whose top edge is closest to the top of the container.
        const topEntry = visible.reduce((best, e) =>
          e.boundingClientRect.top < best.boundingClientRect.top ? e : best
        );

        const id = topEntry.target.getAttribute('data-section') as SectionId | null;
        if (id) setActiveSection(id);
      },
      {
        root: container,
        rootMargin: '0px 0px -70% 0px',
        threshold: 0,
      },
    );

    for (const [, ref] of Object.entries(refs)) {
      if (ref.current) observer.observe(ref.current);
    }

    return () => observer.disconnect();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function scrollTo(id: SectionId) {
    setActiveSection(id);
    // Suppress observer during smooth scroll animation (~600 ms).
    suppressObserverRef.current = true;
    if (suppressTimerRef.current) clearTimeout(suppressTimerRef.current);
    suppressTimerRef.current = setTimeout(() => {
      suppressObserverRef.current = false;
    }, 650);
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
        { id: 'identity', icon: <User size={ICON_SIZE} strokeWidth={STROKE} />,  label: text.nav.identity },
        { id: 'memory',   icon: <Brain size={ICON_SIZE} strokeWidth={STROKE} />, label: text.nav.memory },
      ],
    },
    {
      groupLabel: text.nav.groupAutomation,
      items: [
        { id: 'heartbeat',      icon: <Zap size={ICON_SIZE} strokeWidth={STROKE} />,              label: text.nav.heartbeat },
        { id: 'behavior',       icon: <SlidersHorizontal size={ICON_SIZE} strokeWidth={STROKE} />, label: text.nav.behavior },
        { id: 'session-config', icon: <SlidersHorizontal size={ICON_SIZE} strokeWidth={STROKE} />, label: text.nav.sessionConfig },
      ],
    },
    {
      groupLabel: text.nav.groupConnect,
      items: [
        { id: 'connections', icon: <Radio size={ICON_SIZE} strokeWidth={STROKE} />, label: text.nav.connections },
      ],
    },
    {
      groupLabel: text.nav.groupPreferences,
      items: [
        { id: 'appearance',    icon: <Palette size={ICON_SIZE} strokeWidth={STROKE} />, label: text.nav.appearance },
        { id: 'notifications', icon: <Bell size={ICON_SIZE} strokeWidth={STROKE} />,    label: text.nav.notifications },
        { id: 'preferences',   icon: <Globe size={ICON_SIZE} strokeWidth={STROKE} />,   label: text.nav.preferences },
      ],
    },
    {
      groupLabel: text.nav.groupAdmin,
      items: [
        { id: 'storage', icon: <HardDrive size={ICON_SIZE} strokeWidth={STROKE} />, label: text.nav.storage },
        { id: 'history', icon: <History size={ICON_SIZE} strokeWidth={STROKE} />,  label: text.nav.history },
        { id: 'system',  icon: <Shield size={ICON_SIZE} strokeWidth={STROKE} />,   label: text.nav.system },
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

        <div ref={refs.behavior} data-section="behavior" className="settings-split__anchor">
          <BehaviorSection />
        </div>

        <div ref={refs['session-config']} data-section="session-config" className="settings-split__anchor">
          <SessionConfigSection />
        </div>

        <div ref={refs.connections} data-section="connections" className="settings-split__anchor">
          <ConnectionsSection />
        </div>

        <div ref={refs.appearance} data-section="appearance" className="settings-split__anchor">
          <AppearanceSection />
        </div>

        <div ref={refs.notifications} data-section="notifications" className="settings-split__anchor">
          <NotificationsSection />
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

        <div ref={refs.storage} data-section="storage" className="settings-split__anchor">
          <StorageSection />
        </div>

        <div ref={refs.history} data-section="history" className="settings-split__anchor">
          <HistorySection />
        </div>

        <div ref={refs.system} data-section="system" className="settings-split__anchor">
          <SystemSection />
        </div>
      </div>
    </div>
  );
}
