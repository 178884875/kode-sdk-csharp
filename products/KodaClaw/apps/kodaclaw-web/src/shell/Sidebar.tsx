import type { ReactNode } from 'react';
import {
  MessageSquare, Inbox, LayoutDashboard, Radio, Zap,
  Cpu, Puzzle, Search, Lightbulb, Settings, PenSquare, Activity, Network,
} from 'lucide-react';
import type { MainDesk } from '../shell-shared/types';
import { useSystemEvents } from '../hooks/useSystemEvents';
import { useDiagnosticsHealth } from '../hooks/useDiagnosticsHealth';
import { ThemeToggle } from '../components/ThemeToggle';

type HealthTone = 'healthy' | 'warning' | 'error' | 'unknown';

type SidebarProps = {
  desks: Array<{ id: MainDesk; label: string }>;
  activeDesk: MainDesk;
  healthTone: HealthTone;
  onDeskChange: (desk: MainDesk) => void;
  onRotateSession?: () => void;
};

type NavGroup = {
  label?: string;
  items: MainDesk[];
};

const STROKE = 1.75;
const ICON_SIZE = 16;

const DESK_ICONS: Record<MainDesk, ReactNode> = {
  chat:        <MessageSquare size={ICON_SIZE} strokeWidth={STROKE} />,
  inbox:       <Inbox size={ICON_SIZE} strokeWidth={STROKE} />,
  canvas:      <LayoutDashboard size={ICON_SIZE} strokeWidth={STROKE} />,
  channels:    <Radio size={ICON_SIZE} strokeWidth={STROKE} />,
  automations: <Zap size={ICON_SIZE} strokeWidth={STROKE} />,
  models:      <Cpu size={ICON_SIZE} strokeWidth={STROKE} />,
  plugins:     <Puzzle size={ICON_SIZE} strokeWidth={STROKE} />,
  sessions:    <Search size={ICON_SIZE} strokeWidth={STROKE} />,
  skills:      <Lightbulb size={ICON_SIZE} strokeWidth={STROKE} />,
  settings:    <Settings size={ICON_SIZE} strokeWidth={STROKE} />,
  mcpServers:  <Network size={ICON_SIZE} strokeWidth={STROKE} />,
  diagnostics: <Activity size={ICON_SIZE} strokeWidth={STROKE} />,
};

const NAV_GROUPS: NavGroup[] = [
  { items: ['chat', 'inbox', 'canvas'] },
  { label: '渠道与自动化', items: ['channels', 'automations'] },
  { label: '能力层', items: ['models', 'plugins', 'skills', 'mcpServers'] },
  { label: '工具', items: ['sessions', 'diagnostics'] },
  { label: '', items: ['settings'] },
];

const STATUS_LABELS: Record<HealthTone, string> = {
  healthy: '运行正常',
  warning: '降级',
  error:   '异常',
  unknown: '未知',
};

function formatBadge(count: number): string {
  if (count <= 0) return '';
  return count > 99 ? '99+' : String(count);
}

export function Sidebar({
  desks,
  activeDesk,
  healthTone,
  onDeskChange,
  onRotateSession,
}: SidebarProps) {
  const { inboxUnreadCount } = useSystemEvents(true);
  const { errorCount: diagErrorCount, warningCount: diagWarningCount } = useDiagnosticsHealth();
  const inboxBadge = formatBadge(inboxUnreadCount);
  const diagBadge = formatBadge(diagErrorCount);
  const deskMap = Object.fromEntries(desks.map(d => [d.id, d])) as Record<MainDesk, { id: MainDesk; label: string }>;

  return (
    <aside className="kc-sidebar" data-testid="kc-sidebar">
      <div className="kc-sidebar__header">
        <div className="kc-sidebar__logo">KC</div>
        <span className="kc-sidebar__app-name">KodaClaw</span>
      </div>

      <button
        type="button"
        className="kc-sidebar__new-chat"
        data-testid="new-session-btn"
        onClick={onRotateSession}
      >
        <PenSquare size={14} strokeWidth={STROKE} aria-hidden="true" />
        新对话
      </button>

      <nav className="kc-sidebar__nav" aria-label="导航">
        {NAV_GROUPS.map((group, gi) => (
          <div key={gi} className="kc-sidebar__group">
            {group.label && (
              <div className="kc-sidebar__group-label">{group.label}</div>
            )}
            {group.items.map(deskId => {
              const desk = deskMap[deskId];
              if (!desk) return null;
              const isActive = deskId === activeDesk;
              const badge = deskId === 'inbox' ? inboxBadge : deskId === 'diagnostics' ? diagBadge : '';
              const badgeClass = deskId === 'diagnostics'
                ? 'kc-sidebar__item-badge kc-sidebar__item-badge--error'
                : 'kc-sidebar__item-badge';
              const badgeLabel = deskId === 'inbox'
                ? `${inboxUnreadCount} 条未读`
                : `${diagErrorCount} 个近期错误`;
              return (
                <button
                  key={deskId}
                  type="button"
                  className={`kc-sidebar__item ${isActive ? 'is-active' : ''}`}
                  data-testid={`desk-tab-${deskId}`}
                  aria-pressed={isActive}
                  onClick={() => onDeskChange(deskId)}
                >
                  <span className="kc-sidebar__item-icon" aria-hidden="true">
                    {DESK_ICONS[deskId]}
                  </span>
                  <span className="kc-sidebar__item-label">{desk.label}</span>
                  {badge && (
                    <span
                      className={badgeClass}
                      data-testid={deskId === 'inbox' ? 'inbox-badge' : 'diag-badge'}
                      aria-label={badgeLabel}
                    >
                      {badge}
                    </span>
                  )}
                </button>
              );
            })}
          </div>
        ))}
      </nav>

      <div className="kc-sidebar__footer">
        <Activity size={12} strokeWidth={STROKE} aria-hidden="true" className={`kc-sidebar__status-icon kc-sidebar__status-icon--${healthTone}`} />
        {diagErrorCount > 0 ? (
          <button
            type="button"
            className="kc-sidebar__status-text kc-sidebar__status-text--error"
            onClick={() => onDeskChange('diagnostics')}
          >
            {diagErrorCount} 个近期错误
          </button>
        ) : diagWarningCount > 0 ? (
          <span className="kc-sidebar__status-text">
            {STATUS_LABELS[healthTone]}
            <span className="kc-sidebar__status-warn"> · {diagWarningCount} 警告</span>
          </span>
        ) : (
          <span className="kc-sidebar__status-text">{STATUS_LABELS[healthTone]}</span>
        )}
        <ThemeToggle />
      </div>
    </aside>
  );
}
