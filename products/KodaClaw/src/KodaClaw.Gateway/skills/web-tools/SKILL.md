---
name: web-tools
description: Local-first web search and reading CLI for AI agents. Zero cost, no API keys. Use this skill whenever the user asks to search the web, find information online, read an article or webpage, extract content from a URL, or convert files (PDF, DOCX, PPTX, XLSX) to Markdown. Trigger on phrases like "search for", "look up", "find information", "read this article", "what does this page say", "search the web", "google this", or any task that needs web information retrieval.
license: community
compatibility: KodaClaw 1.x
allowed-tools: Bash(web-tools web-search:*) Bash(web-tools web-reader:*)
metadata:
  kind: optional
  version: "1.1"
  tags: "web, search, reader, scraping"
---

# web-tools — Local-first web search & reading CLI

Local-first web search and reading tools for AI agents. Zero cost, no API keys, no third-party dependencies.

## When to use

- Need to **search the web** for information → `web-search`
- Need to **read/extract content** from a URL or file → `web-reader`
- User asks "look this up", "find information about", "search for", "read this article/page"

## Prerequisites

首次使用前，先确认 `web-tools` 是否已安装：

```bash
which web-tools || echo "not installed"
```

如未安装，下载预编译二进制（Linux amd64，其他平台见 GitHub Releases）：

```bash
# Linux amd64
curl -L https://github.com/koda-claw/web-tools/releases/latest/download/web-tools-linux-amd64 \
  -o /usr/local/bin/web-tools && chmod +x /usr/local/bin/web-tools

# macOS arm64
# curl -L https://github.com/koda-claw/web-tools/releases/latest/download/web-tools-darwin-arm64 \
#   -o ~/.local/bin/web-tools && chmod +x ~/.local/bin/web-tools
```

可选依赖：
- `markitdown`（`pip install markitdown`）— PDF/DOCX/PPTX/XLSX 文件转换
- SearXNG — 更高吞吐量搜索后端（docker-compose 已内置，默认 `http://searxng:8888`）

---

## web-search

### Usage

```bash
web-tools web-search "<query>" [flags]
```

### Flags

| Flag | 默认 | 说明 |
|------|------|------|
| `--json` | false | JSON 结构化输出 |
| `-n, --limit` | 5 | 结果数量 |
| `--locale` | `auto` | 语言偏好：`zh-CN` / `en-US` / `auto` |
| `--category` | `general` | 分类：`general` / `news` / `images` / `videos` / `files` |
| `--time-range` | `any` | 时间范围：`any` / `day` / `week` / `month` / `year` |
| `--engine` | `auto` | 引擎：`auto` / `duckduckgo` / `searxng` |
| `-o, --output` | stdout | 输出到文件 |

### Common patterns

```bash
# 基础搜索
web-tools web-search "latest AI news"

# 中文搜索，最近一周，3条结果，JSON 输出
web-tools web-search "人工智能最新进展" --locale zh-CN --time-range week --limit 3 --json

# 新闻分类
web-tools web-search "Tesla" --category news --time-range day

# 指定 SearXNG 引擎（容器内可用）
web-tools web-search "query" --engine searxng
```

### Exit codes

| Code | 含义 |
|------|------|
| 0 | 成功 |
| 1 | 通用错误（参数错误、网络超时） |
| 4 | SearXNG 不可用（容器未运行） |

---

## web-reader

### Usage

```bash
web-tools web-reader <input> [flags]
```

`<input>` 可以是 URL（`http://`/`https://`）或本地文件路径，自动检测类型。

### Flags

| Flag | 默认 | 说明 |
|------|------|------|
| `--json` | false | JSON 结构化输出 |
| `--extract` | `main` | 提取模式：`main`（正文）/ `full`（完整页面） |
| `--max-words` | 0 | 限制输出字数（0=不限） |
| `--timeout` | 15 | 请求超时（秒） |
| `--no-cache` | false | 忽略缓存，强制重新获取 |
| `--browser` | false | 通过 agent-browser 强制浏览器渲染 |
| `--format` | `markdown` | 输出格式：`markdown` / `text` / `html` |
| `-o, --output` | stdout | 输出到文件 |

### Common patterns

```bash
# 读取网页文章
web-tools web-reader https://example.com/article

# 限制 100 词快速摘要
web-tools web-reader https://example.com/article --max-words 100

# SPA 页面用浏览器渲染
web-tools web-reader https://some-react-app.com/page --browser

# 转换本地 PDF
web-tools web-reader ./report.pdf

# 转换 Office 文档为 Markdown
web-tools web-reader ./slides.pptx
web-tools web-reader ./data.xlsx
```

---

## Combined workflow

```bash
# Step 1: 搜索
web-tools web-search "Go readability library" --limit 3 --json

# Step 2: 读取 top 结果
web-tools web-reader https://github.com/go-shiori/go-readability
```
