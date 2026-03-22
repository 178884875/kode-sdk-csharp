using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using ContentBlock = Kode.Agent.Sdk.Core.Types.ContentBlock;
using ImageContent = Kode.Agent.Sdk.Core.Types.ImageContent;
using Message = Kode.Agent.Sdk.Core.Types.Message;
using TextContent = Kode.Agent.Sdk.Core.Types.TextContent;
using ToolResultContent = Kode.Agent.Sdk.Core.Types.ToolResultContent;
using ToolUseContent = Kode.Agent.Sdk.Core.Types.ToolUseContent;
using AnthropicStopReason = Anthropic.Models.Messages.StopReason;

namespace Kode.Agent.Sdk.Infrastructure.Providers;

/// <summary>
/// Anthropic (Claude) model provider implementation using official SDK.
/// </summary>
public sealed class AnthropicProvider : IModelProvider
{
    private readonly AnthropicClient _client;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicProvider>? _logger;

    public string ProviderName => "anthropic";

    public AnthropicProvider(AnthropicOptions options, ILogger<AnthropicProvider>? logger = null)
    {
        _options = options;
        _logger = logger;

        _client = new AnthropicClient
        {
            ApiKey = options.ApiKey,
            BaseUrl = options.BaseUrl ?? "https://api.anthropic.com"
        };
    }

    /// <summary>
    /// For backward compatibility with HttpClient-based construction.
    /// </summary>
    public AnthropicProvider(HttpClient httpClient, AnthropicOptions options, ILogger<AnthropicProvider>? logger = null)
        : this(options, logger)
    {
    }

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var parameters = BuildMessageParameters(request);
        var toolIdMap = new Dictionary<long, string>();
        var toolNameMap = new Dictionary<long, string>();
        var toolInputBuilders = new Dictionary<long, System.Text.StringBuilder>();

