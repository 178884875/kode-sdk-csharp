/**
 * KodaClaw — DOM Snapshot Script
 *
 * 通过 Page.addScriptToEvaluateOnNewDocument 注入，
 * 在页面加载时定义 window.__kodaclaw_snapshot 函数。
 *
 * background.js 通过 Runtime.evaluate 调用：
 *   window.__kodaclaw_snapshot()
 *   window.__kodaclaw_snapshot({ selector: '#main' })
 *   window.__kodaclaw_snapshot({ framePath: ['iframe[name="main"]'] })
 *
 * 返回结构化 DOM 快照，包含：
 *   - 可交互元素列表（带 index，供 click/type 使用）
 *   - 页面文本摘要
 *   - 基础元信息
 */
(function () {
  'use strict';

  const MAX_INTERACTIVE_ELEMENTS = 120;
  const MAX_TEXT_CHARS = 6000;

  function normalizeFramePath(options) {
    if (options && Array.isArray(options.framePath)) {
      const normalized = options.framePath
        .filter(selector => typeof selector === 'string')
        .map(selector => selector.trim())
        .filter(selector => selector.length > 0);
      return normalized.length > 0 ? normalized : [];
    }

    if (options && typeof options.frameSelector === 'string') {
      const selector = options.frameSelector.trim();
      return selector ? [selector] : [];
    }

    return [];
  }

  function resolveFrameContext(framePath) {
    let currentWindow = window;
    let currentDocument = document;

    for (let index = 0; index < framePath.length; index++) {
      const selector = framePath[index];
      const frameEl = currentDocument.querySelector(selector);
      if (!frameEl) {
        throw new Error(`FRAME_NOT_FOUND: iframe selector "${selector}" did not match any iframe.`);
      }

      const tagName = frameEl.tagName ? frameEl.tagName.toLowerCase() : '';
      if (tagName !== 'iframe' && tagName !== 'frame') {
        throw new Error(`FRAME_NOT_IFRAME: selector "${selector}" matched a ${tagName || 'node'}, not an iframe.`);
      }

      let nextWindow;
      try {
        nextWindow = frameEl.contentWindow;
      } catch (_) {
        throw new Error(`FRAME_ACCESS_DENIED: iframe selector "${selector}" is not same-origin.`);
      }

      if (!nextWindow) {
        throw new Error(`FRAME_UNAVAILABLE: iframe selector "${selector}" has no active window.`);
      }

      let nextDocument;
      try {
        nextDocument = nextWindow.document;
      } catch (_) {
        throw new Error(`FRAME_ACCESS_DENIED: iframe selector "${selector}" is not same-origin.`);
      }

      if (!nextDocument) {
        throw new Error(`FRAME_UNAVAILABLE: iframe selector "${selector}" has no active document.`);
      }

      currentWindow = nextWindow;
      currentDocument = nextDocument;
    }

    return {
      window: currentWindow,
      document: currentDocument,
    };
  }

  /**
   * 判断元素是否可见（粗略判断）
   * @param {Window} targetWindow
   * @param {Element} el
   * @returns {boolean}
   */
  function isVisible(targetWindow, el) {
    if (!el) return false;
    const style = targetWindow.getComputedStyle(el);
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
   * @param {Window} targetWindow
   * @param {Document} targetDocument
   * @param {string[]=} framePath
   * @param {string=} selector
   * @returns {object}
   */
  function buildSnapshot(targetWindow, targetDocument, framePath, selector) {
    const root = selector ? targetDocument.querySelector(selector) : targetDocument.body;
    if (!root) {
      return {
        kind: 'dom_snapshot',
        url: targetWindow.location.href,
        title: targetDocument.title,
        error: selector ? `selector 未匹配：${selector}` : '无 body 元素',
        elements: [],
        text: '',
        framePath: framePath.length > 0 ? framePath : undefined,
      };
    }

    const interactiveTags = ['a', 'button', 'input', 'select', 'textarea', 'label'];
    const roleInteractive = ['button', 'link', 'checkbox', 'radio', 'textbox', 'combobox', 'menuitem', 'tab'];

    const allEls = root.querySelectorAll(
      interactiveTags.join(',') + ',[role],[tabindex],[onclick]'
    );

    const elements = [];
    let totalInteractiveElements = 0;

    allEls.forEach(el => {
      if (!isVisible(targetWindow, el)) return;

      const tag = el.tagName.toLowerCase();
      const role = el.getAttribute('role') || '';
      const isInteractive =
        interactiveTags.includes(tag) ||
        roleInteractive.includes(role) ||
        el.hasAttribute('onclick') ||
        (el.hasAttribute('tabindex') && el.getAttribute('tabindex') !== '-1');

      if (!isInteractive) return;

      totalInteractiveElements++;

      if (elements.length >= MAX_INTERACTIVE_ELEMENTS) {
        return;
      }

      const entry = {
        index: elements.length,
        tag,
        role: role || undefined,
        type: el.getAttribute('type') || undefined,
        label: getElementLabel(el),
        value: (el.value !== undefined) ? el.value : undefined,
        href: el.getAttribute('href') || undefined,
        disabled: el.disabled || el.getAttribute('aria-disabled') === 'true' || undefined,
        checked: (el.type === 'checkbox' || el.type === 'radio') ? el.checked : undefined,
      };

      Object.keys(entry).forEach(k => entry[k] === undefined && delete entry[k]);

      elements.push(entry);
      el.setAttribute('data-kc-index', String(entry.index));
    });

    const fullTextContent = (root.innerText || root.textContent || '')
      .replace(/\s{3,}/g, '\n\n')
      .trim();
    const textTruncated = fullTextContent.length > MAX_TEXT_CHARS;
    const textContent = fullTextContent.slice(0, MAX_TEXT_CHARS);
    const elementLimitReached = totalInteractiveElements > elements.length;
    const notes = [];

    if (elementLimitReached) {
      notes.push(`仅返回前 ${MAX_INTERACTIVE_ELEMENTS} 个可交互元素；请用 selector 缩小范围，或改用 evaluate_dom 做定向提取。`);
    }
    if (textTruncated) {
      notes.push(`页面文本已截断为前 ${MAX_TEXT_CHARS} 个字符。`);
    }

    return {
      kind: 'dom_snapshot',
      url: targetWindow.location.href,
      title: targetDocument.title,
      elements,
      text: textContent,
      elementCount: elements.length,
      totalInteractiveElements,
      elementLimitReached,
      textTruncated,
      scope: selector || 'document',
      framePath: framePath.length > 0 ? framePath : undefined,
      note: notes.length > 0 ? notes.join(' ') : undefined,
      snapshotAt: Date.now(),
    };
  }

  window.__kodaclaw_snapshot = function (options) {
    const selector = options && options.selector ? options.selector : null;
    const framePath = normalizeFramePath(options);
    try {
      const context = resolveFrameContext(framePath);
      return buildSnapshot(context.window, context.document, framePath, selector);
    } catch (err) {
      return {
        kind: 'dom_snapshot',
        url: location.href,
        title: document.title,
        error: err.message,
        elements: [],
        text: '',
        framePath: framePath.length > 0 ? framePath : undefined,
      };
    }
  };
})();
