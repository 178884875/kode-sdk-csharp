using KodaClaw.BrowserHub.Connection;
using KodaClaw.BrowserHub.Device;
using KodaClaw.BrowserHub.Screenshot;
using KodaClaw.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KodaClaw.BrowserHub;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKodaClawBrowserHub(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<BridgeConnectionManager>();
        services.TryAddSingleton<BrowserDeviceStore>(sp =>
        {
            var wsOptions = sp.GetRequiredService<KodaClawWorkspaceOptions>();
            return new BrowserDeviceStore(wsOptions.ResolveRootPath());
        });
        services.TryAddSingleton<DevicePairingService>();
        services.TryAddSingleton<DeviceAuthService>();
        services.TryAddSingleton<BridgeConnectionHandler>();
        services.TryAddSingleton<IBrowserHubService, BrowserHubService>();
        services.TryAddSingleton<ScreenshotUploadService>(sp =>
        {
            var wsOptions = sp.GetRequiredService<KodaClawWorkspaceOptions>();
            return new ScreenshotUploadService(wsOptions.ResolveRootPath());
        });
        return services;
    }
}