        await foreach (var evt in _client.Messages.CreateStreaming(parameters, cancellationToken))
        {
            // Handle content block start
            if (evt.TryPickContentBlockStart(out var startEvent))
            {
                if (startEvent.ContentBlock.TryPickToolUse(out var toolUse))
                {
                    toolIdMap[startEvent.Index] = toolUse.ID;
                    toolNameMap[startEvent.Index] = toolUse.Name;
                    toolInputBuilders[startEvent.Index] = new System.Text.StringBuilder();

                    yield return new StreamChunk
                    {
                        Type = StreamChunkType.ToolUseStart,
                        ToolUse = new ToolUseChunk
                        {
                            Id = toolUse.ID,
                            Name = toolUse.Name
                        }
                    };
                }

                continue;
            }

            // Handle content block delta
            if (evt.TryPickContentBlockDelta(out var deltaEvent))
            {
                if (deltaEvent.Delta.TryPickText(out var textDelta))
                {
                    yield return new StreamChunk
                    {
                        Type = StreamChunkType.TextDelta,
                        TextDelta = textDelta.Text
                    };
                }
                else if (deltaEvent.Delta.TryPickInputJson(out var jsonDelta))
                {
                    if (toolInputBuilders.TryGetValue(deltaEvent.Index, out var builder))
                    {
                        builder.Append(jsonDelta.PartialJson);
                    }

                    var toolId = toolIdMap.GetValueOrDefault(deltaEvent.Index, deltaEvent.Index.ToString());
                    var toolName = toolNameMap.GetValueOrDefault(deltaEvent.Index, "");

                    yield return new StreamChunk
                    {
                        Type = StreamChunkType.ToolUseInputDelta,
                        ToolUse = new ToolUseChunk
                        {
                            Id = toolId,
                            Name = toolName,
                            InputDelta = jsonDelta.PartialJson
                        }
                    };
                }

                continue;
            }

            // Handle content block stop
            if (evt.TryPickContentBlockStop(out var stopEvent))
            {
                if (toolInputBuilders.TryGetValue(stopEvent.Index, out var builder))
                {
                    var inputJson = builder.ToString();
                    toolInputBuilders.Remove(stopEvent.Index);

                    object? input = null;
                    if (!string.IsNullOrEmpty(inputJson))
                    {
                        try
                        {
                            input = JsonSerializer.Deserialize<object>(inputJson);
                        }
                        catch
                        {
                            /* ignore */
                        }
                    }

                    var toolId = toolIdMap.GetValueOrDefault(stopEvent.Index, stopEvent.Index.ToString());
                    toolIdMap.Remove(stopEvent.Index);
                    toolNameMap.Remove(stopEvent.Index);

                    yield return new StreamChunk
                    {
                        Type = StreamChunkType.ToolUseComplete,
                        ToolUse = new ToolUseChunk
                        {
                            Id = toolId,
                            Input = input
                        }
                    };
                }

                continue;
            }

            // Handle message delta
            if (evt.TryPickDelta(out var messageDelta))
            {
                var apiStopReason = messageDelta.Delta.StopReason;
                var stopReason = apiStopReason != null
                    ? ConvertStopReason((AnthropicStopReason)apiStopReason)
                    : ModelStopReason.EndTurn;
                yield return new StreamChunk
                {
                    Type = StreamChunkType.MessageStop,
                    StopReason = stopReason,
                    Usage = new TokenUsage
                    {
                        InputTokens = (int)(messageDelta.Usage.InputTokens ?? 0),
                        OutputTokens = (int)messageDelta.Usage.OutputTokens
                    }
                };
                continue;
            }

            // Handle message stop
            if (evt.TryPickStop(out _))
            {
                yield return new StreamChunk { Type = StreamChunkType.MessageStop };
            }
        }
    }

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        var parameters = BuildMessageParameters(request);
        var response = await _client.Messages.Create(parameters, cancellationToken);
        return ConvertToModelResponse(response);
    }

    public async Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ModelRequest
            {
                Model = _options.ModelId ?? "claude-3-5-haiku-20241022",
                Messages = [Message.User("Hi")],
                MaxTokens = 1
            };

            await CompleteAsync(request, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Anthropic provider validation failed");
            return false;
        }
    }

    private MessageCreateParams BuildMessageParameters(ModelRequest request)
    {
        var messages = request.Messages
            .Where(m => m.Role != MessageRole.System)
            .Select(ConvertMessage)
            .ToList();

        var tools = request.Tools?.Select(t => new ToolUnion(new Tool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = ConvertInputSchema(t.InputSchema)
        })).ToList();

        var stopSequences = request.StopSequences?.Count > 0
            ? request.StopSequences.ToList()
            : null;

        return new MessageCreateParams
        {
            Model = request.Model,
            Messages = messages,
            MaxTokens = request.MaxTokens ?? 4096,
            Temperature = request.Temperature,
            System = !string.IsNullOrEmpty(request.SystemPrompt) ? request.SystemPrompt : null!,
            Tools = tools,
            StopSequences = stopSequences
        };
    }

    private static MessageParam ConvertMessage(Message msg)
    {
        var role = msg.Role == MessageRole.User ? Role.User : Role.Assistant;
        var content = msg.Content.Select(ConvertContentBlock).ToList();

        return new MessageParam
        {
            Role = role,
            Content = content
        };
    }

    private static ContentBlockParam ConvertContentBlock(ContentBlock block)
    {
        return block switch
        {
            TextContent text => new ContentBlockParam(new TextBlockParam { Text = text.Text }),
            ImageContent img when img.Url is not null =>
                new ContentBlockParam(new ImageBlockParam
                {
                    Source = new UrlImageSource { Url = img.Url }
                }),
            ImageContent img when img.Data is not null =>
                new ContentBlockParam(new ImageBlockParam
                {
                    Source = new Base64ImageSource
                    {
                        MediaType = img.MediaType switch
                        {
                            "image/jpeg" => MediaType.ImageJpeg,
                            "image/gif"  => MediaType.ImageGif,
                            "image/webp" => MediaType.ImageWebP,
                            _            => MediaType.ImagePng,
                        },
                        Data = img.Data
                    }
                }),
            ToolUseContent toolUse => new ContentBlockParam(new ToolUseBlockParam
            {
                ID = toolUse.Id,
                Name = toolUse.Name,
                Input = ConvertToolInput(toolUse.Input)
            }),
            ToolResultContent toolResult => new ContentBlockParam(new ToolResultBlockParam(toolResult.ToolUseId)
            {
                Content = toolResult.Content.ToString() ?? "",
                IsError = toolResult.IsError
            }),
            ThinkingContent thinking => new ContentBlockParam(new TextBlockParam
                { Text = $"<thinking>{thinking.Thinking}</thinking>" }),
            _ => new ContentBlockParam(new TextBlockParam { Text = "" })
        };
    }

    private static IReadOnlyDictionary<string, JsonElement> ConvertToolInput(object? input)
    {
        if (input == null)
            return new Dictionary<string, JsonElement>();

        if (input is IReadOnlyDictionary<string, JsonElement> readOnlyDict)
            return readOnlyDict;

        if (input is Dictionary<string, JsonElement> dict)
            return dict;

        if (input is JsonElement { ValueKind: JsonValueKind.Object } jsonElement)
        {
            var result = new Dictionary<string, JsonElement>();
            foreach (var prop in jsonElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.Clone();
            }

            return result;
        }

        // Serialize and deserialize to get proper JsonElement dictionary
        var json = JsonSerializer.Serialize(input);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        if (element.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, JsonElement>();
            foreach (var prop in element.EnumerateObject())
            {
                result[prop.Name] = prop.Value.Clone();
            }

            return result;
        }

        return new Dictionary<string, JsonElement>();
    }

    private static InputSchema ConvertInputSchema(object schema)
    {
        if (schema is JsonElement jsonElement)
        {
            var properties = new Dictionary<string, JsonElement>();
            var required = new List<string>();

            if (jsonElement.TryGetProperty("properties", out var propsElement) &&
                propsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in propsElement.EnumerateObject())
                {
                    properties[prop.Name] = prop.Value.Clone();
                }
            }

            if (jsonElement.TryGetProperty("required", out var reqElement) &&
                reqElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in reqElement.EnumerateArray())
                {
                    if (item.GetString() is { } reqProp)
                    {
                        required.Add(reqProp);
                    }
                }
            }

            return new InputSchema
            {
                Properties = properties,
                Required = required
            };
        }

        // For other object types, serialize and parse
        var json = JsonSerializer.Serialize(schema);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return ConvertInputSchema(element);
    }

    private static ModelStopReason ConvertStopReason(AnthropicStopReason? stopReason)
    {
        return stopReason switch
        {
            AnthropicStopReason.EndTurn => ModelStopReason.EndTurn,
            AnthropicStopReason.MaxTokens => ModelStopReason.MaxTokens,
            AnthropicStopReason.StopSequence => ModelStopReason.StopSequence,
            AnthropicStopReason.ToolUse => ModelStopReason.ToolUse,
            _ => ModelStopReason.EndTurn
        };
    }

    private static ModelResponse ConvertToModelResponse(Anthropic.Models.Messages.Message response)
    {
        var content = new List<ContentBlock>();

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                content.Add(new TextContent { Text = textBlock.Text });
            }
            else if (block.TryPickToolUse(out var toolUseBlock))
            {
                content.Add(new ToolUseContent
                {
                    Id = toolUseBlock.ID,
                    Name = toolUseBlock.Name,
                    Input = toolUseBlock.Input
                });
            }
        }

        var apiStopReason = response.StopReason;
        var stopReason = apiStopReason != null
            ? ConvertStopReason((AnthropicStopReason)apiStopReason)
            : ModelStopReason.EndTurn;

        return new ModelResponse
        {
            Content = content,
            StopReason = stopReason,
            Usage = new TokenUsage
            {
                InputTokens = (int)response.Usage.InputTokens,
                OutputTokens = (int)response.Usage.OutputTokens
            },
            Model = response.Model ?? ""
        };
    }
}

/// <summary>
/// Options for configuring the Anthropic provider.
/// </summary>
public class AnthropicOptions
{
    /// <summary>
    /// The API key for authentication.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// The base URL for the API (default: https://api.anthropic.com).
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// The default model ID to use.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Whether to enable beta features.
    /// </summary>
    public bool EnableBetaFeatures { get; init; }
}