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

  // ── 1. 隐藏 navigator.webdriver ──────────────────────────────
  try {
    Object.defineProperty(navigator, 'webdriver', {
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

  if (window.chrome && !window.chrome.runtime) {
    try {
      window.chrome.runtime = {};
    } catch (_) {}
  }

  // ── 4. 修复 navigator.permissions（避免 automation 提示）──
  try {
    const origQuery = navigator.permissions.query.bind(navigator.permissions);
    navigator.permissions.query = (parameters) => {
      if (parameters.name === 'notifications') {
        return Promise.resolve({ state: 'default', onchange: null });
      }
      return origQuery(parameters);
    };
  } catch (_) {}

  // ── 5. 伪造 navigator.plugins（空插件列表会被检测）────────
  try {
    Object.defineProperty(navigator, 'plugins', {
      get: () => {
        const arr = [1, 2, 3, 4, 5];
        arr.__proto__ = PluginArray.prototype;
        return arr;
      },
      configurable: true,
    });
  } catch (_) {}

  // ── 6. 修复 navigator.languages ──────────────────────────
  try {
    Object.defineProperty(navigator, 'languages', {
      get: () => ['zh-CN', 'zh', 'en-US', 'en'],
      configurable: true,
    });
  } catch (_) {}
})();
