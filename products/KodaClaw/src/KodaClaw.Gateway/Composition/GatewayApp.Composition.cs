using KodaClaw.Automation;
using KodaClaw.ChannelHub;
using KodaClaw.Contracts;
using KodaClaw.ControlPlane;
using KodaClaw.Gateway.Channels;
using KodaClaw.Gateway;
using KodaClaw.Gateway.Plugins;
using KodaClaw.McpHub;
using KodaClaw.ModelHub;
using KodaClaw.PluginHost;
using KodaClaw.Runtime;
using KodaClaw.Runtime.Diagnostics;
using KodaClaw.Storage.Json;
using KodaClaw.Workspace;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

public static partial class GatewayApp
{
    private static void ConfigureGatewayServices(
        WebApplicationBuilder builder,
        RuntimeConfigurationSnapshot runtimeBootstrap,
        IReadOnlySet<string> configuredCorsOrigins,
        Action<IServiceCollection>? configureServices)
    {
        var workspaceRoot = KodaClawWorkspaceOptions.ResolveRootPathStatic(
            builder.Configuration["KODACLAW_WORKSPACE_ROOT"]
            ?? builder.Configuration["Workspace:RootPath"]);

        builder.Host.UseSerilog((ctx, cfg) =>
        {
            var logDir = Path.Combine(workspaceRoot, "logs");
            Directory.CreateDirectory(logDir);
            cfg.ReadFrom.Configuration(ctx.Configuration)
               .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
               .WriteTo.File(
                   Path.Combine(logDir, "gateway-.log"),
                   rollingInterval: RollingInterval.Day,
                   retainedFileCountLimit: 7,
                   outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
               .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
               .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning);
        });

        builder.Services.AddKodaClawControlPlane(workspaceRoot);
        builder.Services.AddCors(options =>
        {
            options.AddPolicy(GatewayCorsPolicyName, policy =>
            {
                policy
                    .SetIsOriginAllowed(origin => IsAllowedCorsOrigin(origin, configuredCorsOrigins))
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .WithExposedHeaders(CorrelationHeaderName);
            });
        });
        builder.Services.AddKodaClawWorkspace(options =>
        {
            options.RootPath = builder.Configuration["KODACLAW_WORKSPACE_ROOT"]
                ?? builder.Configuration["Workspace:RootPath"];
        });
        builder.Services.AddSingleton<GatewayAuthTokenAccessor>();
        builder.Services.AddSingleton<ModelPresetService>();
        builder.Services.AddSingleton<PersonaPresetService>();
        builder.Services.AddSingleton<ModelConnectionTestService>();
        builder.Services.AddSingleton<OnboardingStateService>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<SecretMigrationReportService>();
        builder.Services.AddSingleton<WorkspaceBackupService>();
        builder.Services.AddSingleton<WorkspaceRepairService>();
        builder.Services.AddSingleton<SessionRetentionService>();
        builder.Services.AddHostedService<SessionRetentionHostedService>();
        builder.Services.AddSingleton<SandboxRiskOverviewService>();
        builder.Services.AddSingleton<UpdateStateService>();
        builder.Services.AddSingleton<DiagnosticBundleService>();
        builder.Services.AddSingleton<ChannelInboundGatewayService>();

        if (IsStartupRepairEnabled(builder.Configuration))
        {
            builder.Services.AddHostedService<StartupRepairHostedService>();
        }
        builder.Services.AddHostedService<ModelRegistrySeedService>();
        builder.Services.AddSingleton<ChannelConnectorHostedService>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<ChannelConnectorHostedService>());
        builder.Services.AddSingleton<IChannelConnectorRegistry>(
            provider => provider.GetRequiredService<ChannelConnectorHostedService>());

        builder.Services.AddKodaClawJsonStore(workspaceRoot);
        builder.Services.AddKodaClawAutomation(options => options.Enabled = true);
        builder.Services.AddKodaClawChannelHub();
        builder.Services.AddModelRegistry();
        builder.Services.AddKodaClawMcpHub();
        builder.Services.AddKodaClawPluginHost();
        builder.Services.AddSingleton<IPluginGatewayService, PluginGatewayService>();
        builder.Services.AddSingleton<IRuntimeConfigurationResolver, GatewayRuntimeConfigurationResolver>();
        builder.Services.AddKodaClawRuntime(options =>
        {
            options.DefaultModel = runtimeBootstrap.DefaultModel;
            options.OpenAIApiKey = runtimeBootstrap.OpenAIApiKey;
            options.OpenAIBaseUrl = runtimeBootstrap.OpenAIBaseUrl;
            options.AnthropicApiKey = runtimeBootstrap.AnthropicApiKey;
            options.AnthropicBaseUrl = runtimeBootstrap.AnthropicBaseUrl;
        });

        // MetricsBridgeService bridges SDK Meter events to IDiagnosticsService.
        builder.Services.AddHostedService<MetricsBridgeService>();

        configureServices?.Invoke(builder.Services);
    }

    private static void ConfigureGatewayMiddleware(WebApplication app)
    {
        var diagnosticsService = app.Services.GetRequiredService<IDiagnosticsService>();
        var correlationContextAccessor = app.Services.GetRequiredService<ICorrelationContextAccessor>();
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        loggerFactory.AddProvider(new DiagnosticsLoggerProvider(diagnosticsService, correlationContextAccessor));


        app.Use(async (context, next) =>
        {
            var correlationId = GetOrCreateCorrelationId(context);
            var correlationContextAccessor = context.RequestServices.GetRequiredService<ICorrelationContextAccessor>();
            correlationContextAccessor.CorrelationId = correlationId;
            context.Items[CorrelationHeaderName] = correlationId;
            context.Response.Headers[CorrelationHeaderName] = correlationId;

            try
            {
                await next();
            }
            finally
            {
                correlationContextAccessor.CorrelationId = null;
            }
        });

        app.UseCors(GatewayCorsPolicyName);
    }

    private static void MapGatewayEndpoints(WebApplication app)
    {
        MapSystemEndpoints(app);
        MapChatAndDiagnosticsEndpoints(app);
        MapModelEndpoints(app);
        MapSettingsEndpoints(app);
        MapAutomationEndpoints(app);
        MapPluginEndpoints(app);
        MapCanvasEndpoints(app);
        MapSkillsEndpoints(app);
        MapApprovalEndpoints(app);
        MapSessionEndpoints(app);
        MapInboxEndpoints(app);
        MapChannelEndpoints(app);
        MapWorkspaceEndpoints(app);
        MapWorkspaceGitEndpoints(app);
        MapMcpServersEndpoints(app);
        MapMemoryEndpoints(app);
        MapMediaEndpoints(app);
        MapAutomationNotificationEndpoints(app);
        MapWeChatAuthEndpoints(app);
        MapSystemEventsEndpoints(app);
        MapRootEndpoint(app);
    }

    private static bool IsStartupRepairEnabled(IConfiguration configuration)
    {
        return !string.Equals(
            configuration["KODACLAW_STARTUP_REPAIR_ENABLED"]
                ?? configuration["Gateway:StartupRepairEnabled"],
            "false",
            StringComparison.OrdinalIgnoreCase);
    }
}
