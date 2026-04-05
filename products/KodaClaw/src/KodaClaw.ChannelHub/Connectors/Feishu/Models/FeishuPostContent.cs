namespace KodaClaw.ChannelHub.Connectors.Feishu.Models;

/// <summary>
/// 飞书 Post 富文本内容元素
/// </summary>
public sealed class FeishuPostContent
{
    public string Tag { get; set; } = "";  // text / a / at / img / code
    public string? Text { get; set; }
    public string? Href { get; set; }
    public int? Level { get; set; }  // heading level 1-6
    public string? Language { get; set; }  // for code blocks
}
