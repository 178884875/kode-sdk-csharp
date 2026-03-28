using KodaClaw.Runtime;
using Microsoft.Extensions.Configuration;

namespace KodaClaw.Gateway;

internal sealed class GatewayRuntimeConfigurationResolver : IRuntimeConfigurationResolver
{
    private readonly IConfiguration _configuration;

    public GatewayRuntimeConfigurationResolver(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public RuntimeConfigurationSnapshot Resolve()
    {
        return RuntimeConfigurationBootstrap.Resolve(_configuration);
    }
}
