using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using ContentBlock = Kode.Agent.Sdk.Core.Types.ContentBlock;
using FileContent = Kode.Agent.Sdk.Core.Types.FileContent;
using ImageContent = Kode.Agent.Sdk.Core.Types.ImageContent;
using Message = Kode.Agent.Sdk.Core.Types.Message;
using TextContent = Kode.Agent.Sdk.Core.Types.TextContent;
using ToolResultContent = Kode.Agent.Sdk.Core.Types.ToolResultContent;
using ToolUseContent = Kode.Agent.Sdk.Core.Types.ToolUseContent;
using VideoContent = Kode.Agent.Sdk.Core.Types.VideoContent;

namespace Kode.Agent.Sdk.Infrastructure.Providers;

/// <summary>
/// OpenAI model provider implementation using official SDK.
/// </summary>
public sealed class OpenAIProvider : IModelProvider
{
    private readonly OpenAIClient _client;
    private readonly OpenAIOptions _options;
    private readonly ILogger<OpenAIProvider>? _logger;

    public string ProviderName => "openai";

    public OpenAIProvider(OpenAIOptions options, ILogger<OpenAIProvider>? logger = null)
    {
        _options = options;
        _logger = logger;

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(options.BaseUrl))
            clientOptions.Endpoint = new Uri(options.BaseUrl);
        if (options.CustomHeaders is { Count: > 0 })
        {
            foreach (var (key, value) in options.CustomHeaders)
            {
                if (string.Equals(key, "User-Agent", StringComparison.OrdinalIgnoreCase))
                    clientOptions.UserAgentApplicationId = value;
            }
        }

        // Inject MediaRewriteHttpHandler so video_url / file_url markers in content
        // parts are rewritten to their proper JSON shapes before leaving the process.
        clientOptions.Transport = new HttpClientPipelineTransport(
            new HttpClient(new MediaRewriteHttpHandler()));

