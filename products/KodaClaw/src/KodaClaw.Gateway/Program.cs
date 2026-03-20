using KodaClaw.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

var app = GatewayApp.Build(args);
app.Run();

public partial class Program;

public static partial class GatewayApp
{
    public static WebApplication Build(
        string[]? args = null,
        Action<IServiceCollection>? configureServices = null,
        Action<IConfigurationBuilder>? configureConfiguration = null)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        GatewayConfigurationBootstrap.LoadCurrentDirectoryDotEnvFiles(currentDirectory);

        var builder = WebApplication.CreateBuilder(args ?? []);
        GatewayConfigurationBootstrap.ApplyCurrentDirectoryJsonFiles(
            builder.Configuration,
            currentDirectory,
            builder.Environment.EnvironmentName);
        configureConfiguration?.Invoke(builder.Configuration);
        var runtimeBootstrap = RuntimeConfigurationBootstrap.Resolve(builder.Configuration);
        var configuredCorsOrigins = GetConfiguredCorsOrigins(builder.Configuration);

        ConfigureGatewayServices(builder, runtimeBootstrap, configuredCorsOrigins, configureServices);

        var app = builder.Build();
        ConfigureGatewayMiddleware(app);
        MapGatewayEndpoints(app);

        return app;
    }

}

internal static class GatewayJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
