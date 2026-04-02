using System.Text.Json.Serialization;

namespace KodaClaw.ChannelHub.Connectors.Feishu;

/// <summary>
/// WS 事件信封（对应飞书事件框架 v2.0 外层结构，
/// protobuf 帧 payload 字段反序列化后得到此结构）
/// </summary>
public sealed class FeishuWsEventEnvelope
{
    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    [JsonPropertyName("header")]
    public FeishuEventHeader? Header { get; init; }

    [JsonPropertyName("event")]
    public FeishuImMessageEvent? Event { get; init; }
}

public sealed class FeishuEventHeader
{
    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; init; }

    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("app_id")]
    public string? AppId { get; init; }

    [JsonPropertyName("tenant_key")]
    public string? TenantKey { get; init; }
}

// ── im.message.receive_v1 事件体 ──────────────────────────────────────────

public sealed class FeishuImMessageEvent
{
    [JsonPropertyName("sender")]
    public FeishuSender? Sender { get; init; }

    [JsonPropertyName("message")]
    public FeishuImMessage? Message { get; init; }
}

public sealed class FeishuSender
{
    [JsonPropertyName("sender_id")]
    public FeishuUserId? SenderId { get; init; }

    [JsonPropertyName("sender_type")]
    public string? SenderType { get; init; }

    [JsonPropertyName("tenant_key")]
    public string? TenantKey { get; init; }
}

public sealed class FeishuUserId
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("union_id")]
    public string? UnionId { get; init; }

    [JsonPropertyName("open_id")]
    public string? OpenId { get; init; }
}

public sealed class FeishuImMessage
{
    [JsonPropertyName("message_id")]
    public string MessageId { get; init; } = string.Empty;

    [JsonPropertyName("root_id")]
    public string? RootId { get; init; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; init; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; init; }

    [JsonPropertyName("chat_id")]
    public string ChatId { get; init; } = string.Empty;

    [JsonPropertyName("chat_type")]
    public string? ChatType { get; init; }

    [JsonPropertyName("message_type")]
    public string? MessageType { get; init; }

    /// <summary>消息内容（JSON 字符串，需二次解析）</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("mentions")]
    public IReadOnlyList<FeishuMention>? Mentions { get; init; }
}

public sealed class FeishuMention
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("id")]
    public FeishuUserId? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("tenant_key")]
    public string? TenantKey { get; init; }
}

/// <summary>消息 content 字段（text 类型）</summary>
public sealed class FeishuTextContent
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

// ── Token 响应 ────────────────────────────────────────────────────────────

public sealed class FeishuTenantAccessTokenResponse
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("msg")]
    public string? Msg { get; init; }

    [JsonPropertyName("tenant_access_token")]
    public string? TenantAccessToken { get; init; }

    [JsonPropertyName("expire")]
    public int ExpireSeconds { get; init; }
}

public sealed class FeishuAppAccessTokenResponse
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("msg")]
    public string? Msg { get; init; }

    [JsonPropertyName("app_access_token")]
    public string? AppAccessToken { get; init; }

    [JsonPropertyName("expire")]
    public int ExpireSeconds { get; init; }
}

// ── 发消息响应 ────────────────────────────────────────────────────────────

public sealed class FeishuSendMessageResponse
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("msg")]
    public string? Msg { get; init; }

    [JsonPropertyName("data")]
    public FeishuSendMessageData? Data { get; init; }
}

public sealed class FeishuSendMessageData
{
    [JsonPropertyName("message_id")]
    public string? MessageId { get; init; }
}

// ── 图片上传响应 ──────────────────────────────────────────────────────────

public sealed class FeishuUploadImageResponse
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("msg")]
    public string? Msg { get; init; }

    [JsonPropertyName("data")]
    public FeishuUploadImageData? Data { get; init; }
}

public sealed class FeishuUploadImageData
{
    [JsonPropertyName("image_key")]
    public string? ImageKey { get; init; }
}

// ── 文件上传响应（音频等） ─────────────────────────────────────────────────

public sealed class FeishuUploadFileResponse
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("msg")]
    public string? Msg { get; init; }

    [JsonPropertyName("data")]
    public FeishuUploadFileData? Data { get; init; }
}

public sealed class FeishuUploadFileData
{
    [JsonPropertyName("file_key")]
    public string? FileKey { get; init; }
}

// ── WS 长连接 endpoint ────────────────────────────────────────────────────

/// <summary>
/// POST /callback/ws/endpoint 返回的连接信息。
/// URL 中包含 service_id、ticket 等，直接用于 WebSocket 连接，无需额外认证头。
/// </summary>
public sealed record FeishuWsEndpoint(
    string Url,
    int PingIntervalSeconds,
    int ReconnectCount,
    int ReconnectIntervalSeconds,
    int ReconnectNonceSeconds);

public sealed class FeishuImageContent
{
    [JsonPropertyName("image_key")]
    public string? ImageKey { get; init; }
}