        _client = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey),
            clientOptions);
    }

    /// <summary>
    /// For backward compatibility with HttpClient-based construction.
    /// </summary>
    public OpenAIProvider(HttpClient httpClient, OpenAIOptions options, ILogger<OpenAIProvider>? logger = null)
        : this(options, logger)
    {
    }

    /// <summary>
    /// Internal constructor for testing — accepts a fully-configured
    /// <see cref="OpenAIClientOptions"/> (including a mock transport).
    /// The caller is responsible for chaining <see cref="MediaRewriteHttpHandler"/>
    /// inside the transport if request-body transformation is required.
    /// </summary>
    internal OpenAIProvider(OpenAIOptions options, OpenAIClientOptions clientOptions, ILogger<OpenAIProvider>? logger = null)
    {
        _options = options;
        _logger = logger;
        _client = new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions);
    }

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatClient = _client.GetChatClient(request.Model);
        var messages = BuildChatMessages(request);
        var options = BuildChatOptions(request);

        var toolCallBuilders = new Dictionary<int, (string Id, string Name, System.Text.StringBuilder Args)>();

        await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, options, cancellationToken))
        {
            foreach (var chunk in ConvertStreamUpdate(update, toolCallBuilders))
            {
                yield return chunk;
            }
        }
    }

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        var chatClient = _client.GetChatClient(request.Model);
        var messages = BuildChatMessages(request);
        var options = BuildChatOptions(request);

        var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
        return ConvertToModelResponse(result.Value);
    }

    public async Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ModelRequest
            {
                Model = "gpt-3.5-turbo",
                Messages = [Message.User("Hi")],
                MaxTokens = 1
            };

            await CompleteAsync(request, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "OpenAI provider validation failed");
            return false;
        }
    }

    private List<ChatMessage> BuildChatMessages(ModelRequest request)
    {
        var messages = new List<ChatMessage>();

        // Add system message if provided
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new SystemChatMessage(request.SystemPrompt));
        }

        foreach (var msg in request.Messages)
        {
            if (msg.Role == MessageRole.System) continue;

            var chatMessage = ConvertMessage(msg);
            if (chatMessage != null)
            {
                messages.Add(chatMessage);
            }

            // Handle tool results separately
            foreach (var toolResult in msg.Content.OfType<ToolResultContent>())
            {
                messages.Add(new ToolChatMessage(toolResult.ToolUseId, JsonSerializer.Serialize(toolResult.Content) ?? "")); // Fixed by Nietzsche: use JSON serialization instead of ToString() for anonymous types
            }
        }

        return messages;
    }

    private static ChatMessage? ConvertMessage(Message msg)
    {
        if (msg.Role == MessageRole.User)
        {
            var hasMultiModal = msg.Content.OfType<ImageContent>().Any()
                || msg.Content.OfType<VideoContent>().Any()
                || msg.Content.OfType<FileContent>().Any();
            if (!hasMultiModal)
            {
                var text = string.Join("", msg.Content.OfType<TextContent>().Select(t => t.Text));
                // Skip empty user messages (e.g. messages that contain only ToolResultContent —
                // those are added separately as ToolChatMessage entries below).
                if (string.IsNullOrEmpty(text)) return null;
                return new UserChatMessage(text);
            }

            // Multi-modal user message: build content part list.
            // VideoContent and FileContent are not natively supported by the OpenAI SDK, so they
            // are encoded as specially-prefixed text parts.  MediaRewriteHttpHandler rewrites them
            // into the correct video_url / file_url JSON shapes at the HTTP layer before the
            // request leaves the process.
            var parts = new List<ChatMessageContentPart>();
            foreach (var block in msg.Content)
            {
                switch (block)
                {
                    case TextContent tc:
                        parts.Add(ChatMessageContentPart.CreateTextPart(tc.Text));
                        break;
                    case ImageContent img when img.Url is not null:
                        parts.Add(ChatMessageContentPart.CreateImagePart(new Uri(img.Url)));
                        break;
                    case ImageContent img when img.Data is not null:
                        parts.Add(ChatMessageContentPart.CreateImagePart(
                            BinaryData.FromBytes(Convert.FromBase64String(img.Data)),
                            img.MediaType ?? "image/png"));
                        break;
                    case VideoContent video:
                        parts.Add(ChatMessageContentPart.CreateTextPart(
                            MediaRewriteHttpHandler.VideoMarker + video.Url));
                        break;
                    case FileContent file:
                        parts.Add(ChatMessageContentPart.CreateTextPart(
                            MediaRewriteHttpHandler.FileMarker + file.Url));
                        break;
                }
            }
            return new UserChatMessage(parts);
        }

        if (msg.Role == MessageRole.Assistant)
        {
            var toolUses = msg.Content.OfType<ToolUseContent>().ToList();
            var textContent = string.Join("", msg.Content.OfType<TextContent>().Select(t => t.Text));

            if (toolUses.Count > 0)
            {
                var toolCalls = toolUses.Select(tu =>
                    ChatToolCall.CreateFunctionToolCall(
                        tu.Id,
                        tu.Name,
                        BinaryData.FromString(JsonSerializer.Serialize(tu.Input))
                    )).ToList();

                // Create assistant message with tool calls
                var assistantMessage = new AssistantChatMessage(toolCalls);

                // Always add content - OpenAI requires it even if empty
                if (!string.IsNullOrEmpty(textContent))
                {
                    assistantMessage.Content.Add(ChatMessageContentPart.CreateTextPart(textContent));
                }
                else
                {
                    // Add empty text to satisfy OpenAI API requirement
                    assistantMessage.Content.Add(ChatMessageContentPart.CreateTextPart(""));
                }

                return assistantMessage;
            }

            // For non-tool messages, return text content (use empty string if null)
            return new AssistantChatMessage(textContent ?? "");
        }

        return null;
    }

    private ChatCompletionOptions BuildChatOptions(ModelRequest request)
    {
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = request.MaxTokens,
            Temperature = (float?)request.Temperature
        };

        if (request.StopSequences?.Count > 0)
        {
            foreach (var stop in request.StopSequences)
            {
                options.StopSequences.Add(stop);
            }
        }

        if (request.Tools?.Count > 0)
        {
            foreach (var tool in request.Tools)
            {
                var schema = tool.InputSchema is JsonElement je
                    ? BinaryData.FromString(je.GetRawText())
                    : BinaryData.FromString(JsonSerializer.Serialize(tool.InputSchema));

                options.Tools.Add(ChatTool.CreateFunctionTool(
                    tool.Name,
                    tool.Description,
                    schema));
            }
        }

        return options;
    }

    private IEnumerable<StreamChunk> ConvertStreamUpdate(
        StreamingChatCompletionUpdate update,
        Dictionary<int, (string Id, string Name, System.Text.StringBuilder Args)> toolCallBuilders)
    {
        // Text delta
        foreach (var contentPart in update.ContentUpdate)
        {
            if (!string.IsNullOrEmpty(contentPart.Text))
            {
                yield return new StreamChunk
                {
                    Type = StreamChunkType.TextDelta,
                    TextDelta = contentPart.Text
                };
            }
        }

        // Tool calls
        foreach (var toolUpdate in update.ToolCallUpdates)
        {
            var index = toolUpdate.Index;

            // New tool call
            if (!string.IsNullOrEmpty(toolUpdate.ToolCallId))
            {
                toolCallBuilders[index] = (toolUpdate.ToolCallId, toolUpdate.FunctionName ?? "", new System.Text.StringBuilder());

                yield return new StreamChunk
                {
                    Type = StreamChunkType.ToolUseStart,
                    ToolUse = new ToolUseChunk
                    {
                        Id = toolUpdate.ToolCallId,
                        Name = toolUpdate.FunctionName
                    }
                };
            }

            // Tool arguments delta
            var argsUpdate = toolUpdate.FunctionArgumentsUpdate?.ToString();
            if (!string.IsNullOrEmpty(argsUpdate))
            {
                if (toolCallBuilders.TryGetValue(index, out var builder))
                {
                    builder.Args.Append(argsUpdate);

                    yield return new StreamChunk
                    {
                        Type = StreamChunkType.ToolUseInputDelta,
                        ToolUse = new ToolUseChunk
                        {
                            Id = builder.Id,
                            InputDelta = argsUpdate
                        }
                    };
                }
            }
        }

        // Finish reason
        if (update.FinishReason != null)
        {
            // Complete pending tool calls
            foreach (var (index, builder) in toolCallBuilders)
            {
                object? input = null;
                var argsJson = builder.Args.ToString();
                if (!string.IsNullOrEmpty(argsJson))
                {
                    try { input = JsonSerializer.Deserialize<object>(argsJson); }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Failed to deserialize tool arguments JSON"); }
                }

                yield return new StreamChunk
                {
                    Type = StreamChunkType.ToolUseComplete,
                    ToolUse = new ToolUseChunk
                    {
                        Id = builder.Id,
                        Name = builder.Name,
                        Input = input
                    }
                };
            }
            toolCallBuilders.Clear();

            var stopReason = update.FinishReason switch
            {
                ChatFinishReason.Stop => ModelStopReason.EndTurn,
                ChatFinishReason.Length => ModelStopReason.MaxTokens,
                ChatFinishReason.ToolCalls => ModelStopReason.ToolUse,
                ChatFinishReason.ContentFilter => ModelStopReason.EndTurn,
                _ => ModelStopReason.EndTurn
            };

            yield return new StreamChunk
            {
                Type = StreamChunkType.MessageStop,
                StopReason = stopReason,
                Usage = update.Usage != null
                    ? new TokenUsage
                    {
                        InputTokens = update.Usage.InputTokenCount,
                        OutputTokens = update.Usage.OutputTokenCount
                    }
                    : null
            };
        }
    }

    private static ModelResponse ConvertToModelResponse(ChatCompletion response)
    {
        var content = new List<ContentBlock>();

        foreach (var part in response.Content)
        {
            if (!string.IsNullOrEmpty(part.Text))
            {
                content.Add(new TextContent { Text = part.Text });
            }
        }

        foreach (var toolCall in response.ToolCalls)
        {
            object? input = null;
            var argsJson = toolCall.FunctionArguments?.ToString();
            if (!string.IsNullOrEmpty(argsJson))
            {
                try { input = JsonSerializer.Deserialize<object>(argsJson); }
                catch { }
            }

            content.Add(new ToolUseContent
            {
                Id = toolCall.Id,
                Name = toolCall.FunctionName,
                Input = input ?? new { }
            });
        }

        var stopReason = response.FinishReason switch
        {
            ChatFinishReason.Stop => ModelStopReason.EndTurn,
            ChatFinishReason.Length => ModelStopReason.MaxTokens,
            ChatFinishReason.ToolCalls => ModelStopReason.ToolUse,
            ChatFinishReason.ContentFilter => ModelStopReason.EndTurn,
            _ => ModelStopReason.EndTurn
        };

        return new ModelResponse
        {
            Content = content,
            StopReason = stopReason,
            Usage = new TokenUsage
            {
                InputTokens = response.Usage?.InputTokenCount ?? 0,
                OutputTokens = response.Usage?.OutputTokenCount ?? 0
            },
            Model = response.Model ?? ""
        };
    }
}

