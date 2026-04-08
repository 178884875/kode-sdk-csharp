/**
 * KodaClaw — Evaluate Sandbox（只读 JS 沙箱）
 *
 * 通过 Page.addScriptToEvaluateOnNewDocument 注入，
 * 定义 window.__kodaclaw_evaluate 函数。
 *
 * background.js 通过 Runtime.evaluate 调用：
 *   window.__kodaclaw_evaluate("document.title")
 *
 * 沙箱策略：
 *   - 在 iframe（sandbox="allow-scripts"）中执行用户脚本
 *   - iframe 无法访问父页面 DOM，无法发起网络请求
 *   - 结果序列化后返回（防止 non-serializable 引用泄漏）
 *   - 执行超时 5 秒
 *
 * 注：沙箱阻止写入父页面，适合只读 observe 场景。
 *     如需访问父页面状态（document.title 等），
 *     沙箱内通过 postMessage 从父页面获取预设数据。
 */
(function () {
  'use strict';

  /**
   * 在隔离沙箱中求值表达式
   * @param {string} script
   * @returns {Promise<any>}
   */
  window.__kodaclaw_evaluate = function (script) {
    return new Promise((resolve, reject) => {
      // 构建沙箱数据（把父页面关键状态传入沙箱）
      const contextData = {
        url: location.href,
        title: document.title,
        readyState: document.readyState,
        bodyText: (document.body ? document.body.innerText : '').slice(0, 4096),
      };

      // 创建隔离 iframe
      const iframe = document.createElement('iframe');
      iframe.style.cssText = 'display:none;width:0;height:0;border:0;';
      iframe.setAttribute('sandbox', 'allow-scripts');
      document.documentElement.appendChild(iframe);

      let settled = false;
      const timeoutId = setTimeout(() => {
        if (!settled) {
          settled = true;
          cleanup();
          reject(new Error('evaluate 超时（5000ms）'));
        }
      }, 5000);

      function cleanup() {
        clearTimeout(timeoutId);
        window.removeEventListener('message', onMessage);
        try { document.documentElement.removeChild(iframe); } catch (_) {}
      }

      function onMessage(ev) {
        if (ev.source !== iframe.contentWindow) return;
        if (settled) return;
        settled = true;
        cleanup();
        const { ok, value, error } = ev.data || {};
        if (ok) {
          resolve(value);
        } else {
          reject(new Error(error || 'evaluate 失败'));
        }
      }

      window.addEventListener('message', onMessage);

      // iframe 内执行脚本并通过 postMessage 返回结果
      const iframeCode = `
        (function() {
          var __ctx = ${JSON.stringify(contextData)};
          try {
            // 在沙箱中提供受限的上下文访问
            var location = { href: __ctx.url };
            var document = {
              title: __ctx.title,
              readyState: __ctx.readyState,
              body: { innerText: __ctx.bodyText }
            };
            var result = (function() { return (${script}); })();
            // 序列化结果（仅支持 JSON-safe 值）
            var serialized = JSON.parse(JSON.stringify(result === undefined ? null : result));
            parent.postMessage({ ok: true, value: serialized }, '*');
          } catch (err) {
            parent.postMessage({ ok: false, error: err.message }, '*');
          }
        })();
      `;

      try {
        const doc = iframe.contentDocument || iframe.contentWindow.document;
        doc.open();
        doc.write('<script>' + iframeCode + '<\/script>');
        doc.close();
      } catch (err) {
        cleanup();
        reject(new Error('沙箱初始化失败：' + err.message));
      }
    });
  };
})();
