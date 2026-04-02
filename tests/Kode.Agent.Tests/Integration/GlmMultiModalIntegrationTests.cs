using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Infrastructure.Providers;
using Kode.Agent.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Kode.Agent.Tests.Integration;

/// <summary>
/// Live integration tests against the ZhipuAI (GLM) OpenAI-compatible API.
/// These tests require a valid <c>ZHIPU_API_KEY</c> and a working internet
/// connection; they are skipped automatically when the key is absent.
///
/// Run manually:
///   ZHIPU_API_KEY=xxx dotnet test --filter "GlmMultiModal"
/// </summary>
public sealed class GlmMultiModalIntegrationTests(ITestOutputHelper output)
{
    private OpenAIProvider CreateGlmProvider()
    {
        var apiKey = Environment.GetEnvironmentVariable(GlmIntegrationFactAttribute.EnvVar)!;
        return new OpenAIProvider(new OpenAIOptions
        {
            ApiKey = apiKey,
            BaseUrl = GlmIntegrationFactAttribute.GlmBaseUrl
        });
    }

    private void PrintResponse(ModelResponse response)
    {
        var text = string.Join("", response.Content.OfType<TextContent>().Select(t => t.Text));
        output.WriteLine($"[model] {response.Model}");
        output.WriteLine($"[usage] in={response.Usage?.InputTokens} out={response.Usage?.OutputTokens}");
        output.WriteLine($"[reply] {text}");
    }

    private void PrintStreamChunks(IReadOnlyList<StreamChunk> chunks)
    {
        var text = string.Join("", chunks
            .Where(c => c.Type == StreamChunkType.TextDelta)
            .Select(c => c.TextDelta));
        var stop = chunks.FirstOrDefault(c => c.Type == StreamChunkType.MessageStop);
        output.WriteLine($"[stream] {text}");
        if (stop?.Usage is { } u)
            output.WriteLine($"[usage]  in={u.InputTokens} out={u.OutputTokens}");
    }

    // -------------------------------------------------------------------------
    // ImageContent (URL)
    // -------------------------------------------------------------------------

    [GlmIntegrationFact]
    public async Task CompleteAsync_WithImageUrl_ReturnsNonEmptyResponse()
    {
        var provider = CreateGlmProvider();

        var request = new ModelRequest
        {
            Model = GlmIntegrationFactAttribute.DefaultModel,
            Messages =
            [
                new Message
                {
                    Role = MessageRole.User,
                    Content =
                    [
                        new TextContent { Text = "用一句话描述图片内容。" },
                        ImageContent.FromUrl("https://cdn.bigmodel.cn/static/logo/register.png")
                    ]
                }
            ],
            MaxTokens = 100
        };

        var response = await provider.CompleteAsync(request);
        PrintResponse(response);

        response.Should().NotBeNull();
        response.Content.Should().NotBeEmpty();
        response.Content.OfType<TextContent>().Should().ContainSingle();
        response.Content.OfType<TextContent>().Single().Text.Should().NotBeNullOrWhiteSpace();
    }

    // -------------------------------------------------------------------------
    // VideoContent
    // -------------------------------------------------------------------------

    [GlmIntegrationFact]
    public async Task CompleteAsync_WithVideoUrl_ReturnsNonEmptyResponse()
    {
        var provider = CreateGlmProvider();

        var request = new ModelRequest
        {
            Model = GlmIntegrationFactAttribute.DefaultModel,
            Messages =
            [
                new Message
                {
                    Role = MessageRole.User,
                    Content =
                    [
                        new TextContent { Text = "用一句话描述视频内容。" },
                        VideoContent.FromUrl("https://cdn.bigmodel.cn/agent-demos/lark/113123.mov")
                    ]
                }
            ],
            MaxTokens = 100
        };

        var response = await provider.CompleteAsync(request);
        PrintResponse(response);

        response.Should().NotBeNull();
        response.Content.Should().NotBeEmpty();
        response.Content.OfType<TextContent>().Should().ContainSingle();
        response.Content.OfType<TextContent>().Single().Text.Should().NotBeNullOrWhiteSpace();
    }

