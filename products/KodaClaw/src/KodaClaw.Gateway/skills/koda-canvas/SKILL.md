---
name: koda-canvas
description: Canvas artifact 创作指南——Markdown/HTML/Image artifact 类型、canvas_upsert 工具用法、多 section 文档结构
license: built-in
compatibility: KodaClaw 1.x
allowed-tools: canvas_upsert generate_image
metadata:
  kind: builtin-core
  version: "1.1"
  tags: "canvas, artifact, document, image"
---

# KodaClaw Canvas — Artifact 创作指南

## Canvas 是什么

Canvas 是 KodaClaw 的长内容创作工作区，用于生成用户可持久查看的文档、报告、网页或图片。所有 artifact 保存在 CanvasDesk，不受对话上下文长度限制。

## Artifact 类型

| 类型 | kind 值 | 用途 | 渲染方式 |
|------|---------|------|---------|
| Markdown 文档 | `Markdown` | 报告、笔记、文章 | react-markdown 渲染 |
| HTML 页面 | `Html` | 富格式页面、仪表盘 | sandboxed iframe |
| 图片 | `Image` | AI 生成图片 | `<img>` 标签 |

## canvas_upsert 工具用法

```
canvas_upsert(
  title="季度财务报告",
  kind="Markdown",
  content="# Q1 财务报告\n\n## 收入\n..."
)
```

参数说明：
- `title`（必填）：artifact 名称，显示在 CanvasDesk 列表
- `kind`（必填）：`Markdown` | `Html` | `Image`
- `content`（必填）：artifact 的文本内容（Image 类型填 mediaId）

## 多 Section Markdown 文档

复杂报告可分节写入，每次调用 `canvas_upsert` 会覆盖整个 artifact：

```
canvas_upsert(
  title="项目进展报告",
  kind="Markdown",
  content="""
# 项目进展报告

## 本周完成
- 功能 A 开发完毕
- Bug #123 修复

## 下周计划
- 功能 B 开发
- 代码审查

## 风险事项
- 第三方 API 延期可能影响发布
"""
)
```

## HTML Artifact

适合需要交互元素或精确排版的场景：

```
canvas_upsert(
  title="销售数据仪表盘",
  kind="Html",
  content="""
<!DOCTYPE html>
<html>
<head>
  <style>
    body { font-family: sans-serif; padding: 20px; }
    .metric { background: #f0f4ff; border-radius: 8px; padding: 16px; margin: 8px 0; }
  </style>
</head>
<body>
  <h1>销售数据</h1>
  <div class="metric"><strong>本月收入：</strong>¥128,500</div>
  <div class="metric"><strong>新客户：</strong>34 人</div>
</body>
</html>
"""
)
```

## 图片生成

生成图片需要两步：

1. 调用 `generate_image` 生成图片，获取 `mediaId`
2. 调用 `canvas_upsert` 以 `Image` 类型保存到 Canvas

```
# 第一步：生成图片
generate_image(
  prompt="一只可爱的橙色猫咪坐在书桌前，二次元风格",
  size="1024x1024"
)
# → 返回 mediaId: "media-abc123"

# 第二步：保存到 Canvas
canvas_upsert(
  title="AI 生成猫咪",
  kind="Image",
  content="media-abc123"
)
```

## Artifact 命名规范

- 使用描述性名称，便于在 CanvasDesk 查找
- 包含日期的报告建议格式：`2026-03-25 周报`
- 同一主题持续更新时保持标题一致（会覆盖同名 artifact）

## 常见用例

| 场景 | kind | 示例标题 |
|------|------|---------|
| 会议纪要 | `Markdown` | `2026-03-25 产品会议纪要` |
| 数据报告 | `Html` | `Q1 销售数据报告` |
| 项目计划 | `Markdown` | `功能 X 开发计划` |
| 品牌素材 | `Image` | `产品 Logo 方案 1` |
