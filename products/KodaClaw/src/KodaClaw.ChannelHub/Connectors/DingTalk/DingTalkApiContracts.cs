using System.Text.Json.Serialization;

namespace KodaClaw.ChannelHub.Connectors.DingTalk;

/// <summary>
/// 钉钉 Stream 模式 WebSocket 消息信封（顶层结构）。
/// </summary>
public sealed class DingTalkStreamEnvelope
{
    [JsonPropertyName("specVersion")]
    public string? SpecVersion { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("headers")]
    public DingTalkStreamHeaders? Headers { get; init; }

    /// <summary>事件数据（JSON 字符串，需二次解析）</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }
}

public sealed class DingTalkStreamHeaders
{
    [JsonPropertyName("messageId")]
    public string MessageId { get; init; } = string.Empty;

    [JsonPropertyName("topic")]
    public string? Topic { get; init; }

    [JsonPropertyName("time")]
    public string? Time { get; init; }

    [JsonPropertyName("eventType")]
    public string? EventType { get; init; }

    [JsonPropertyName("eventBornTime")]
    public string? EventBornTime { get; init; }
}

// ── IM 消息事件数据（data 字段 JSON 二次解析）────────────────────────────────

public sealed class DingTalkStreamEventData
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; init; } = string.Empty;

    /// <summary>会话类型：1=单聊, 2=群聊</summary>
    [JsonPropertyName("conversationType")]
    public string? ConversationType { get; init; }

    [JsonPropertyName("senderStaffId")]
    public string? SenderStaffId { get; init; }

    [JsonPropertyName("senderNick")]
    public string? SenderNick { get; init; }

    [JsonPropertyName("senderId")]
    public string? SenderId { get; init; }

    [JsonPropertyName("text")]
    public DingTalkTextContent? Text { get; init; }

    [JsonPropertyName("msgId")]
    public string? MsgId { get; init; }

    [JsonPropertyName("msgtype")]
    public string? MsgType { get; init; }

    [JsonPropertyName("createAt")]
    public long? CreateAt { get; init; }

    [JsonPropertyName("robotCode")]
    public string? RobotCode { get; init; }

    [JsonPropertyName("sessionWebhook")]
    public string? SessionWebhook { get; init; }

    /// <summary>群聊 sessionWebhook 过期时间（Unix 毫秒时间戳）</summary>
    [JsonPropertyName("sessionWebhookExpiredTime")]
    public long? SessionWebhookExpiredTime { get; init; }

    [JsonPropertyName("isInAtList")]
    public bool? IsInAtList { get; init; }

    [JsonPropertyName("chatbotCorpId")]
    public string? ChatbotCorpId { get; init; }
}

public sealed class DingTalkTextContent
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

// ── Access Token 响应 ─────────────────────────────────────────────────────

public sealed class DingTalkAccessTokenResponse
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; init; }

    /// <summary>过期时间（秒）</summary>
    [JsonPropertyName("expireIn")]
    public int ExpireIn { get; init; }
}

// ── 开启 Stream 连接响应 ──────────────────────────────────────────────────

public sealed class DingTalkOpenConnectionResponse
{
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    [JsonPropertyName("ticket")]
    public string? Ticket { get; init; }
}

// ── 发送消息响应 ──────────────────────────────────────────────────────────

public sealed class DingTalkSendMessageResponse
{
    [JsonPropertyName("processQueryKey")]
    public string? ProcessQueryKey { get; init; }
}
