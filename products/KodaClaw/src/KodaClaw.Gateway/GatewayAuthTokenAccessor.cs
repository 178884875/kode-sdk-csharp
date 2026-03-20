using KodaClaw.Contracts;
using Microsoft.Extensions.Configuration;

namespace KodaClaw.Gateway;

internal sealed class GatewayAuthTokenAccessor
{
    private readonly IConfiguration _configuration;
    private readonly ISecretStore _secretStore;
    private readonly object _sync = new();
    private string? _cachedToken;
    private bool _loaded;

    public GatewayAuthTokenAccessor(IConfiguration configuration, ISecretStore secretStore)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    public string? GetConfiguredToken()
    {
        if (_loaded)
        {
            return _cachedToken;
        }

        lock (_sync)
        {
            if (_loaded)
            {
                return _cachedToken;
            }

            _cachedToken = LoadConfiguredToken();
            _loaded = true;
            return _cachedToken;
        }
    }

    private string? LoadConfiguredToken()
    {
        var secretRefValue = _configuration["KODACLAW_GATEWAY_TOKEN_SECRET_REF"]
            ?? _configuration["Gateway:TokenSecretRef"];
        if (SecretRef.TryParse(secretRefValue, out var secretRef))
        {
            var resolvedFromSecretStore = _secretStore.GetAsync(secretRef).GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(resolvedFromSecretStore))
            {
                return resolvedFromSecretStore.Trim();
            }
        }

        var configuredToken = _configuration["KODACLAW_GATEWAY_TOKEN"] ?? _configuration["Gateway:Token"];
        return string.IsNullOrWhiteSpace(configuredToken)
            ? null
            : configuredToken.Trim();
    }
}
