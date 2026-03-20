using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ModelProviderKind>))]
public enum ModelProviderKind
{
    OpenAI = 0,
    Anthropic = 1,
    OpenAICompatible = 2,
    AnthropicCompatible = 3,
}
