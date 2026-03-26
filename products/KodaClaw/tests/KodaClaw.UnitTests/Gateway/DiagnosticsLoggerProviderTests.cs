using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.ControlPlane;
using KodaClaw.Gateway;
using Microsoft.Extensions.Logging;
using Xunit;

namespace KodaClaw.UnitTests.Gateway;

public sealed class DiagnosticsLoggerProviderTests
{
    private static InMemoryDiagnosticsService CreateService() => new();

    [Fact]
    public void Warning_IsRecorded()
    {
        var svc = CreateService();
        var provider = new DiagnosticsLoggerProvider(svc);
        var logger = provider.CreateLogger("KodaClaw.Gateway.SomeService");

        logger.LogWarning("test warning {Value}", 42);

        var events = svc.GetRecent(10);
        events.Should().ContainSingle(e =>
            e.Level == "warning" &&
            e.Message.Contains("test warning") &&
            e.Source == "gateway");
    }

    [Fact]
    public void Error_IsRecorded()
    {
        var svc = CreateService();
        var provider = new DiagnosticsLoggerProvider(svc);
        var logger = provider.CreateLogger("KodaClaw.Runtime.ChannelSessionService");

        logger.LogError("stream failed");

        var events = svc.GetRecent(10);
        events.Should().ContainSingle(e =>
            e.Level == "error" &&
            e.Source == "runtime");
    }

    [Fact]
    public void Info_IsNotRecorded()
    {
        var svc = CreateService();
        var provider = new DiagnosticsLoggerProvider(svc);
        var logger = provider.CreateLogger("KodaClaw.Gateway.SomeService");

        logger.LogInformation("this should not appear");

        svc.GetRecent(10).Should().BeEmpty();
    }

    [Fact]
    public void MicrosoftAspNetCore_MapsToAspnetcore()
    {
        var svc = CreateService();
        var provider = new DiagnosticsLoggerProvider(svc);
        var logger = provider.CreateLogger("Microsoft.AspNetCore.Routing.EndpointMiddleware");

        logger.LogWarning("route not found");

        var events = svc.GetRecent(10);
        events.Should().ContainSingle(e => e.Source == "aspnetcore");
    }
}
