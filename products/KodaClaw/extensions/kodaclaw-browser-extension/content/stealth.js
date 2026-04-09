/**
 * KodaClaw — Stealth Script（反自动化检测）
 *
 * 通过 Page.addScriptToEvaluateOnNewDocument 注入，
 * 在页面任何脚本执行前运行。
 *
 * 目的：隐藏 Chrome DevTools Protocol 的可检测痕迹，
 * 防止被目标页面识别为自动化控制的浏览器。
 */
(function () {
  'use strict';

  const stealthMarker = Symbol.for('kodaclaw.stealth');

  if (window[stealthMarker]) {
    return;
  }

  Object.defineProperty(window, stealthMarker, {
    value: true,
    configurable: false,
    enumerable: false,
    writable: false,
  });

  // ── 1. 隐藏 navigator.webdriver ──────────────────────────────
  try {
    Object.defineProperty(Navigator.prototype, 'webdriver', {
      get: () => undefined,
      configurable: true,
    });
  } catch (_) {}

  // ── 2. 清除 CDP 注入留下的全局变量 ──────────────────────────
  const cdpMarkers = [
    'cdc_adoQpoasnfa76pfcZLmcfl_Array',
    'cdc_adoQpoasnfa76pfcZLmcfl_Promise',
    'cdc_adoQpoasnfa76pfcZLmcfl_Symbol',
    '__selenium_unwrapped',
    '__webdriver_evaluate',
    '__driver_evaluate',
    '__webdriver_script_function',
    '__webdriver_script_func',
    '__webdriver_script_element',
    '__fxdriver_evaluate',
    '__driver_unwrapped',
    '__webdriver_unwrapped',
    '__fxdriver_unwrapped',
    '__selenium_evaluate',
    '__selenium_script_function',
  ];

  cdpMarkers.forEach(key => {
    try {
      delete window[key];
      Object.defineProperty(window, key, {
        get: () => undefined,
        set: () => {},
        configurable: true,
      });
    } catch (_) {}
  });

  // ── 3. 确保 window.chrome 存在且不泄漏自动化信息 ──────────
  if (!window.chrome) {
    try {
      Object.defineProperty(window, 'chrome', {
        value: {},
        writable: true,
        configurable: true,
      });
    } catch (_) {}
  }

  if (window.chrome && !window.chrome.app) {
    try {
      window.chrome.app = {
        isInstalled: false,
        InstallState: {
          DISABLED: 'disabled',
          INSTALLED: 'installed',
          NOT_INSTALLED: 'not_installed',
        },
        RunningState: {
          CANNOT_RUN: 'cannot_run',
          READY_TO_RUN: 'ready_to_run',
          RUNNING: 'running',
        },
      };
    } catch (_) {}
  }

  if (window.chrome && !window.chrome.runtime) {
    try {
      window.chrome.runtime = {
        connect: undefined,
        sendMessage: undefined,
      };
    } catch (_) {}
  }

  // ── 4. 修复 navigator.permissions（避免 automation 提示）──
  try {
    const origQuery = navigator.permissions && navigator.permissions.query
      ? navigator.permissions.query.bind(navigator.permissions)
      : null;

    if (origQuery) {
      navigator.permissions.query = (parameters) => {
        if (!parameters || !parameters.name) {
          return origQuery(parameters);
        }

        if (parameters.name === 'notifications') {
          const permissionState = typeof Notification !== 'undefined'
            ? Notification.permission
            : 'default';
          return Promise.resolve({
            state: permissionState,
            onchange: null,
          });
        }

        return origQuery(parameters);
      };
    }
  } catch (_) {}

  // ── 5. 伪造 navigator.plugins（空插件列表会被检测）────────
  try {
    const currentPlugins = navigator.plugins;
    if (!currentPlugins || currentPlugins.length === 0) {
      const fakePlugins = {
        length: 3,
        0: { name: 'Chrome PDF Plugin', filename: 'internal-pdf-viewer' },
        1: { name: 'Chrome PDF Viewer', filename: 'mhjfbmdgcfjbbpaeojofohoefgiehjai' },
        2: { name: 'Native Client', filename: 'internal-nacl-plugin' },
        item(index) {
          return this[index] || null;
        },
        namedItem(name) {
          return Object.values(this).find(value => value && value.name === name) || null;
        },
        refresh() {},
      };

      Object.setPrototypeOf(fakePlugins, PluginArray.prototype);
      Object.defineProperty(Navigator.prototype, 'plugins', {
        get: () => fakePlugins,
        configurable: true,
      });
    }
  } catch (_) {}

  // ── 6. 修复 navigator.languages ──────────────────────────
  try {
    const fallbackLanguages = Array.isArray(navigator.languages) && navigator.languages.length > 0
      ? navigator.languages.slice()
      : [navigator.language || 'en-US', 'en'];

    Object.defineProperty(Navigator.prototype, 'languages', {
      get: () => fallbackLanguages,
      configurable: true,
    });
  } catch (_) {}
})();
