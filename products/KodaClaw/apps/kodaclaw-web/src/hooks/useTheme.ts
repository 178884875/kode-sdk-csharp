import { useEffect } from 'react';
import { fetchSettings } from '../lib/api';
import type { ThemeMode } from '../types/contracts';

function applyTheme(mode: ThemeMode) {
  const el = document.documentElement;
  if (mode === 'Light') {
    el.dataset.theme = 'light';
  } else if (mode === 'Dark') {
    el.dataset.theme = 'dark';
  } else {
    delete el.dataset.theme; // System: 由 prefers-color-scheme 决定
  }
}

export function useTheme() {
  useEffect(() => {
    fetchSettings()
      .then(s => applyTheme(s.theme))
      .catch(() => {}); // 失败时跟随系统（无 data-theme 属性）
  }, []);
}
