/**
 * KodaClaw — Data Extract Helpers
 *
 * Injected into the page so BrowserHub can run high-level structured extraction
 * without forcing the model to hand-write DOM scripts for common result pages.
 */
(function () {
  'use strict';

  const extractMarker = Symbol.for('kodaclaw.dataExtract');
  if (window[extractMarker]) {
    return;
  }

  Object.defineProperty(window, extractMarker, {
    value: true,
    configurable: false,
    enumerable: false,
    writable: false,
  });

  const MAX_LIMIT = 100;
  const DEFAULT_LINK_LIMIT = 20;
  const DEFAULT_RESULT_LIMIT = 10;

  function clampLimit(limit, fallback) {
    const numeric = Number(limit);
    if (!Number.isFinite(numeric) || numeric <= 0) {
      return fallback;
    }

    return Math.min(Math.trunc(numeric), MAX_LIMIT);
  }

  function normalizeText(value) {
    return (value || '').replace(/\s+/g, ' ').trim();
  }

  function toAbsoluteUrl(rawHref) {
    if (!rawHref) {
      return null;
    }

    try {
      return new URL(rawHref, location.href).toString();
    } catch (_) {
      return null;
    }
  }

  function isHttpUrl(url) {
    return typeof url === 'string' && (url.startsWith('http://') || url.startsWith('https://'));
  }

  function getRoot(selector) {
    if (!selector) {
      return document;
    }

    return document.querySelector(selector);
  }

  function safeQuery(root, selector) {
    if (!root || !selector) {
      return [];
    }

    try {
      return Array.from(root.querySelectorAll(selector));
    } catch (_) {
      return [];
    }
  }

  function firstMatch(root, selectors) {
    for (const selector of selectors) {
      if (!selector) {
        continue;
      }

      try {
        const match = root.querySelector(selector);
        if (match) {
          return match;
        }
      } catch (_) {}
    }

    return null;
  }

  function pickText(root, selectors) {
    const node = firstMatch(root, selectors);
    return node ? normalizeText(node.innerText || node.textContent || '') : '';
  }

  function getLinkLabel(anchor) {
    return normalizeText(
      anchor.innerText ||
      anchor.textContent ||
      anchor.getAttribute('aria-label') ||
      anchor.getAttribute('title') ||
      ''
    );
  }

  function buildLinkEntry(anchor) {
    const href = toAbsoluteUrl(anchor.href || anchor.getAttribute('href'));
    if (!isHttpUrl(href)) {
      return null;
    }

    const text = getLinkLabel(anchor);
    const title = normalizeText(anchor.getAttribute('title') || '');
    const host = (() => {
      try {
        return new URL(href).hostname;
      } catch (_) {
        return '';
      }
    })();

    return {
      text: text || title || host,
      href,
      title: title || undefined,
      host,
      sameOrigin: host === location.hostname,
    };
  }

  function dedupeBy(items, keySelector) {
    const seen = new Set();
    return items.filter(item => {
      const key = keySelector(item);
      if (!key || seen.has(key)) {
        return false;
      }

      seen.add(key);
      return true;
    });
  }

  function extractLinks(options) {
    const selector = options && options.selector ? options.selector : null;
    const linkSelector = options && options.linkSelector ? options.linkSelector : 'a[href]';
    const limit = clampLimit(options && options.limit, DEFAULT_LINK_LIMIT);
    const sameOriginOnly = !!(options && options.sameOriginOnly);
    const root = getRoot(selector);

    if (!root) {
      return {
        kind: 'link_extract',
        url: location.href,
        title: document.title,
        selector: selector || 'document',
        linkSelector,
        totalMatches: 0,
        returnedCount: 0,
        sameOriginOnly,
        links: [],
        error: `selector 未匹配：${selector}`,
      };
    }

    const rawLinks = safeQuery(root, linkSelector)
      .map(buildLinkEntry)
      .filter(Boolean)
      .filter(link => !sameOriginOnly || link.sameOrigin);

    const dedupedLinks = dedupeBy(rawLinks, link => link.href);
    const limitedLinks = dedupedLinks.slice(0, limit);
    const note = dedupedLinks.length > limit
      ? `仅返回前 ${limit} 条链接；可用 selector 或 linkSelector 缩小范围。`
      : undefined;

    return {
      kind: 'link_extract',
      url: location.href,
      title: document.title,
      selector: selector || 'document',
      linkSelector,
      totalMatches: dedupedLinks.length,
      returnedCount: limitedLinks.length,
      sameOriginOnly,
      links: limitedLinks,
      note,
    };
  }

  function genericResultFromContainer(container, config) {
    const title = pickText(container, [config.titleSelector, 'h1', 'h2', 'h3', 'a[href]']);
    const linkNode = firstMatch(container, [config.linkSelector, 'a[href]']);
    const url = linkNode ? toAbsoluteUrl(linkNode.href || linkNode.getAttribute('href')) : null;
    const snippet = pickText(container, [config.snippetSelector, 'p', 'div']);

    if (!title || !isHttpUrl(url)) {
      return null;
    }

    const host = (() => {
      try {
        return new URL(url).hostname;
      } catch (_) {
        return '';
      }
    })();

    return {
      title,
      url,
      snippet: snippet && snippet !== title ? snippet : undefined,
      host,
      source: host,
    };
  }

  function extractResultsWithConfig(root, config, limit, sameOriginOnly) {
    const items = safeQuery(root, config.itemSelector);
    const rawResults = items
      .map(item => genericResultFromContainer(item, config))
      .filter(Boolean)
      .filter(result => !sameOriginOnly || result.host === location.hostname);

    const deduped = dedupeBy(rawResults, result => `${result.url}|${result.title}`);
    return {
      config,
      totalMatches: deduped.length,
      results: deduped.slice(0, limit).map((result, index) => ({
        index,
        title: result.title,
        url: result.url,
        snippet: result.snippet,
        source: result.source,
      })),
    };
  }

  function detectSearchConfig(root, requestedStrategy) {
    const host = location.hostname;
    const strategy = requestedStrategy && requestedStrategy !== 'auto'
      ? requestedStrategy
      : (function () {
          if (/(\.|^)google\./i.test(host)) return 'google';
          if (/(\.|^)bing\.com$/i.test(host)) return 'bing';
          if (/(\.|^)duckduckgo\.com$/i.test(host)) return 'duckduckgo';
          if (/(\.|^)github\.com$/i.test(host) && location.pathname.includes('/search')) return 'github';
          return 'generic';
        })();

    switch (strategy) {
      case 'google':
        return {
          strategy,
          selector: '#search',
          itemSelector: 'div.g, div[data-snc], div[data-hveid]',
          titleSelector: 'h3',
          linkSelector: 'a[href]',
          snippetSelector: '.VwiC3b, .yXK7lf, .aCOpRe, .MUxGbd',
        };
      case 'bing':
        return {
          strategy,
          selector: '#b_results',
          itemSelector: 'li.b_algo',
          titleSelector: 'h2',
          linkSelector: 'h2 a[href]',
          snippetSelector: '.b_caption p',
        };
      case 'duckduckgo':
        return {
          strategy,
          selector: '[data-testid="mainline"], main',
          itemSelector: '[data-testid="result"], article',
          titleSelector: 'h2',
          linkSelector: 'a[data-testid="result-title-a"], h2 a[href], a[href]',
          snippetSelector: '[data-result="snippet"], [data-testid="result-snippet"], .kY2IgmnCmOGjharHErah',
        };
      case 'github':
        return {
          strategy,
          selector: 'main',
          itemSelector: '[data-testid="results-list"] > div, main .search-title',
          titleSelector: 'a.v-align-middle, .search-title a',
          linkSelector: 'a.v-align-middle, .search-title a',
          snippetSelector: '.search-match, p.mb-1, .prc-Text-Text-0ima0',
        };
      default:
        return {
          strategy: 'generic',
          selector: root === document ? 'document' : null,
          itemSelector: 'main article, main li, article, [role="article"], .result, .search-result, li',
          titleSelector: 'h1, h2, h3, h4, a[href]',
          linkSelector: 'a[href]',
          snippetSelector: 'p, .snippet, .description',
        };
    }
  }

  function extractGenericFallback(root, limit, sameOriginOnly) {
    const anchors = safeQuery(root, 'a[href]')
      .map(buildLinkEntry)
      .filter(Boolean)
      .filter(link => !!link.text && (!sameOriginOnly || link.sameOrigin));

    const deduped = dedupeBy(anchors, link => link.href);
    return deduped.slice(0, limit).map((link, index) => ({
      index,
      title: link.text,
      url: link.href,
      source: link.host,
    }));
  }

  function extractResults(options) {
    const requestedSelector = options && options.selector ? options.selector : null;
    const requestedStrategy = options && options.strategy ? String(options.strategy).toLowerCase() : 'auto';
    const limit = clampLimit(options && options.limit, DEFAULT_RESULT_LIMIT);
    const sameOriginOnly = !!(options && options.sameOriginOnly);
    const root = getRoot(requestedSelector);

    if (!root) {
      return {
        kind: 'result_extract',
        url: location.href,
        title: document.title,
        strategy: requestedStrategy,
        selector: requestedSelector || 'document',
        totalMatches: 0,
        returnedCount: 0,
        sameOriginOnly,
        results: [],
        error: `selector 未匹配：${requestedSelector}`,
      };
    }

    const customConfig = options && options.itemSelector
      ? {
          strategy: requestedStrategy === 'auto' ? 'custom' : requestedStrategy,
          selector: requestedSelector || 'document',
          itemSelector: options.itemSelector,
          titleSelector: options.titleSelector || 'h3, h2, a[href]',
          linkSelector: options.linkSelector || 'a[href]',
          snippetSelector: options.snippetSelector || 'p',
        }
      : detectSearchConfig(root, requestedStrategy);

    const configuredRoot = customConfig.selector && customConfig.selector !== 'document'
      ? (document.querySelector(customConfig.selector) || root)
      : root;

    const extracted = extractResultsWithConfig(configuredRoot, customConfig, limit, sameOriginOnly);
    const results = extracted.results.length > 0
      ? extracted.results
      : extractGenericFallback(configuredRoot, limit, sameOriginOnly);
    const totalMatches = extracted.results.length > 0 ? extracted.totalMatches : results.length;

    let note;
    if (totalMatches > limit) {
      note = `仅返回前 ${limit} 条结果；可通过 selector/itemSelector 缩小范围。`;
    } else if (results.length === 0) {
      note = '未匹配到明显的结果卡片；可传入自定义 selector/itemSelector/titleSelector/linkSelector/snippetSelector 重试。';
    }

    return {
      kind: 'result_extract',
      url: location.href,
      title: document.title,
      strategy: customConfig.strategy,
      selector: customConfig.selector || requestedSelector || 'document',
      itemSelector: customConfig.itemSelector,
      totalMatches,
      returnedCount: results.length,
      sameOriginOnly,
      results,
      note,
    };
  }

  window.__kodaclaw_extract_links = function (options) {
    return extractLinks(options || {});
  };

  window.__kodaclaw_extract_results = function (options) {
    return extractResults(options || {});
  };
})();