    [GlmIntegrationFact]
    public async Task StreamAsync_WithVideoUrl_StreamsChunks()
    {
        var provider = CreateGlmProvider();

        var request = new ModelRequest
        {
            Model = GlmIntegrationFactAttribute.DefaultModel,
            Messages =
            [
                new Message
                {
                    Role = MessageRole.User,
                    Content =
                    [
                        new TextContent { Text = "用一句话描述视频内容。" },
                        VideoContent.FromUrl("https://cdn.bigmodel.cn/agent-demos/lark/113123.mov")
                    ]
                }
            ],
            MaxTokens = 100
        };

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(request))
            chunks.Add(chunk);

        PrintStreamChunks(chunks);

        chunks.Should().NotBeEmpty();
        chunks.Should().Contain(c => c.Type == StreamChunkType.TextDelta);
        chunks.Should().Contain(c => c.Type == StreamChunkType.MessageStop);
    }

    // -------------------------------------------------------------------------
    // FileContent
    // -------------------------------------------------------------------------

    [GlmIntegrationFact]
    public async Task CompleteAsync_WithFileUrl_ReturnsNonEmptyResponse()
    {
        var provider = CreateGlmProvider();

        var request = new ModelRequest
        {
            Model = GlmIntegrationFactAttribute.DefaultModel,
            Messages =
            [
                new Message
                {
                    Role = MessageRole.User,
                    Content =
                    [
                        new TextContent { Text = "用一句话总结文件内容。" },
                        FileContent.FromUrl("https://cdn.bigmodel.cn/static/demo/demo2.txt")
                    ]
                }
            ],
            MaxTokens = 100
        };

        var response = await provider.CompleteAsync(request);
        PrintResponse(response);

        response.Should().NotBeNull();
        response.Content.Should().NotBeEmpty();
        response.Content.OfType<TextContent>().Should().ContainSingle();
        response.Content.OfType<TextContent>().Single().Text.Should().NotBeNullOrWhiteSpace();
    }

    // -------------------------------------------------------------------------
    // Mixed content (text + single media type only)
    // GLM does not support mixing document, video, and image in one request.
    // -------------------------------------------------------------------------

    [GlmIntegrationFact]
    public async Task CompleteAsync_WithTextAndVideo_ReturnsNonEmptyResponse()
    {
        var provider = CreateGlmProvider();

        var request = new ModelRequest
        {
            Model = GlmIntegrationFactAttribute.DefaultModel,
            Messages =
            [
                new Message
                {
                    Role = MessageRole.User,
                    Content =
                    [
                        new TextContent { Text = "用一句话描述视频内容。" },
                        VideoContent.FromUrl("https://cdn.bigmodel.cn/agent-demos/lark/113123.mov")
                    ]
                }
            ],
            MaxTokens = 100
        };

        var response = await provider.CompleteAsync(request);
        PrintResponse(response);

        response.Should().NotBeNull();
        response.Content.OfType<TextContent>().Single().Text.Should().NotBeNullOrWhiteSpace();
    }

    [GlmIntegrationFact]
    public async Task CompleteAsync_WithTextAndFile_ReturnsNonEmptyResponse()
    {
        var provider = CreateGlmProvider();

        var request = new ModelRequest
        {
            Model = GlmIntegrationFactAttribute.DefaultModel,
            Messages =
            [
                new Message
                {
                    Role = MessageRole.User,
                    Content =
                    [
                        new TextContent { Text = "用一句话总结文件内容。" },
                        FileContent.FromUrl("https://cdn.bigmodel.cn/static/demo/demo2.txt")
                    ]
                }
            ],
            MaxTokens = 100
        };

        var response = await provider.CompleteAsync(request);
        PrintResponse(response);

        response.Should().NotBeNull();
        response.Content.OfType<TextContent>().Single().Text.Should().NotBeNullOrWhiteSpace();
    }
}
