/**
 * KodaClaw — DOM Snapshot Script
 *
 * 通过 Page.addScriptToEvaluateOnNewDocument 注入，
 * 在页面加载时定义 window.__kodaclaw_snapshot 函数。
 *
 * background.js 通过 Runtime.evaluate 调用：
 *   window.__kodaclaw_snapshot()
 *   window.__kodaclaw_snapshot({ selector: '#main' })
 *
 * 返回结构化 DOM 快照，包含：
 *   - 可交互元素列表（带 index，供 click/type 使用）
 *   - 页面文本摘要
 *   - 基础元信息
 */
(function () {
  'use strict';

  /**
   * 判断元素是否可见（粗略判断）
   * @param {Element} el
   * @returns {boolean}
   */
  function isVisible(el) {
    if (!el) return false;
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden') return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  }

  /**
   * 获取元素的简洁描述文本
   * @param {Element} el
   * @returns {string}
   */
  function getElementLabel(el) {
    return (
      el.getAttribute('aria-label') ||
      el.getAttribute('placeholder') ||
      el.getAttribute('title') ||
      el.getAttribute('name') ||
      el.getAttribute('id') ||
      (el.textContent || '').trim().slice(0, 80) ||
      el.tagName.toLowerCase()
    );
  }

  /**
   * 构建 DOM 快照
   * @param {string=} selector  可选 CSS 选择器，限定范围
   * @returns {object}
   */
  function buildSnapshot(selector) {
    const root = selector ? document.querySelector(selector) : document.body;
    if (!root) {
      return {
        url: location.href,
        title: document.title,
        error: selector ? `selector 未匹配：${selector}` : '无 body 元素',
        elements: [],
        text: '',
      };
    }

    // ── 收集可交互元素 ──────────────────────────────────────
    const interactiveTags = ['a', 'button', 'input', 'select', 'textarea', 'label'];
    const roleInteractive = ['button', 'link', 'checkbox', 'radio', 'textbox', 'combobox', 'menuitem', 'tab'];

    const allEls = root.querySelectorAll(
      interactiveTags.join(',') + ',[role],[tabindex],[onclick]'
    );

    const elements = [];
    let index = 0;

    allEls.forEach(el => {
      if (!isVisible(el)) return;

      const tag = el.tagName.toLowerCase();
      const role = el.getAttribute('role') || '';
      const isInteractive =
        interactiveTags.includes(tag) ||
        roleInteractive.includes(role) ||
        el.hasAttribute('onclick') ||
        (el.hasAttribute('tabindex') && el.getAttribute('tabindex') !== '-1');

      if (!isInteractive) return;

      const entry = {
        index,
        tag,
        role: role || undefined,
        type: el.getAttribute('type') || undefined,
        label: getElementLabel(el),
        value: (el.value !== undefined) ? el.value : undefined,
        href: el.getAttribute('href') || undefined,
        disabled: el.disabled || el.getAttribute('aria-disabled') === 'true' || undefined,
        checked: (el.type === 'checkbox' || el.type === 'radio') ? el.checked : undefined,
      };

      // 去掉 undefined 字段
      Object.keys(entry).forEach(k => entry[k] === undefined && delete entry[k]);

      elements.push(entry);
      el.setAttribute('data-kc-index', String(index));
      index++;
    });

    // ── 提取页面主文本（去重、截断）──────────────────────────
    const textContent = (root.innerText || root.textContent || '')
      .replace(/\s{3,}/g, '\n\n')
      .trim()
      .slice(0, 8000);

    return {
      url: location.href,
      title: document.title,
      elements,
      text: textContent,
      elementCount: elements.length,
      snapshotAt: Date.now(),
    };
  }

  window.__kodaclaw_snapshot = function (options) {
    const selector = options && options.selector ? options.selector : null;
    try {
      return buildSnapshot(selector);
    } catch (err) {
      return {
        url: location.href,
        title: document.title,
        error: err.message,
        elements: [],
        text: '',
      };
    }
  };
})();