/// <summary>
/// <see cref="DelegatingHandler"/> that rewrites specially-prefixed text content
/// parts into <c>video_url</c> and <c>file_url</c> JSON shapes before the HTTP
/// request is sent.  This is necessary because the OpenAI .NET SDK does not
/// natively support these GLM-compatible content-part types; they are encoded
/// as text parts with sentinel prefixes in <see cref="OpenAIProvider"/> and
/// decoded here at the HTTP layer.
/// </summary>
internal sealed class MediaRewriteHttpHandler(HttpMessageHandler? inner = null)
    : DelegatingHandler(inner ?? new HttpClientHandler())
{
    /// <summary>Sentinel prefix for a <see cref="VideoContent"/> URL.</summary>
    internal const string VideoMarker = "__KODE_VIDEO_URL__";

    /// <summary>Sentinel prefix for a <see cref="FileContent"/> URL.</summary>
    internal const string FileMarker = "__KODE_FILE_URL__";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            var transformed = TransformMediaMarkers(body);
            if (!ReferenceEquals(transformed, body))
                request.Content = new StringContent(transformed, Encoding.UTF8, "application/json");
        }

        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Walks the serialised request JSON and replaces any text content parts whose
    /// text begins with <see cref="VideoMarker"/> or <see cref="FileMarker"/> with
    /// the correct <c>video_url</c> / <c>file_url</c> object shapes.
    /// Returns the original string (same reference) when no markers are found, to
    /// avoid unnecessary allocations on the hot path.
    /// </summary>
    internal static string TransformMediaMarkers(string json)
    {
        // Fast pre-check: skip JSON parsing when no markers are present.
        if (!json.Contains(VideoMarker, StringComparison.Ordinal)
            && !json.Contains(FileMarker, StringComparison.Ordinal))
            return json;

        var root = JsonNode.Parse(json);
        if (root is null) return json;

        var messages = root["messages"]?.AsArray();
        if (messages is null) return json;

        bool changed = false;
        foreach (var msg in messages)
        {
            if (msg?["content"] is not JsonArray parts) continue;

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part?["type"]?.GetValue<string>() != "text") continue;

                var text = part["text"]?.GetValue<string>();
                if (text is null) continue;

                if (text.StartsWith(VideoMarker, StringComparison.Ordinal))
                {
                    var url = text[VideoMarker.Length..];
                    parts[i] = JsonNode.Parse(JsonSerializer.Serialize(
                        new { type = "video_url", video_url = new { url } }))!;
                    changed = true;
                }
                else if (text.StartsWith(FileMarker, StringComparison.Ordinal))
                {
                    var url = text[FileMarker.Length..];
                    parts[i] = JsonNode.Parse(JsonSerializer.Serialize(
                        new { type = "file_url", file_url = new { url } }))!;
                    changed = true;
                }
            }
        }

        return changed ? root.ToJsonString() : json;
    }
}

/// <summary>
/// Options for configuring the OpenAI provider.
/// </summary>
public class OpenAIOptions
{
    /// <summary>
    /// The API key for authentication.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// The base URL for the API (default: https://api.openai.com).
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// The organization ID.
    /// </summary>
    public string? Organization { get; init; }

    /// <summary>
    /// The default model to use.
    /// </summary>
    public string? DefaultModel { get; init; }

    /// <summary>
    /// Custom HTTP request headers to add to each request.
    /// Only User-Agent is supported via UserAgentApplicationId.
    /// </summary>
    public IReadOnlyDictionary<string, string>? CustomHeaders { get; init; }
}
