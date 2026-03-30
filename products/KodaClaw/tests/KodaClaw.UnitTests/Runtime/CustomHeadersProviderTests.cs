using FluentAssertions;
using Kode.Agent.Sdk.Infrastructure.Providers;
using Xunit;

namespace KodaClaw.UnitTests.Runtime;

/// <summary>
/// L1 单元测试 — CustomHeaders 对 AnthropicProvider / OpenAIProvider 构造函数的影响
/// </summary>
public sealed class CustomHeadersProviderTests
{
    // ── AnthropicProvider ─────────────────────────────────────────────────────

    [Fact]
    public void AnthropicProvider_constructs_without_custom_headers()
    {
        var options = new AnthropicOptions { ApiKey = "sk-test" };
        var act = () => new AnthropicProvider(options);
        act.Should().NotThrow();
    }

    [Fact]
    public void AnthropicProvider_constructs_with_custom_headers()
    {
        var options = new AnthropicOptions
        {
            ApiKey = "sk-test",
            CustomHeaders = new Dictionary<string, string>
            {
                ["User-Agent"] = "claude-code/0.1.0"
            }
        };

        var act = () => new AnthropicProvider(options);
        act.Should().NotThrow();
    }

    [Fact]
    public void AnthropicProvider_constructs_with_empty_custom_headers_dict()
    {
        var options = new AnthropicOptions
        {
            ApiKey = "sk-test",
            CustomHeaders = new Dictionary<string, string>()
        };

        var act = () => new AnthropicProvider(options);
        act.Should().NotThrow();
    }

    // ── OpenAIProvider ────────────────────────────────────────────────────────

    [Fact]
    public void OpenAIProvider_constructs_without_custom_headers()
    {
        var options = new OpenAIOptions { ApiKey = "sk-test" };
        var act = () => new OpenAIProvider(options);
        act.Should().NotThrow();
    }

    [Fact]
    public void OpenAIProvider_constructs_with_user_agent_custom_header()
    {
        var options = new OpenAIOptions
        {
            ApiKey = "sk-test",
            CustomHeaders = new Dictionary<string, string>
            {
                ["User-Agent"] = "claude-code/0.1.0"
            }
        };

        var act = () => new OpenAIProvider(options);
        act.Should().NotThrow();
    }

    [Fact]
    public void OpenAIProvider_constructs_with_base_url_and_custom_headers()
    {
        var options = new OpenAIOptions
        {
            ApiKey = "sk-test",
            BaseUrl = "https://proxy.example.com/v1",
            CustomHeaders = new Dictionary<string, string>
            {
                ["User-Agent"] = "my-agent/1.0",
                ["X-Custom"] = "value"
            }
        };

        var act = () => new OpenAIProvider(options);
        act.Should().NotThrow();
    }
}
