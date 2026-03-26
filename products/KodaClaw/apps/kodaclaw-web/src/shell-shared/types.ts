export type ShellMode = 'main';
export type MainDesk = 'chat' | 'inbox' | 'sessions' | 'models' | 'automations' | 'channels' | 'plugins' | 'canvas' | 'skills' | 'settings' | 'mcpServers' | 'diagnostics';

export type DeskMeta = {
  id: MainDesk;
  label: string;
  eyebrow: string;
  summary: string;
};
