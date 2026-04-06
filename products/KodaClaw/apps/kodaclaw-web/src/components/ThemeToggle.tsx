import { useState, useEffect } from 'react';
import { Sun, Monitor, Moon } from 'lucide-react';
import { fetchSettings, saveSettings } from '../lib/api';
import type { ThemeMode } from '../types/contracts';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../lib/queryKeys';

function readThemeFromDom(): ThemeMode {
  const t = document.documentElement.dataset.theme;
  if (t === 'dark') return 'Dark';
  if (t === 'light') return 'Light';
  return 'System';
}

function applyTheme(mode: ThemeMode) {
  const el = document.documentElement;
  if (mode === 'Light') el.dataset.theme = 'light';
  else if (mode === 'Dark') el.dataset.theme = 'dark';
  else delete el.dataset.theme;
}

const SEGMENTS: { mode: ThemeMode; icon: React.ReactNode; label: string }[] = [
  { mode: 'Light',  icon: <Sun     size={11} strokeWidth={2} />, label: '浅色' },
  { mode: 'System', icon: <Monitor size={11} strokeWidth={2} />, label: '跟随系统' },
  { mode: 'Dark',   icon: <Moon    size={11} strokeWidth={2} />, label: '深色' },
];

export function ThemeToggle() {
  const queryClient = useQueryClient();
  const { data: settingsData } = useQuery({ queryKey: queryKeys.settings, queryFn: () => fetchSettings() });
  const [current, setCurrent] = useState<ThemeMode>(() => readThemeFromDom());

  // 与 AppearanceSection 保持一致：从后端确认主题
  useEffect(() => {
    if (settingsData) setCurrent(settingsData.theme);
  }, [settingsData]);

  function handleChange(mode: ThemeMode) {
    setCurrent(mode);
    applyTheme(mode);
    // 后台 merge-save，与 AppearanceSection 同一持久化路径
    if (settingsData) {
      saveSettings({ ...settingsData, theme: mode })
        .then(() => queryClient.invalidateQueries({ queryKey: queryKeys.settings }))
        .catch(() => {});
    }
  }

  return (
    <div className="theme-toggle" role="group" aria-label="切换主题">
      {SEGMENTS.map(seg => (
        <button
          key={seg.mode}
          type="button"
          className={`theme-toggle__btn${current === seg.mode ? ' is-active' : ''}`}
          title={seg.label}
          aria-pressed={current === seg.mode}
          onClick={() => handleChange(seg.mode)}
        >
          {seg.icon}
        </button>
      ))}
    </div>
  );
}
