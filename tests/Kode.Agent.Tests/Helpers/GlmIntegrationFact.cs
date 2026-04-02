using Xunit;

namespace Kode.Agent.Tests.Helpers;

/// <summary>
/// Integration test fact that only runs when the <c>ZHIPU_API_KEY</c>
/// environment variable is set.  Tests are silently skipped in CI unless
/// the key is provided.
/// </summary>
public sealed class GlmIntegrationFactAttribute : FactAttribute
{
    internal const string EnvVar = "ZHIPU_API_KEY";
    internal const string GlmBaseUrl = "https://open.bigmodel.cn/api/paas/v4/";
    internal const string DefaultModel = "glm-5v-turbo";

    public GlmIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Set {EnvVar} environment variable to run GLM integration tests.";
    }
}
