using FluentAssertions;
using Kode.Agent.Tools.Orchestration.Internal;
using Xunit;

namespace Kode.Agent.Tests.Unit.Orchestration;

public sealed class SpawnDepthTrackerTests
{
    [Fact]
    public void Current_Starts_At_Zero()
    {
        // AsyncLocal is per async context; in a fresh test it should be 0
        SpawnDepthTracker.Current.Should().Be(0);
    }

    [Fact]
    public void Enter_Increments_Depth()
    {
        using var scope = SpawnDepthTracker.Enter();
        SpawnDepthTracker.Current.Should().Be(1);
    }

    [Fact]
    public void Enter_Nested_Increments_Further()
    {
        using var outer = SpawnDepthTracker.Enter();
        using var inner = SpawnDepthTracker.Enter();
        SpawnDepthTracker.Current.Should().Be(2);
    }

    [Fact]
    public void Dispose_Decrements_Depth()
    {
        var scope = SpawnDepthTracker.Enter();
        SpawnDepthTracker.Current.Should().Be(1);
        scope.Dispose();
        SpawnDepthTracker.Current.Should().Be(0);
    }

    [Fact]
    public async Task Enter_Does_Not_Leak_To_Sibling_Tasks()
    {
        int siblingDepth = -1;

        // Start a sibling task that reads depth BEFORE we enter our scope
        var siblingReady = new TaskCompletionSource();
        var siblingContinue = new TaskCompletionSource();

        var siblingTask = Task.Run(async () =>
        {
            siblingReady.SetResult();
            await siblingContinue.Task;
            siblingDepth = SpawnDepthTracker.Current;
        });

        await siblingReady.Task;

        using (SpawnDepthTracker.Enter())
        {
            SpawnDepthTracker.Current.Should().Be(1);
            siblingContinue.SetResult();
            await siblingTask;
        }

        // Sibling ran while our scope was active but should NOT see depth=1
        siblingDepth.Should().Be(0);
    }

    [Fact]
    public async Task Enter_Is_Visible_In_Child_Async_Context()
    {
        int childDepth = -1;

        using (SpawnDepthTracker.Enter())
        {
            // Child task inherits AsyncLocal value from the point of Enter()
            await Task.Run(() => { childDepth = SpawnDepthTracker.Current; });
        }

        // AsyncLocal flows to child tasks started WITHIN the scope
        childDepth.Should().Be(1);
    }
}
