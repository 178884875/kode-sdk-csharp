using System.Text.RegularExpressions;
using KodaClaw.ChannelHub.Connectors.Feishu.Models;

namespace KodaClaw.ChannelHub.Connectors.Feishu;

/// <summary>
/// 将常见 Markdown 语法转换为飞书 Post 富文本元素列表。
/// 仅支持最常用的元素，不追求 100% 兼容。
/// </summary>
internal static class MarkdownToFeishuPostConverter
{
    private static readonly Regex CodeBlockRegex = new(
        @"```(\w*)\n?([\s\S]*?)```",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

    private static readonly Regex InlineCodeRegex = new(
        @"`([^`]+)`",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    private static readonly Regex LinkRegex = new(
        @"\[([^\]]+)\]\(([^)]+)\)",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    private static readonly Regex BoldRegex = new(
        @"\*\*([^*]+)\*\*",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    private static readonly Regex ItalicRegex = new(
        @"\*([^*]+)\*",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    private static readonly Regex HeadingRegex = new(
        @"^(#{1,6})\s+(.+)$",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    public static List<FeishuPostContent> Convert(string markdown, string title = "通知")
    {
        var result = new List<FeishuPostContent>();

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return result;
        }

        // 先处理代码块（多行），避免与行内处理冲突
        var codeBlockPlaceholders = new Dictionary<string, FeishuPostContent>();
        var processed = CodeBlockRegex.Replace(markdown, match =>
        {
            var lang = match.Groups[1].Value.Trim();
            var code = match.Groups[2].Value.TrimEnd('\n');
            var placeholder = $"\x01CODEBLOCK{codeBlockPlaceholders.Count}\x01";
            codeBlockPlaceholders[placeholder] = new FeishuPostContent
            {
                Tag = "code",
                Language = string.IsNullOrEmpty(lang) ? null : lang,
                Text = code,
            };
            return placeholder;
        });

        // 按行处理
        var lines = processed.Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine;

            // 检查是否是代码块占位符（独占一行）
            if (codeBlockPlaceholders.TryGetValue(line.Trim(), out var codeElem))
            {
                result.Add(codeElem);
                continue;
            }

            // 检查是否包含代码块占位符（嵌在行中）
            foreach (var kv in codeBlockPlaceholders)
            {
                if (line.Contains(kv.Key, StringComparison.Ordinal))
                {
                    result.Add(kv.Value);
                    line = line.Replace(kv.Key, string.Empty, StringComparison.Ordinal).Trim();
                    if (string.IsNullOrEmpty(line)) goto NextLine;
                }
            }

            // 标题行
            var headingMatch = HeadingRegex.Match(line);
            if (headingMatch.Success)
            {
                var headingText = headingMatch.Groups[2].Value.Trim();
                result.Add(new FeishuPostContent
                {
                    Tag = "text",
                    Text = headingText,
                });
                goto NextLine;
            }

            // 行内元素解析（链接、粗体、斜体、行内代码、普通文本）
            ParseInline(line, result);

            NextLine:;
        }

        return result;
    }

    private static void ParseInline(string line, List<FeishuPostContent> result)
    {
        if (string.IsNullOrEmpty(line))
        {
            result.Add(new FeishuPostContent { Tag = "text", Text = "\n" });
            return;
        }

        var pos = 0;

        while (pos < line.Length)
        {
            // 尝试匹配行内代码
            var inlineCode = InlineCodeRegex.Match(line, pos);
            // 尝试匹配链接
            var link = LinkRegex.Match(line, pos);
            // 尝试匹配粗体
            var bold = BoldRegex.Match(line, pos);
            // 尝试匹配斜体
            var italic = ItalicRegex.Match(line, pos);

            // 找最近的匹配
            var nextMatch = GetEarliestMatch(inlineCode, link, bold, italic);

            if (nextMatch is null || !nextMatch.Success)
            {
                // 剩余文本全部作为普通文本
                var remaining = line[pos..];
                if (!string.IsNullOrEmpty(remaining))
                {
                    result.Add(new FeishuPostContent { Tag = "text", Text = remaining });
                }
                break;
            }

            // 输出匹配前的普通文本
            if (nextMatch.Index > pos)
            {
                var before = line[pos..nextMatch.Index];
                if (!string.IsNullOrEmpty(before))
                {
                    result.Add(new FeishuPostContent { Tag = "text", Text = before });
                }
            }

            // 处理匹配内容
            if (nextMatch == inlineCode)
            {
                result.Add(new FeishuPostContent { Tag = "code", Text = nextMatch.Groups[1].Value });
            }
            else if (nextMatch == link)
            {
                result.Add(new FeishuPostContent
                {
                    Tag = "a",
                    Text = nextMatch.Groups[1].Value,
                    Href = nextMatch.Groups[2].Value,
                });
            }
            else if (nextMatch == bold)
            {
                result.Add(new FeishuPostContent { Tag = "text", Text = nextMatch.Groups[1].Value });
            }
            else if (nextMatch == italic)
            {
                result.Add(new FeishuPostContent { Tag = "text", Text = nextMatch.Groups[1].Value });
            }

            pos = nextMatch.Index + nextMatch.Length;
        }
    }

    private static Match? GetEarliestMatch(params Match[] matches)
    {
        Match? earliest = null;
        foreach (var m in matches)
        {
            if (!m.Success) continue;
            if (earliest is null || m.Index < earliest.Index)
            {
                earliest = m;
            }
        }
        return earliest;
    }
}
