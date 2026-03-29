using System.Diagnostics;
using FluentAssertions;
using Kode.Agent.Sdk.Diagnostics;
using Xunit;

namespace Kode.Agent.Tests.Unit.Diagnostics;

public sealed class KodeAgentActivitySourceTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public KodeAgentActivitySourceTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == KodeAgentActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void SourceName_should_be_Kode_Agent()
    {
        KodeAgentActivitySource.SourceName.Should().Be("Kode.Agent");
    }

    [Fact]
    public void StartActivity_should_create_activity_when_listener_is_registered()
    {
        using var activity = KodeAgentActivitySource.Source.StartActivity("agent.run");

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("agent.run");
    }

    [Fact]
    public void Activity_should_support_tags()
    {
        using var activity = KodeAgentActivitySource.Source.StartActivity("agent.step");
        activity?.SetTag("step.number", 3);

        activity.Should().NotBeNull();
        activity!.GetTagItem("step.number").Should().Be(3);
    }

    [Fact]
    public void Activity_should_support_error_status()
    {
        using var activity = KodeAgentActivitySource.Source.StartActivity("agent.tool.execute");
        activity?.SetStatus(ActivityStatusCode.Error, "Tool failed");

        activity.Should().NotBeNull();
        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("Tool failed");
    }

    [Fact]
    public void Nested_activities_should_form_parent_child_hierarchy()
    {
        using var parent = KodeAgentActivitySource.Source.StartActivity("agent.run");
        using var child = KodeAgentActivitySource.Source.StartActivity("agent.step");

        parent.Should().NotBeNull();
        child.Should().NotBeNull();
        child!.ParentId.Should().Be(parent!.Id);
    }
}
