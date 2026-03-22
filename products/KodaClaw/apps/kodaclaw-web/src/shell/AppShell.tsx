import type { ReactNode } from 'react';
import type { MainDesk } from '../shell-shared/types';
import { Sidebar } from './Sidebar';
import './app-shell.css';

type HealthTone = 'healthy' | 'warning' | 'error' | 'unknown';

type AppShellProps = {
  mainDesk: MainDesk;
  desks: Array<{ id: MainDesk; label: string }>;
  onDeskChange: (desk: MainDesk) => void;
  healthTone: HealthTone;
  onRotateSession?: () => void;
  children: ReactNode;
};

export function AppShell({
  mainDesk,
  desks,
  onDeskChange,
  healthTone,
  onRotateSession,
  children,
}: AppShellProps) {
  return (
    <div className="kc-shell" data-testid="kc-shell" data-kc-mode="main">
      <Sidebar
        desks={desks}
        activeDesk={mainDesk}
        healthTone={healthTone}
        onDeskChange={onDeskChange}
        onRotateSession={onRotateSession}
      />
      <main className="kc-main-content" data-testid="kc-main-content">
        {children}
      </main>
    </div>
  );
}
